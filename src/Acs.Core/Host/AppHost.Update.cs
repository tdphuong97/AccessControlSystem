using System.Text.Json;
using Acs.Core.Update;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    public const string AutoCheckKey = "autoCheckUpdates";
    public const string StartWithWindowsKey = "startWithWindows";
    public static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan UpdateCheckEvery = TimeSpan.FromHours(20);

    private ITimer? _updateTimer;
    private UpdateInfo? _foundUpdate;

    private static UpdateView ToView(UpdateInfo i) =>
        new(i.Version, i.Notes ?? "", Math.Round((i.Package?.Size ?? 0) / 1048576.0, 1));

    private bool AutoCheckOn => Db.Settings.Get(AutoCheckKey) != "false";

    private void StartUpdateTimer()
    {
        lock (_gate)
        {
            if (_stopped || _closed) return;   // StopAsync already ran: no timer to leak
            _updateTimer = _time.CreateTimer(_ => _ = AutoCheckAsync(), null, FirstUpdateCheck, UpdateCheckEvery);
        }
    }

    /// <summary>Feed check with its own short limit; a timeout surfaces as OperationCanceledException (mapped by HandleAsync).</summary>
    private async Task<UpdateInfo?> CheckFeed()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        cts.CancelAfter(FeedTimeout);
        return await _updater.CheckAsync(_version, UpdatePlatform, cts.Token);
    }

    private async Task AutoCheckAsync()
    {
        try
        {
            lock (_gate) if (_closed || _restoring) return;
            if (!AutoCheckOn) return;
            var info = await CheckFeed();
            if (info is null) return;
            var isNew = _foundUpdate?.Version != info.Version;
            _foundUpdate = info;
            if (isNew) Emit("update.available", ToView(info));
        }
        catch { /* a failed background check is silent; the user can check by hand */ }
    }

    private async Task<UpdateView?> UpdateCheck()
    {
        var info = await CheckFeed();
        _foundUpdate = info;
        return info is null ? null : ToView(info);
    }

    private async Task<object?> UpdateInstall()
    {
        var run = RunInstaller ?? throw new HostException("Chưa sẵn sàng cập nhật");
        if (Interlocked.Exchange(ref _installing, 1) == 1) throw new HostException("Đang cập nhật…");
        try
        {
            var info = _foundUpdate ?? await CheckFeed() ?? throw new HostException("Đang dùng bản mới nhất rồi.");
            _foundUpdate = info;
            var path = await _updater.DownloadAsync(info, _updatesFolder, f => Emit("update.progress", new { fraction = f }), _cts.Token);
            if (Backup.HasPassword) Backup.Create("truoc-cap-nhat");
            else Db.Log.Add(null, null, "Cập nhật", true, "Chưa đặt mật khẩu sao lưu nên không sao lưu trước khi cập nhật.");
            await run(path);
            return null;
        }
        finally { Interlocked.Exchange(ref _installing, 0); }
    }

    private SettingsView SettingsGet() =>
        new(AutoCheckOn, Db.Settings.Get(StartWithWindowsKey) == "true");

    private SettingsView SettingsSet(JsonElement a)
    {
        if (Has(a, "autoCheckUpdates")) Db.Settings.Set(AutoCheckKey, Bool(a, "autoCheckUpdates", true) ? "true" : "false");
        if (Has(a, "startWithWindows"))
        {
            var on = Bool(a, "startWithWindows");
            ApplyStartWithWindows?.Invoke(on);   // apply first: if it fails, the stored setting stays truthful
            Db.Settings.Set(StartWithWindowsKey, on ? "true" : "false");
        }
        return SettingsGet();
    }
}
