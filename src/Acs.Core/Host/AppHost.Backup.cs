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

    private const string FolderExample = "ví dụ E:\\SaoLuuACS hoặc \\\\may-chu\\SaoLuu";

    /// <summary>
    /// backup.listFolder {folder}: the ACS-*.acsbak files in another folder (USB, network share, a copied folder on a
    /// new PC), same shape as backup.status.files. The page restores one with backup.restore {path, password}.
    /// </summary>
    private IReadOnlyList<BackupFileView> BackupListFolder(JsonElement a)
    {
        var folder = (OptStr(a, "folder") ?? "").Trim();
        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
            throw new HostException($"Hãy nhập đường dẫn đầy đủ của thư mục có file .acsbak ({FolderExample}).");
        try
        {
            if (!Directory.Exists(folder))
                throw new HostException("Không thấy thư mục này. Kiểm tra lại đường dẫn, hoặc USB / ổ mạng đã cắm chưa, rồi bấm Xem.");
            return Backup.List(folder).Select(f => new BackupFileView(f.Name, LocalStamp(f.At), f.Size)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            throw new HostException("Không đọc được thư mục này. Hãy chép file .acsbak vào một thư mục trên máy tính rồi chọn lại.");
        }
    }

    /// <summary>The file named by backup.restore: {path} (any folder, must be a full path ending .acsbak) or {name}
    /// (one of backup.status.files).</summary>
    private string RestoreSource(JsonElement a)
    {
        var path = OptStr(a, "path")?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            var name = Str(a, "name");
            return (Backup.List().FirstOrDefault(b => b.Name == name)
                ?? throw new HostException("Không tìm thấy file sao lưu. Hãy chọn lại.")).Path;
        }
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".acsbak", StringComparison.OrdinalIgnoreCase))
            throw new HostException($"Hãy chọn một file sao lưu .acsbak bằng đường dẫn đầy đủ ({FolderExample}).");
        try { if (File.Exists(path)) return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        throw new HostException("Không tìm thấy file sao lưu ở đường dẫn này. Hãy bấm Xem rồi chọn lại.");
    }

    /// <summary>On start: back up when a password is set and the newest backup is older than 24 hours (or there is none).</summary>
    private void DailyBackup()
    {
        try
        {
            lock (_gate) if (_restoring || _closed) return;
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
        var source = RestoreSource(a);
        var password = OptStr(a, "password") ?? "";
        VerifyBackup(source, password);

        lock (_gate)
        {
            if (_restoring) throw new HostException(RestoringMessage);
            _restoring = true;
        }
        var done = false;
        try
        {
            // Drain first: nothing is stopped or disposed until no other command is running (bounded).
            var deadline = _time.GetUtcNow() + DrainLimit;
            while (true)
            {
                lock (_gate) if (_inflight <= 1) break;   // this call is the one left
                if (_time.GetUtcNow() >= deadline) throw new HostException(BusyMessage);
                await Task.Delay(10, _cts.Token);   // real-time poll; only the 60 s limit follows TimeProvider
            }
            var old = _g;
            var wasStarted = _started;
            await old.Sync.StopAsync();
            old.Db.Dispose();
            try { old.Backup.Restore(source, password, _dbFile); }
            catch
            {
                Graph fresh;
                try { fresh = Build(ReopenDb is { } open ? open() : OpenDb()); }
                catch
                {
                    lock (_gate) _broken = true;   // _g still points at the disposed graph: refuse everything
                    done = true;                   // keep _restoring set
                    throw new HostException(BrokenMessage);
                }
                _g = fresh;
                if (wasStarted) fresh.Sync.Start();
                throw;
            }
            done = true;
            try { File.WriteAllText(_restoreMarker, "1"); } catch { /* the hint is best-effort; the restore itself succeeded */ }
            lock (_gate) { _closed = true; _updateTimer?.Dispose(); }
            RestartRequested = true;
            Emit("app.restart", null);
            return null;
        }
        finally { if (!done) lock (_gate) _restoring = false; }
    }
}
