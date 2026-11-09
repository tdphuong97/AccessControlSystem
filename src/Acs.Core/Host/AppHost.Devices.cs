using System.Text.Json;
using Acs.Core.Backup;
using Acs.Core.Hik;
using Acs.Core.Import;
using Acs.Core.Model;
using Acs.Core.Reconcile;
using Acs.Core.Sync;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    private ReconcilePlan? _plan;
    private Task<ReconcilePlan>? _planRun;
    private readonly object _importLock = new();
    private ImportStatusView? _importRun;   // null = no import running

    /// <summary>Reports synchronously, in order, before the command returns. Progress&lt;T&gt; posts to the thread pool,
    /// so its events could arrive out of order or after the result (the page then ignores them).</summary>
    private sealed class InOrder<T>(Action<T> act) : IProgress<T>
    {
        public void Report(T value) => act(value);
    }

    /// <summary>Settings key behind app.state.needsReconcile ("1" = ask the admin to run So khớp).</summary>
    public const string NeedsReconcileKey = "reconcile.needed";

    private void SetNeedsReconcile(bool on) => Db.Settings.Set(NeedsReconcileKey, on ? "1" : "0");

    private AppStateView AppState()
    {
        var devices = DeviceViews();
        var cutoff = Now - TimeSpan.FromDays(180);
        var stale = Db.People.List(null, PersonStatus.Locked).Count(p => p.LockedAt is { } at && at < cutoff);
        return new AppStateView(_version, devices.Count == 0, devices, _foundUpdate is null ? null : ToView(_foundUpdate),
            devices.Sum(d => d.Pending), devices.Sum(d => d.Failed), stale,
            devices.Count > 0 && Db.Settings.Get(NeedsReconcileKey) == "1");
    }

    private List<DeviceView> DeviceViews()
    {
        var status = Sync.Status().ToDictionary(s => s.DeviceId);
        return Db.Devices.List().Select(d => ViewOf(d, status.GetValueOrDefault(d.Id))).ToList();
    }

    private static DeviceView ViewOf(DeviceRecord d, DeviceStatus? s)
    {
        var online = s?.Online ?? false;
        var paused = s?.AuthPaused ?? d.AuthPaused;
        var failed = s?.Failed ?? 0;
        string? message = paused ? s?.Message ?? JobRules.AuthMessage
            : !online ? s?.Message ?? JobRules.OfflineMessage
            : failed > 0 ? FailedMessage
            : s?.Message;
        return new DeviceView(d.Id, d.Name, d.Host, d.Username, d.DoorNo, d.IsCaptureDefault, d.IsFingerReader, d.Model,
            online, paused, s?.LastSeen ?? d.LastSeen, s?.Pending ?? 0, failed, message);
    }

    private DeviceView DevicesSave(JsonElement a)
    {
        var name = (OptStr(a, "name") ?? "").Trim();
        var host = (OptStr(a, "host") ?? "").Trim();
        var user = (OptStr(a, "username") ?? "").Trim();
        var password = OptStr(a, "password") ?? "";
        if (name.Length == 0) throw new HostException("Nhập tên máy, ví dụ \"Máy tầng trệt\".");
        if (host.Length == 0) throw new HostException("Nhập địa chỉ IP của máy.");
        if (user.Length == 0) throw new HostException("Nhập tên đăng nhập của máy.");
        var door = OptInt(a, "doorNo") ?? 1;
        var capture = Bool(a, "isCaptureDefault");
        var reader = Bool(a, "isFingerReader");

        if (door < 1) throw new HostException("Số cửa phải từ 1 trở lên.");
        var id = Db.Tx(() =>
        {
        int sid;
        if (OptInt(a, "id") is { } existingId)
        {
            var cur = Db.Devices.Get(existingId) ?? throw new HostException("Không tìm thấy máy chấm công.");
            var changedLogin = password.Length > 0 || host != cur.Host || user != cur.Username;
            if (host != cur.Host)
            {
                // A new address = possibly another (replaced or reset) unit: nothing confirmed on the old one may show
                // as ✓ for it. So khớp reads the new unit and sends what is missing.
                Db.Jobs.DeleteStates(existingId);
                if (Db.People.Count() > 0) SetNeedsReconcile(true);
            }
            Db.Devices.Update(cur with
            {
                Name = name, Host = host, Username = user, Password = password.Length > 0 ? password : cur.Password,
                DoorNo = door, IsCaptureDefault = capture, IsFingerReader = reader,
                AuthPaused = changedLogin ? false : cur.AuthPaused,
            });
            sid = existingId;
        }
        else
        {
            if (password.Length == 0) throw new HostException("Nhập mật khẩu của máy.");
            sid = Db.Devices.Insert(new DeviceRecord(0, name, host, user, password, door, capture, reader, null, null, null,
                "device-0", false, null));
            var created = Db.Devices.Get(sid)!;
            // Dữ liệu chung: fp_group must be unique across PCs (it names the device in the cloud), "device-<id>" is not.
            var group = CloudEnabled ? "device-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() : "device-" + sid;
            Db.Devices.Update(created with { FpGroup = group });
            // A new device starts empty as far as the app knows: nothing is queued for it, So khớp sends the data.
            if (Db.People.Count() > 0 || Db.Devices.List().Count > 1) SetNeedsReconcile(true);
        }
        if (capture)
            foreach (var other in Db.Devices.List().Where(d => d.Id != sid && d.IsCaptureDefault))
                Db.Devices.Update(other with { IsCaptureDefault = false });
        return sid;
        });
        Sync.Wake();
        var view = DeviceViews().First(d => d.Id == id);
        Emit("devices.changed", DeviceViews());
        return view;
    }

    private async Task<DeviceTestResult> DevicesTest(JsonElement a)
    {
        var dev = Db.Devices.Get(Int(a, "id")) ?? throw new HostException("Không tìm thấy máy chấm công.");
        // Paused for a wrong password and the login was not changed since (devices.save clears the pause when it is):
        // testing again would only be one more failed login towards the IP lock-out.
        if (dev.AuthPaused) return new DeviceTestResult(false, dev.Model, dev.Firmware, 0, false, false, JobRules.AuthMessage);
        try
        {
            var hik = _factory.Create(dev);
            var info = await hik.GetDeviceInfoAsync(_cts.Token);
            var caps = await hik.GetCapabilitiesAsync(_cts.Token);
            var users = 0;
            await foreach (var _ in hik.ListUsersAsync(_cts.Token)) users++;
            Db.Devices.SetSeen(dev.Id, info, Now);
            return new DeviceTestResult(true, info.Model, info.Firmware, users, caps.CaptureFace != false,
                caps.CaptureFingerprint != false, $"Kết nối tốt: {info.Model}, {users} người trên máy.");
        }
        catch (HikAuthException ex)
        {
            AuthGuard.Pause(Db, dev.Id, Sync);
            Emit("devices.changed", DeviceViews());
            return new DeviceTestResult(false, dev.Model, dev.Firmware, 0, false, false, ex.Message);
        }
        catch (HikException ex)
        {
            return new DeviceTestResult(false, dev.Model, dev.Firmware, 0, false, false, ex.Message);
        }
    }

    public const string DiagAllGood = "Máy hoạt động bình thường.";

    /// <summary>
    /// "Chẩn đoán": checks one device step by step (login, what it can do over the network, camera, the send queue, the
    /// last failed capture) and says what to do for each problem. Stops at the first step the rest depends on.
    /// Never logs in to a device paused for a wrong password, and never runs beside a capture on that device.
    /// </summary>
    private async Task<DiagReport> DevicesDiagnose(JsonElement a)
    {
        var dev = Db.Devices.Get(Int(a, "id")) ?? throw new HostException("Không tìm thấy máy chấm công.");
        var checks = new List<DiagCheck>();
        DiagReport Done()
        {
            var bad = checks.FirstOrDefault(c => c.State == "err");
            var summary = bad is not null ? $"Có vấn đề: {bad.Name.ToLowerInvariant()}."
                : checks.Any(c => c.State == "warn") ? "Máy dùng được, có điểm cần để ý." : DiagAllGood;
            return new DiagReport(dev.Id, dev.Name, summary, checks);
        }

        if (dev.AuthPaused)
        {
            checks.Add(new("Kết nối và đăng nhập", "err", "App đang dừng gửi cho máy này vì máy báo sai mật khẩu.",
                "Bấm Sửa mật khẩu, nhập đúng mật khẩu của máy rồi Lưu máy. Không thử nhiều lần: máy khoá đăng nhập sau 5 lần sai."));
            return Done();
        }
        if (Enroll.IsCapturing(dev.Id))
        {
            checks.Add(new("Máy đang bận", "warn", "Máy đang chụp mặt hoặc quét vân tay.", "Đợi xong rồi bấm Chẩn đoán lại."));
            return Done();
        }

        var hik = _factory.Create(dev);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var info = await hik.GetDeviceInfoAsync(_cts.Token);
            Db.Devices.SetSeen(dev.Id, info, Now);
            checks.Add(new("Kết nối và đăng nhập", sw.ElapsedMilliseconds > 3000 ? "warn" : "ok",
                $"Máy trả lời sau {sw.ElapsedMilliseconds} ms: {info.Model}, firmware {info.Firmware}.",
                sw.ElapsedMilliseconds > 3000 ? "Mạng tới máy đang chậm. Kiểm tra dây mạng hoặc switch giữa máy tính và máy." : null));
        }
        catch (HikAuthException ex)
        {
            AuthGuard.Pause(Db, dev.Id, Sync);
            Emit("devices.changed", DeviceViews());
            checks.Add(new("Kết nối và đăng nhập", "err", ex.Message, "Bấm Sửa mật khẩu, nhập đúng mật khẩu của máy rồi Lưu máy."));
            return Done();
        }
        catch (HikException ex)
        {
            checks.Add(new("Kết nối và đăng nhập", "err", ex.Message,
                $"Kiểm tra máy có điện, dây mạng cắm chắc, và máy tính này cùng mạng với địa chỉ {dev.Host}. Xong bấm Chẩn đoán lại."));
            return Done();
        }

        try
        {
            var caps = await hik.GetCapabilitiesAsync(_cts.Token);
            checks.Add(caps.CaptureFace == false
                ? new("Chụp mặt qua mạng", "err", "Máy không cho chụp mặt qua mạng.", "Chụp bằng webcam máy tính hoặc chọn ảnh có sẵn, hoặc cập nhật firmware máy.")
                : new("Chụp mặt qua mạng", "ok", "Máy cho chụp mặt qua mạng.", null));
            checks.Add(caps.CaptureFingerprint == false
                ? new("Quét vân tay qua mạng", "err", "Máy không cho quét vân tay qua mạng.", "Quét vân tay ở máy khác có cảm biến, hoặc cập nhật firmware máy.")
                : new("Quét vân tay qua mạng", "ok", "Máy cho quét vân tay qua mạng.", null));
        }
        catch (HikException ex)
        {
            checks.Add(new("Chụp mặt và quét vân tay qua mạng", "warn", ex.Message, "Vẫn thử chụp, quét được; nếu lỗi thì khởi động lại máy."));
        }

        sw.Restart();
        try
        {
            var frame = await hik.SnapshotAsync(_cts.Token);
            checks.Add(new("Camera", "ok", $"Camera gửi hình ({frame.Length / 1024} KB, {sw.ElapsedMilliseconds} ms).", null));
        }
        catch (HikException ex)
        {
            checks.Add(new("Camera", "warn", "Không lấy được hình camera: " + ex.Message,
                "Vẫn bấm Chụp ngay được. Nếu chụp cũng lỗi, khởi động lại máy chấm công."));
        }

        var open = Db.Jobs.ListOpen(dev.Id);
        var failed = open.Where(j => j.Status == JobStatus.Failed).ToList();
        checks.Add(failed.Count > 0
            ? new("Việc gửi xuống máy", "err", $"{failed.Count} việc gửi bị lỗi, ví dụ: {failed[0].ErrorMessage}",
                "Ở tab Người dùng, bấm bộ lọc Chưa đủ 2 máy, mở người có dấu Lỗi gửi để xem cách xử lý, rồi bấm Thử lại.")
            : open.Count > 0
                ? new("Việc gửi xuống máy", "warn", $"Còn {open.Count} việc đang chờ gửi.", "App tự gửi tiếp. Nếu số này không giảm sau vài phút, bấm Thử lại trên thẻ máy.")
                : new("Việc gửi xuống máy", "ok", "Không có việc nào đang chờ hay bị lỗi.", null));

        var lastCapture = Db.Log.List(null, dev.Id, 200)
            .FirstOrDefault(l => l.Action is CaptureFaceAction or CaptureFingerAction && l.At > Now.AddDays(-1));
        if (lastCapture is { Ok: false })
            checks.Add(new("Lần chụp/quét lỗi gần nhất", "warn",
                $"{lastCapture.Action} lúc {lastCapture.At.ToLocalTime():HH:mm}: {lastCapture.Message}",
                lastCapture.Action == CaptureFingerAction
                    ? "Bấm Quét, khi máy báo thì đặt ngón tay ngay giữa cảm biến và giữ yên. Ngón khô quá hoặc ướt quá thì lau rồi quét lại."
                    : "Mời đứng thẳng trước camera, cách khoảng nửa mét, đủ sáng, bỏ khẩu trang, kính râm, mũ."));
        return Done();
    }

    private object? DevicesDelete(JsonElement a)
    {
        Db.Devices.Delete(Int(a, "id"));
        Sync.Wake();
        Emit("devices.changed", DeviceViews());
        return null;
    }

    /// <summary>
    /// A second request while a plan is being built (the screen was left and opened again) waits for that same run:
    /// two runs would read every device twice over, side by side.
    /// </summary>
    private Task<ReconcilePlan> ReconcilePlanAsync()
    {
        lock (_importLock)
        {
            if (_planRun is null || _planRun.IsCompleted) _planRun = BuildPlanAsync();
            return _planRun;
        }
    }

    private async Task<ReconcilePlan> BuildPlanAsync()
    {
        await Task.Yield();   // never run the device reads inside the lock above
        var plan = await Reconciler.BuildAsync(new InOrder<string>(m => Emit("reconcile.progress", new { message = m })), _cts.Token);
        _plan = plan;
        // Nothing to send anywhere (ExtraFingers is information only): the devices already match the app.
        if (!plan.Devices.Any(d => d.Items.Any(i => i.Kind != DiffKind.ExtraFingers))) SetNeedsReconcile(false);
        return plan;
    }

    private async Task<object?> ReconcileApplyAsync(JsonElement a)
    {
        var plan = _plan ?? throw new HostException("Chưa có kế hoạch đối chiếu. Bấm Đối chiếu trước.");
        var map = new Dictionary<string, StrangerChoice>();
        if (Has(a, "strangers") && a.GetProperty("strangers").ValueKind == JsonValueKind.Object)
            foreach (var p in a.GetProperty("strangers").EnumerateObject())
                map[p.Name] = Enum.TryParse<StrangerChoice>(p.Value.GetString(), true, out var c) ? c : StrangerChoice.Skip;
        await Reconciler.ApplyAsync(plan, map, _cts.Token);
        _plan = null;
        SetNeedsReconcile(false);
        Sync.Wake();
        return null;
    }

    private ImportStatusView ImportStatus()
    {
        lock (_importLock) return _importRun ?? new ImportStatusView(false, null, null, 0, 0, null);
    }

    /// <summary>
    /// The import runs on in the host when its screen is left; import.status and the import.done event let a screen
    /// opened later show it. One import at a time: two would read the same device side by side.
    /// </summary>
    private async Task<ImportSummary> ImportStart(JsonElement a)
    {
        var id = Int(a, "deviceId");
        var name = Db.Devices.Get(id)?.Name ?? "máy";
        lock (_importLock)
        {
            if (_importRun is { } running) throw new HostException($"Đang lấy dữ liệu từ {running.DeviceName}. Chờ xong rồi thử lại.");
            _importRun = new ImportStatusView(true, id, name, 0, 0, null);
        }
        var before = Db.People.Count();
        var finished = false;
        ImportSummary? summary = null;
        string? error = null;
        try
        {
            summary = await Importer.ImportAllAsync(id,
                new InOrder<ImportProgress>(p =>
                {
                    lock (_importLock) if (_importRun is not null) _importRun = _importRun with { Done = p.Done, Total = p.Total, CurrentName = p.CurrentName };
                    Emit("import.progress", new { done = p.Done, total = p.Total, currentName = p.CurrentName });
                }),
                _cts.Token);
            finished = true;
            return summary;
        }
        catch (Exception ex)
        {
            error = ex is Acs.Core.Enroll.EnrollException or HikException or HostException ? ex.Message
                : ex is OperationCanceledException ? SlowNetworkMessage : GenericError;
            throw;
        }
        finally
        {
            // Imported data exists only on the source device: the others get it through So khớp (also after a run
            // that stopped half-way but already brought people in).
            try { if (Db.Devices.List().Count > 1 && (finished || Db.People.Count() > before)) SetNeedsReconcile(true); }
            catch { /* the hint is best-effort */ }
            Sync.Wake();
            lock (_importLock) _importRun = null;
            Emit("import.done", new { deviceId = id, deviceName = name, summary, error });
        }
    }

    private IReadOnlyList<LogRow> LogList(JsonElement a)
    {
        var names = Db.Devices.List().ToDictionary(d => d.Id, d => d.Name);
        var limit = Math.Clamp(OptInt(a, "limit") ?? 200, 1, 1000);
        return Db.Log.List(OptStr(a, "employeeNo"), OptInt(a, "deviceId"), limit)
            .Select(l => new LogRow(l.At, l.DeviceId is { } d ? names.GetValueOrDefault(d) : null, l.EmployeeNo, LogAction(l.Action), l.Ok, l.Message, l.Pc))
            .ToList();
    }
}
