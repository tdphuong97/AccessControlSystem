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

        var snap = Path.Combine(Path.GetTempPath(), "acs-snap-" + Guid.NewGuid().ToString("N") + ".db");
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
        using (var gcm = new AesGcm(key, TagSize))
            gcm.Encrypt(nonce, plain, file.AsSpan(HeaderSize), tag);
        CryptographicOperations.ZeroMemory(plain);

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
    /// Decrypts <paramref name="path"/>, replaces the database file and the data key. The caller must have
    /// stopped the engine and disposed the live Database first (AppHost restarts the app afterwards).
    /// <paramref name="dbFile"/> defaults to <see cref="AppPaths.DbFile"/>. The database is written to a temp file and
    /// moved into place, so a failed restore never leaves a half-written database. The backup password becomes
    /// the backup key on this PC, so backups continue without asking again.
    /// </summary>
    public void Restore(string path, string password, string? dbFile = null)
    {
        const string bad = "Mật khẩu sao lưu không đúng hoặc file đã hỏng.";
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new BackupException("Không đọc được file sao lưu.", ex); }
        if (file.Length < HeaderSize + DataKeySize || !file.AsSpan(0, 4).SequenceEqual(Magic) || file[4] != 1)
            throw new BackupException(bad);

        var salt = file.AsSpan(5, SaltSize).ToArray();
        var key = Derive(password ?? "", salt);
        var plain = new byte[file.Length - HeaderSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(file.AsSpan(5 + SaltSize, NonceSize), file.AsSpan(HeaderSize), file.AsSpan(5 + SaltSize + NonceSize, TagSize), plain);
        }
        catch (CryptographicException ex) { throw new BackupException(bad, ex); }

        var target = dbFile ?? AppPaths.DbFile;
        var dir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(dir);
        var tmp = target + ".restore.tmp";
        try
        {
            File.WriteAllBytes(tmp, plain.AsSpan(DataKeySize).ToArray());
            foreach (var side in new[] { target + "-wal", target + "-shm" }) if (File.Exists(side)) File.Delete(side);
            File.Move(tmp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch { }
            throw new BackupException("Không ghi được dữ liệu khôi phục. Hãy đóng phần mềm khác đang dùng dữ liệu rồi thử lại.", ex);
        }
        _keys.ReplaceDataKey(plain.AsSpan(0, DataKeySize).ToArray());
        _keys.SetBackupKey(key, salt);
        CryptographicOperations.ZeroMemory(plain);
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
