using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Acs.Core.Security;
using Acs.Core.Storage;

namespace Acs.Core.Backup;

public sealed record BackupFile(string Path, string Name, DateTime At, long Size);

public sealed class BackupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Encrypted backups. File layout: "ACSB" | 1 | salt(16) | nonce(12) | tag(16) | AES-GCM cipher of
/// (data key (32) | SQLite bytes). The key is PBKDF2-SHA256(password, salt, 600 000, 32); it is derived once in
/// <see cref="SetPassword"/> and kept (DPAPI-wrapped) in the KeyStore so daily backups need no password.
/// The snapshot is taken with VACUUM INTO through the Database (it does not expose its connection).
/// </summary>
public sealed class BackupService
{
    public const int Keep = 14;
    public const string ExtraFolderSetting = "backup.extraFolder";
    internal const int ProductionIterations = 600_000;

    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, DataKeySize = 32, HeaderSize = 4 + 1 + SaltSize + NonceSize + TagSize;
    private static readonly byte[] Magic = "ACSB"u8.ToArray();

    private readonly Database _db;
    private readonly KeyStore _keys;
    private readonly string _folder;
    private readonly TimeProvider _time;
    private readonly int _iterations;

    /// <param name="iterations">PBKDF2 iterations; leave at the default in production (tests lower it).</param>
    public BackupService(Database db, KeyStore keys, string backupFolder, TimeProvider time, int iterations = ProductionIterations)
    {
        _db = db; _keys = keys; _folder = backupFolder; _time = time; _iterations = iterations;
    }

    public bool HasPassword => _keys.GetBackupKey() is not null;

    public void SetPassword(string password)
    {
        if (password is null || password.Length < 8)
            throw new BackupException("Mật khẩu sao lưu phải có ít nhất 8 ký tự.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        _keys.SetBackupKey(Derive(password, salt), salt);
    }

    public string Create(string reason)
    {
        var (key, salt) = _keys.GetBackupKey() ?? throw new BackupException("Chưa đặt mật khẩu sao lưu.");
        Directory.CreateDirectory(_folder);

        var now = _time.GetLocalNow();
        var name = $"ACS-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Sanitise(reason)}.acsbak";
        var path = Path.Combine(_folder, name);

        var snapDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_folder)) ?? _folder, "tmp");
        Directory.CreateDirectory(snapDir);
        var snap = Path.Combine(snapDir, "acs-snap-" + Guid.NewGuid().ToString("N") + ".db");
        byte[] plain;
        try
        {
            _db.Exec("VACUUM INTO $f", ("$f", snap));
            var sqlite = File.ReadAllBytes(snap);
            var dataKey = _keys.GetOrCreateDataKey();
            plain = new byte[DataKeySize + sqlite.Length];
            dataKey.CopyTo(plain, 0);
            sqlite.CopyTo(plain, DataKeySize);
        }
        finally { try { File.Delete(snap); } catch { } }

        var file = new byte[HeaderSize + plain.Length];
        Magic.CopyTo(file, 0);
        file[4] = 1;
        salt.CopyTo(file, 5);
        var nonce = file.AsSpan(5 + SaltSize, NonceSize);
        var tag = file.AsSpan(5 + SaltSize + NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, plain, file.AsSpan(HeaderSize), tag);
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); }

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, file);
        File.Move(tmp, path, overwrite: true);

        Prune();
        CopyToExtra(path);
        return path;
    }

    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(_folder)) return [];
        return Directory.EnumerateFiles(_folder, "ACS-*.acsbak")
            .Select(p => new FileInfo(p))
            .Select(f => new BackupFile(f.FullName, f.Name, ParseAt(f), f.Length))
            .OrderByDescending(b => b.At).ThenByDescending(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Checks the password against the file by decrypting it in memory only; nothing is written. False for a wrong
    /// password, a tampered or malformed file; BackupException only when the file is missing or unreadable.
    /// </summary>
    public bool CheckPassword(string path, string password)
    {
        var opened = Open(path, password);
        if (opened is null) return false;
        CryptographicOperations.ZeroMemory(opened.Value.Plain);
        CryptographicOperations.ZeroMemory(opened.Value.Key);
        return true;
    }

    private (byte[] Plain, byte[] Key, byte[] Salt)? Open(string path, string password)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new BackupException("Không đọc được file sao lưu.", ex); }
        if (file.Length < HeaderSize + DataKeySize || !file.AsSpan(0, 4).SequenceEqual(Magic) || file[4] != 1)
            return null;
        var salt = file.AsSpan(5, SaltSize).ToArray();
        var key = Derive(password ?? "", salt);
        var plain = new byte[file.Length - HeaderSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(file.AsSpan(5 + SaltSize, NonceSize), file.AsSpan(HeaderSize), file.AsSpan(5 + SaltSize + NonceSize, TagSize), plain);
            return (plain, key, salt);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
            return null;
        }
    }

    /// <summary>
    /// Decrypts <paramref name="path"/>, replaces the database file and the data key. The caller must have
    /// stopped the engine and disposed the live Database first (AppHost restarts the app afterwards).
    /// <paramref name="dbFile"/> defaults to <see cref="AppPaths.DbFile"/>. The old database (with -wal/-shm) and old
    /// keys are kept aside until the new pair is fully in place; any failure rolls both back, so the old data stays
    /// openable. The backup password becomes the backup key on this PC.
    /// </summary>
    public void Restore(string path, string password, string? dbFile = null)
    {
        var opened = Open(path, password) ?? throw new BackupException("Mật khẩu sao lưu không đúng hoặc file đã hỏng.");
        var (plain, key, salt) = opened;
        try
        {
            var target = dbFile ?? AppPaths.DbFile;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            var tmp = target + ".restore.tmp";
            string[] set = [target, target + "-wal", target + "-shm"];
            var moved = new List<string>();
            byte[]? oldKey = null;
            (byte[] Key, byte[] Salt)? oldBackup = null;
            var keyReplaced = false;
            try
            {
                try { oldKey = _keys.GetOrCreateDataKey(); } catch { }
                try { oldBackup = _keys.GetBackupKey(); } catch { }
                File.WriteAllBytes(tmp, plain.AsSpan(DataKeySize).ToArray());
                foreach (var f in set)
                    if (File.Exists(f)) { File.Move(f, f + ".bak-restore", overwrite: true); moved.Add(f); }
                _keys.ReplaceDataKey(plain.AsSpan(0, DataKeySize).ToArray());
                keyReplaced = true;
                _keys.SetBackupKey(key, salt);
                File.Move(tmp, target, overwrite: true);
            }
            catch (Exception ex)
            {
                try { File.Delete(tmp); } catch { }
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                foreach (var f in moved)
                    try { File.Move(f + ".bak-restore", f, overwrite: true); } catch { }
                if (keyReplaced && oldKey is not null)
                {
                    try { _keys.ReplaceDataKey(oldKey); } catch { }
                    if (oldBackup is { } ob) try { _keys.SetBackupKey(ob.Key, ob.Salt); } catch { }
                }
                throw new BackupException("Không khôi phục được bản sao lưu. Dữ liệu cũ vẫn được giữ nguyên.", ex);
            }
            foreach (var f in moved)
                try { File.Delete(f + ".bak-restore"); } catch { }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, _iterations, HashAlgorithmName.SHA256, DataKeySize);

    private static string Sanitise(string reason)
    {
        var sb = new StringBuilder();
        foreach (var c in (reason ?? "").ToLowerInvariant())
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' ? c : '-');
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "backup" : s;
    }

    private static DateTime ParseAt(FileInfo f)
    {
        // ACS-yyyyMMdd-HHmmss-reason.acsbak
        var n = f.Name;
        if (n.Length >= 19 && DateTime.TryParseExact(n.AsSpan(4, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return at;
        return f.LastWriteTime;
    }

    private void Prune()
    {
        foreach (var old in List().Skip(Keep))
            try { File.Delete(old.Path); } catch { }
    }

    private void CopyToExtra(string path)
    {
        try
        {
            var extra = _db.Settings.Get(ExtraFolderSetting);
            if (string.IsNullOrWhiteSpace(extra)) return;
            Directory.CreateDirectory(extra);
            File.Copy(path, Path.Combine(extra, Path.GetFileName(path)), overwrite: true);
        }
        catch { /* the extra copy is best-effort; the main backup already succeeded */ }
    }
}
