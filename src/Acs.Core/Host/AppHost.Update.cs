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

    private void StartUpdateTimer() =>
        _updateTimer = _time.CreateTimer(_ => _ = AutoCheckAsync(), null, FirstUpdateCheck, UpdateCheckEvery);

    private async Task AutoCheckAsync()
    {
        try
        {
            if (_closed || !AutoCheckOn) return;
            var info = await _updater.CheckAsync(_version, UpdatePlatform, _cts.Token);
            if (info is null) return;
            var isNew = _foundUpdate?.Version != info.Version;
            _foundUpdate = info;
            if (isNew) Emit("update.available", ToView(info));
        }
        catch { /* a failed background check is silent; the user can check by hand */ }
    }

    private async Task<UpdateView?> UpdateCheck()
    {
        var info = await _updater.CheckAsync(_version, UpdatePlatform, _cts.Token);
        _foundUpdate = info;
        return info is null ? null : ToView(info);
    }

    private async Task<object?> UpdateInstall()
    {
        var run = RunInstaller ?? throw new HostException("Chưa sẵn sàng cập nhật");
        var info = _foundUpdate ?? await _updater.CheckAsync(_version, UpdatePlatform, _cts.Token)
            ?? throw new HostException("Đang dùng bản mới nhất rồi.");
        _foundUpdate = info;
        var path = await _updater.DownloadAsync(info, _updatesFolder, f => Emit("update.progress", new { fraction = f }), _cts.Token);
        if (Backup.HasPassword) Backup.Create("truoc-cap-nhat");
        else Db.Log.Add(null, null, "Cập nhật", true, "Chưa đặt mật khẩu sao lưu nên không sao lưu trước khi cập nhật.");
        await run(path);
        return null;
    }

    private SettingsView SettingsGet() =>
        new(AutoCheckOn, Db.Settings.Get(StartWithWindowsKey) == "true");

    private SettingsView SettingsSet(JsonElement a)
    {
        if (Has(a, "autoCheckUpdates")) Db.Settings.Set(AutoCheckKey, Bool(a, "autoCheckUpdates", true) ? "true" : "false");
        if (Has(a, "startWithWindows"))
        {
            var on = Bool(a, "startWithWindows");
            Db.Settings.Set(StartWithWindowsKey, on ? "true" : "false");
            ApplyStartWithWindows?.Invoke(on);
        }
        return SettingsGet();
    }
}
