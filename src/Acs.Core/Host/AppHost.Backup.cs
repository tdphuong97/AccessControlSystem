using System.Globalization;
using System.Text.Json;
using Acs.Core.Backup;
using Acs.Core.Security;
using Acs.Core.Storage;

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
            if (!Path.IsPathFullyQualified(folder))
                throw new HostException("Hãy nhập đường dẫn đầy đủ, ví dụ D:\\SaoLuu hoặc \\\\may-chu\\SaoLuu.");
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
    /// Checks name + password against a scratch copy (own key store and database), so a wrong password can be
    /// refused before anything live is touched.
    /// </summary>
    private void VerifyBackup(string path, string password)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "acs-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var scratch = new Database(Path.Combine(tmp, "acs.db"), new DataProtector(new byte[32]));
            var keys = new KeyStore(Path.Combine(tmp, "keys"), _protector);
            var svc = _backupIterations is { } n ? new BackupService(scratch, keys, tmp, _time, n) : new BackupService(scratch, keys, tmp, _time);
            svc.Restore(path, password, Path.Combine(tmp, "verify.db"));
        }
        finally { try { Directory.Delete(tmp, true); } catch { /* temp clean-up only */ } }
    }

    /// <summary>
    /// Replaces the database from a backup. The password is verified first, so a wrong one changes nothing. Then new
    /// commands are refused ("Đang khôi phục"), running ones drain, the engine stops and the database is replaced.
    /// On success the host is closed and RestartRequested is set (plus the app.restart event): the App must relaunch
    /// itself. If the swap itself fails, a fresh graph on the old database is published and the engine restarted.
    /// </summary>
    private async Task<object?> BackupRestore(JsonElement a)
    {
        var name = Str(a, "name");
        var password = OptStr(a, "password") ?? "";
        var file = Backup.List().FirstOrDefault(b => b.Name == name)
            ?? throw new HostException("Không tìm thấy file sao lưu. Hãy chọn lại.");
        VerifyBackup(file.Path, password);

        lock (_gate)
        {
            if (_restoring) throw new HostException(RestoringMessage);
            _restoring = true;
        }
        var done = false;
        try
        {
            while (true)
            {
                lock (_gate) if (_inflight <= 1) break;   // this call is the one left
                await Task.Delay(10);
            }
            var old = _g;
            var wasStarted = _started;
            await old.Sync.StopAsync();
            old.Db.Dispose();
            try { old.Backup.Restore(file.Path, password, _dbFile); }
            catch
            {
                var fresh = Build(OpenDb());
                _g = fresh;
                if (wasStarted) fresh.Sync.Start();
                throw;
            }
            done = true;
            lock (_gate) { _closed = true; _updateTimer?.Dispose(); }
            RestartRequested = true;
            Emit("app.restart", null);
            return null;
        }
        finally { if (!done) lock (_gate) _restoring = false; }
    }
}
