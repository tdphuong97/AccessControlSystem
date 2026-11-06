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
            Db.Devices.Update(created with { FpGroup = "device-" + sid });
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
        catch (HikException ex)
        {
            return new DeviceTestResult(false, dev.Model, dev.Firmware, 0, false, false, ex.Message);
        }
    }

    private object? DevicesDelete(JsonElement a)
    {
        Db.Devices.Delete(Int(a, "id"));
        Sync.Wake();
        Emit("devices.changed", DeviceViews());
        return null;
    }

    private async Task<ReconcilePlan> ReconcilePlanAsync()
    {
        var plan = await Reconciler.BuildAsync(new Progress<string>(m => Emit("reconcile.progress", new { message = m })), _cts.Token);
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

    private async Task<ImportSummary> ImportStart(JsonElement a)
    {
        var id = Int(a, "deviceId");
        try
        {
            return await Importer.ImportAllAsync(id,
                new Progress<ImportProgress>(p => Emit("import.progress", new { done = p.Done, total = p.Total, currentName = p.CurrentName })),
                _cts.Token);
        }
        finally
        {
            // Imported people exist only on the source device: the others get them through So khớp.
            try { if (Db.Devices.List().Count > 1 && Db.People.Count() > 0) SetNeedsReconcile(true); } catch { /* the hint is best-effort */ }
            Sync.Wake();
        }
    }

    private IReadOnlyList<LogRow> LogList(JsonElement a)
    {
        var names = Db.Devices.List().ToDictionary(d => d.Id, d => d.Name);
        var limit = Math.Clamp(OptInt(a, "limit") ?? 200, 1, 1000);
        return Db.Log.List(OptStr(a, "employeeNo"), OptInt(a, "deviceId"), limit)
            .Select(l => new LogRow(l.At, l.DeviceId is { } d ? names.GetValueOrDefault(d) : null, l.EmployeeNo, LogAction(l.Action), l.Ok, l.Message))
            .ToList();
    }
}
