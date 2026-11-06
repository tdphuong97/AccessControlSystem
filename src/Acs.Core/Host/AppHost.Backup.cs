using System.Globalization;
using System.Text.Json;
using Acs.Core.Backup;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    private static readonly TimeSpan BackupEvery = TimeSpan.FromHours(24);

    private static string LocalStamp(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private BackupStatus BackupStatusView()
    {
        var files = Backup.List();
        var extra = Db.Settings.Get(BackupService.ExtraFolderSetting);
        return new BackupStatus(Backup.HasPassword, files.Count > 0 ? LocalStamp(files[0].At) : null,
            files.Select(f => new BackupFileView(f.Name, LocalStamp(f.At), f.Size)).ToList(),
            string.IsNullOrWhiteSpace(extra) ? null : extra);
    }

    private BackupStatus BackupSetPassword(JsonElement a)
    {
        Backup.SetPassword(Str(a, "password"));
        return BackupStatusView();
    }

    private BackupStatus BackupNow()
    {
        Backup.Create("thu-cong");
        return BackupStatusView();
    }

    private BackupStatus BackupSetExtraFolder(JsonElement a)
    {
        var folder = OptStr(a, "folder")?.Trim();
        if (string.IsNullOrEmpty(folder)) Db.Settings.Set(BackupService.ExtraFolderSetting, "");
        else
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { throw new HostException("Không dùng được thư mục này. Hãy chọn thư mục khác (ví dụ ổ đĩa mạng hoặc USB)."); }
            Db.Settings.Set(BackupService.ExtraFolderSetting, folder);
        }
        return BackupStatusView();
    }

    /// <summary>On start: back up when a password is set and the newest backup is older than 24 hours (or there is none).</summary>
    private void DailyBackup()
    {
        try
        {
            if (!Backup.HasPassword) return;
            var last = Backup.List().FirstOrDefault();
            if (last is null || _time.GetLocalNow().DateTime - last.At > BackupEvery) Backup.Create("hang-ngay");
        }
        catch (Exception ex)
        {
            try { Db.Log.Add(null, null, "Sao lưu", false, ex.Message); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Replaces the database from a backup. On success the host is closed and RestartRequested is set (plus the
    /// app.restart event): the App must relaunch itself. A wrong password leaves everything running as before.
    /// </summary>
    private async Task<object?> BackupRestore(JsonElement a)
    {
        var name = Str(a, "name");
        var password = OptStr(a, "password") ?? "";
        var file = Backup.List().FirstOrDefault(b => b.Name == name)
            ?? throw new HostException("Không tìm thấy file sao lưu. Hãy chọn lại.");
        var wasStarted = _started;
        await Sync.StopAsync();
        Db.Dispose();
        try { Backup.Restore(file.Path, password, _dbFile); }
        catch
        {
            Build(OpenDb());
            if (wasStarted) Sync.Start();
            throw;
        }
        _closed = true;
        RestartRequested = true;
        _updateTimer?.Dispose();
        Emit("app.restart", null);
        return null;
    }
}
