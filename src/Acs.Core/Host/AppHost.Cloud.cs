using System.Text.Json;
using Acs.Core.Cloud;
using Acs.Core.Security;

namespace Acs.Core.Host;

public sealed record CloudStatusView(bool Enabled, bool Connected, string? LastSyncAt, int Pending, bool Online, string? Error,
    string? SenderName, bool IsSender, int PeopleCount);

/// <summary>
/// Dữ liệu chung: one encrypted data set shared by several PCs through the acs-sync Worker. While enabled, a background
/// loop pushes queued changes every second, pulls every 15 s and renews the sender lease every 30 s (the PC whose window
/// is open sends to the devices). Disabled, the app behaves exactly as without it and makes no network call.
/// </summary>
public sealed partial class AppHost
{
    public const string CloudEnabledKey = "cloud.enabled";
    public const string RepublishMarkerName = "republish.flag";
    private const string JoinBackupReason = "truoc-du-lieu-chung";
    private const int MinCloudPassword = 8;

    private readonly string _dataFolder;
    private readonly HttpClient? _cloudHttp;
    private HttpClient? _cloudHttpOwned;
    private volatile CloudRuntime? _cloud;
    private volatile bool _windowActive;

    private sealed record CloudRuntime(CloudCode Code, byte[] Key, string PcId, CloudClient Client, CloudSync Sync, LeaseKeeper Lease,
        CancellationTokenSource Stop, Task Loop);

    private bool CloudEnabled => Db.Settings.Get(CloudEnabledKey) == "1" && _keys.GetCloud() is not null;
    private int CloudIterations => _backupIterations ?? CloudCrypto.DefaultIterations;
    private HttpClient CloudHttp => _cloudHttp ?? (_cloudHttpOwned ??= NewCloudHttp());

    private static HttpClient NewCloudHttp()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AccessControlSystem");
        return c;
    }

    /// <summary>Called by the window: an open window takes the send queue from a PC that only runs in the tray.</summary>
    public void SetWindowActive(bool active)
    {
        _windowActive = active;
        if (active && _cloud is { } c) _ = Task.Run(async () => { try { await CloudLeaseTickAsync(); await c.Sync.PullAsync(c.Stop.Token); EmitCloud(); } catch { } });
    }

    // ---------------- commands ----------------

    private CloudStatusView CloudStatusView()
    {
        var c = _cloud;
        var s = c?.Sync.Status;
        return new CloudStatusView(CloudEnabled, _keys.GetCloud() is not null, s?.LastSyncAt is { } t ? LocalStamp(t.ToLocalTime()) : null,
            Db.Outbox.Count(), s?.Online ?? true, s?.Error, c?.Lease.HolderName, Sync.IsSender, Db.People.Count());
    }

    /// <summary>Before connecting: does the shared data exist yet (create a password) or not (enter it)?</summary>
    private async Task<object?> CloudProbe(JsonElement a)
    {
        var client = new CloudClient(CloudHttp, CloudCode.Parse(Str(a, "code")));
        return new { exists = await client.GetSpaceAsync(_cts.Token) is not null, people = Db.People.Count() };
    }

    private async Task<object?> CloudConnect(JsonElement a)
    {
        var code = CloudCode.Parse(Str(a, "code"));
        var password = OptStr(a, "password") ?? "";
        var client = new CloudClient(CloudHttp, code);
        var space = await client.GetSpaceAsync(_cts.Token);
        await StopCloudAsync(release: true);
        if (space is null)
        {
            if (password.Length < MinCloudPassword) throw new HostException("Mật khẩu dữ liệu chung cần ít nhất 8 ký tự.");
            var k = CloudCrypto.NewKey();
            await client.PutSpaceAsync(CloudCrypto.Wrap(k, password, CloudIterations), 0, _cts.Token);
            if (Backup.HasPassword) Backup.Create(JoinBackupReason);
            var secret = new CloudSecret(code.ToString(), k, Guid.NewGuid().ToString("N"));
            _keys.SetCloud(secret);
            Db.Settings.Set(CloudEnabledKey, "1");
            Db.Log.Add(null, null, CloudSync.LogAction, true, "Đã bật dữ liệu chung và đưa dữ liệu máy này lên.");
            var rt = StartCloud(secret);
            await rt.Sync.PublishAllAsync(null, _cts.Token);
            EmitCloud();
            return new { mode = "published", people = Db.People.Count() };
        }
        var key = CloudCrypto.Unwrap(space.Key, password);   // wrong password throws before anything changes
        RequireJoinBackup();
        var joined = new CloudSecret(code.ToString(), key, Guid.NewGuid().ToString("N"));
        _keys.SetCloud(joined);
        var people = await JoinAsync(joined);
        Db.Log.Add(null, null, CloudSync.LogAction, true, $"Đã tham gia dữ liệu chung: lấy {people} người từ dữ liệu chung.");
        return new { mode = "joined", people };
    }

    private async Task<object?> CloudSetEnabled(JsonElement a)
    {
        var on = a.TryGetProperty("on", out var v) && v.ValueKind == JsonValueKind.True;
        var secret = _keys.GetCloud() ?? throw new HostException("Máy này chưa kết nối dữ liệu chung.");
        if (!on)
        {
            await StopCloudAsync(release: true);
            Db.Outbox.Capture = false;
            Db.Outbox.Clear();
            Db.Settings.Set(CloudEnabledKey, "0");
            await Sync.SetSenderAsync(true);
            Db.Log.Add(null, null, CloudSync.LogAction, true, "Đã tắt dữ liệu chung trên máy này.");
            EmitCloud();
            return CloudStatusView();
        }
        RequireJoinBackup();
        var people = await JoinAsync(secret);
        Db.Log.Add(null, null, CloudSync.LogAction, true, $"Đã bật lại dữ liệu chung: lấy {people} người từ dữ liệu chung.");
        return CloudStatusView();
    }

    private async Task<object?> CloudSyncNow()
    {
        var c = _cloud ?? throw new HostException("Dữ liệu chung đang tắt trên máy này.");
        await c.Sync.PushAsync(_cts.Token);
        await c.Sync.PullAsync(_cts.Token);
        EmitCloud();
        return CloudStatusView();
    }

    private async Task<object?> CloudCheck()
    {
        var secret = _keys.GetCloud() ?? throw new HostException("Máy này chưa kết nối dữ liệu chung.");
        var client = new CloudClient(CloudHttp, CloudCode.Parse(secret.Code));
        var health = await client.HealthAsync(_cts.Token);
        string auth;
        try { await client.GetSpaceAsync(_cts.Token); auth = "ok"; }
        catch (Exception e) when (e is CloudException or CloudOfflineException) { auth = e.Message; }
        var s = _cloud?.Sync.Status;
        return new { health, auth, lastSyncAt = s?.LastSyncAt is { } t ? LocalStamp(t.ToLocalTime()) : null, pending = Db.Outbox.Count() };
    }

    private async Task<object?> CloudChangePassword(JsonElement a)
    {
        var password = OptStr(a, "password") ?? "";
        if (password.Length < MinCloudPassword) throw new HostException("Mật khẩu dữ liệu chung cần ít nhất 8 ký tự.");
        var secret = _keys.GetCloud() ?? throw new HostException("Máy này chưa kết nối dữ liệu chung.");
        var client = new CloudClient(CloudHttp, CloudCode.Parse(secret.Code));
        var space = await client.GetSpaceAsync(_cts.Token) ?? throw new HostException("Dữ liệu chung chưa được tạo.");
        await client.PutSpaceAsync(CloudCrypto.Wrap(secret.Key, password, CloudIterations), space.Ver, _cts.Token);
        Db.Log.Add(null, null, CloudSync.LogAction, true, "Đã đổi mật khẩu dữ liệu chung.");
        return new { ok = true };
    }

    private async Task<object?> CloudForget()
    {
        if (_keys.GetCloud() is not null && Db.Settings.Get(CloudEnabledKey) == "1") await CloudSetEnabled(JsonDocument.Parse("{\"on\":false}").RootElement);
        _keys.ForgetCloud();
        EmitCloud();
        return CloudStatusView();
    }

    // ---------------- runtime ----------------

    private void RequireJoinBackup()
    {
        if (!Backup.HasPassword)
            throw new HostException("Hãy đặt mật khẩu sao lưu trước (mục Sao lưu), để phần mềm sao lưu dữ liệu máy này trước khi thay bằng dữ liệu chung.");
        Backup.Create(JoinBackupReason);
    }

    /// <summary>Replaces the local shared rows with the cloud's (send queue stopped meanwhile) and starts syncing.</summary>
    private async Task<int> JoinAsync(CloudSecret secret)
    {
        await StopCloudAsync(release: true);
        await Sync.StopAsync();
        var client = new CloudClient(CloudHttp, CloudCode.Parse(secret.Code));
        var sync = new CloudSync(Db, client, secret.Key, secret.PcId, Environment.MachineName, _time);
        int people;
        try { people = await sync.ReplaceLocalFromCloudAsync(null, _cts.Token); }
        finally { Sync.Start(); }
        Db.Settings.Set(CloudEnabledKey, "1");
        SetNeedsReconcile(true);
        StartCloud(secret);
        Emit("devices.changed", DeviceViews());
        EmitCloud();
        return people;
    }

    private async Task StartCloudIfEnabledAsync()
    {
        var marker = Path.Combine(_dataFolder, RepublishMarkerName);
        // A restore while shared data was on: even a backup from before joining becomes the shared data.
        if (File.Exists(marker) && _keys.GetCloud() is not null) Db.Settings.Set(CloudEnabledKey, "1");
        if (!CloudEnabled) return;
        var secret = _keys.GetCloud()!;
        if (File.Exists(marker))
        {
            try
            {
                // Before the loop starts: a pull first would bring back what the restore removed.
                Db.Outbox.Capture = true;
                var once = new CloudSync(Db, new CloudClient(CloudHttp, CloudCode.Parse(secret.Code)), secret.Key, secret.PcId, Environment.MachineName, _time);
                await once.RepublishAsync(_cts.Token);
                File.Delete(marker);
                Db.Log.Add(null, null, CloudSync.LogAction, true, "Đã đưa dữ liệu chung về như bản sao lưu vừa khôi phục.");
            }
            catch (Exception e) when (e is CloudException or CloudOfflineException)
            {
                Db.Log.Add(null, null, CloudSync.LogAction, false, "Chưa đưa được bản khôi phục lên dữ liệu chung, sẽ thử lại khi mở phần mềm: " + e.Message);
                return;   // no sync until the restore is in the shared data, or a pull would undo it
            }
        }
        StartCloud(secret);
    }

    private CloudRuntime StartCloud(CloudSecret secret)
    {
        var code = CloudCode.Parse(secret.Code);
        var client = new CloudClient(CloudHttp, code);
        var sync = new CloudSync(Db, client, secret.Key, secret.PcId, Environment.MachineName, _time);
        sync.Applied += OnCloudApplied;
        var lease = new LeaseKeeper(client, secret.Key, secret.PcId, Environment.MachineName, _time);
        Db.Outbox.Capture = true;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        CloudRuntime? rt = null;
        var loop = Task.Run(async () => { while (rt is null) await Task.Yield(); await CloudLoopAsync(rt, stop.Token); });
        rt = new CloudRuntime(code, secret.Key, secret.PcId, client, sync, lease, stop, loop);
        _cloud = rt;
        return rt;
    }

    private async Task StopCloudAsync(bool release)
    {
        var rt = _cloud;
        if (rt is null) return;
        _cloud = null;
        rt.Sync.Applied -= OnCloudApplied;
        await rt.Stop.CancelAsync();
        try { await rt.Loop; } catch { /* the loop only stops */ }
        if (release)
        {
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await rt.Lease.ReleaseAsync(quick.Token);
        }
        rt.Stop.Dispose();
    }

    private async Task CloudLoopAsync(CloudRuntime rt, CancellationToken ct)
    {
        var tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (tick % 30 == 0) await LeaseTick(rt, ct);
                if (Db.Outbox.Count() > 0) { await rt.Sync.PushAsync(ct); EmitCloud(); }
                if (tick % 15 == 0) { await rt.Sync.PullAsync(ct); EmitCloud(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) when (e is CloudException or CloudOfflineException) { EmitCloud(); }
            catch (Exception e) { try { Db.Log.Add(null, null, CloudSync.LogAction, false, e.GetType().Name + ": " + e.Message); } catch { } }
            tick++;
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One lease round now (also used by tests): runs or stops this PC's send queue.</summary>
    public async Task CloudLeaseTickAsync()
    {
        if (_cloud is { } rt) await LeaseTick(rt, _cts.Token);
    }

    private async Task LeaseTick(CloudRuntime rt, CancellationToken ct)
    {
        var sender = await rt.Lease.TickAsync(_windowActive, ct);
        if (sender != Sync.IsSender) { await Sync.SetSenderAsync(sender); EmitCloud(); }
    }

    private void OnCloudApplied(string tbl, string key)
    {
        switch (tbl)
        {
            case "people" or "faces" or "fingerprints" or "device_state" or "trash":
                Emit("person.changed", new { employeeNo = key.Split(Storage.OutboxStore.Sep)[0] });
                break;
            case "devices":
                Sync.Wake();
                Emit("devices.changed", DeviceViews());
                break;
        }
    }

    private void EmitCloud() => Emit("cloud.changed", CloudStatusView());
}
