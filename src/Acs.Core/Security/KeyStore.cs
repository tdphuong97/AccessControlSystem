using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Acs.Core.Security;

/// <summary>Keeps the data key (data.key) and backup key (backup.key), both wrapped by an IKeyProtector.</summary>
public sealed class KeyStore
{
    private readonly string _folder;
    private readonly IKeyProtector _protector;

    public KeyStore(string folder, IKeyProtector protector)
    {
        _folder = folder;
        _protector = protector;
    }

    private string DataKeyPath => Path.Combine(_folder, "data.key");
    private string BackupKeyPath => Path.Combine(_folder, "backup.key");

    public byte[] GetOrCreateDataKey()
    {
        if (File.Exists(DataKeyPath))
            return _protector.Unprotect(File.ReadAllBytes(DataKeyPath));
        var key = RandomNumberGenerator.GetBytes(32);
        WriteAtomic(DataKeyPath, _protector.Protect(key));
        return key;
    }

    public void ReplaceDataKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        WriteAtomic(DataKeyPath, _protector.Protect(key));
    }

    public (byte[] Key, byte[] Salt)? GetBackupKey()
    {
        if (!File.Exists(BackupKeyPath)) return null;
        var blob = _protector.Unprotect(File.ReadAllBytes(BackupKeyPath)); // keyLen(4 LE) | key | salt
        var keyLen = BinaryPrimitives.ReadInt32LittleEndian(blob);
        return (blob.AsSpan(4, keyLen).ToArray(), blob.AsSpan(4 + keyLen).ToArray());
    }

    public void SetBackupKey(byte[] key, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(salt);
        var blob = new byte[4 + key.Length + salt.Length];
        BinaryPrimitives.WriteInt32LittleEndian(blob, key.Length);
        key.CopyTo(blob, 4);
        salt.CopyTo(blob, 4 + key.Length);
        WriteAtomic(BackupKeyPath, _protector.Protect(blob));
    }

    private string CloudKeyPath => Path.Combine(_folder, "cloud.key");

    /// <summary>Dữ liệu chung: connection code, shared key K and this PC's random id, DPAPI-wrapped like the other keys.</summary>
    public CloudSecret? GetCloud()
    {
        if (!File.Exists(CloudKeyPath)) return null;
        var json = System.Text.Json.JsonDocument.Parse(_protector.Unprotect(File.ReadAllBytes(CloudKeyPath))).RootElement;
        return new CloudSecret(json.GetProperty("code").GetString()!, Convert.FromBase64String(json.GetProperty("key").GetString()!),
            json.GetProperty("pcId").GetString()!);
    }

    public void SetCloud(CloudSecret s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { code = s.Code, key = Convert.ToBase64String(s.Key), pcId = s.PcId });
        WriteAtomic(CloudKeyPath, _protector.Protect(json));
    }

    public void ForgetCloud() { if (File.Exists(CloudKeyPath)) File.Delete(CloudKeyPath); }

    private void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(_folder);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed record CloudSecret(string Code, byte[] Key, string PcId);
