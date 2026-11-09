using System.Security.Cryptography;
using System.Text;

namespace Acs.Core.Cloud;

/// <summary>A refusal the person can act on (wrong code, wrong password, changed key). The message is shown as is.</summary>
public sealed class CloudException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The Worker could not be reached (no Internet, timeout, 5xx). The app keeps working on its local copy.</summary>
public sealed class CloudOfflineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The connection code a person types once: <c>acs1:&lt;worker host&gt;:&lt;CONNECT_KEY&gt;</c>. It names the Worker and
/// carries the bearer key; the shared password is separate and never leaves the PC.
/// </summary>
public sealed record CloudCode(Uri BaseUrl, string Key)
{
    private const string Prefix = "acs1";
    private const string Invalid = "Mã kết nối không đúng. Hãy chép lại nguyên dòng mã.";

    public static CloudCode Parse(string text)
    {
        var parts = (text ?? "").Trim().Split(':');
        if (parts.Length != 3 || parts[0] != Prefix || parts[1].Length == 0 || parts[2].Length == 0
            || Uri.CheckHostName(parts[1]) != UriHostNameType.Dns || !parts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new CloudException(Invalid);
        return new CloudCode(new Uri("https://" + parts[1]), parts[2]);
    }

    public override string ToString() => $"{Prefix}:{BaseUrl.Host}:{Key}";
}

/// <summary>The shared key K wrapped with the shared password, as stored in the Worker's space record.</summary>
public sealed record WrappedKey(string Wrapped, string Salt, int Iterations);

/// <summary>
/// Shared key K (32 bytes), wrapped with PBKDF2-SHA256(password, salt 16, iterations) + AES-GCM. Each row is sealed with
/// AES-GCM(K) and bound to its table and row id (AAD), and the row id is an HMAC of the natural key, so the Worker sees
/// neither employee numbers nor anything else in plaintext.
/// </summary>
public static class CloudCrypto
{
    public const int DefaultIterations = 600_000;
    private const int NonceLen = 12, TagLen = 16;

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    public static WrappedKey Wrap(byte[] k, string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var kek = Derive(password, salt, iterations);
        return new WrappedKey(Convert.ToBase64String(Encrypt(kek, k, "acs-space"u8)), Convert.ToBase64String(salt), iterations);
    }

    public static byte[] Unwrap(WrappedKey w, string password)
    {
        try { return Decrypt(Derive(password, Convert.FromBase64String(w.Salt), w.Iterations), Convert.FromBase64String(w.Wrapped), "acs-space"u8); }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        { throw new CloudException("Mật khẩu dữ liệu chung chưa đúng.", e); }
    }

    public static string RowId(byte[] k, string tbl, string naturalKey)
    {
        var mac = HMACSHA256.HashData(k, Encoding.UTF8.GetBytes(tbl + "\n" + naturalKey));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Seal(byte[] k, string tbl, string rid, byte[] plain) =>
        Convert.ToBase64String(Encrypt(k, plain, Aad(tbl, rid)));

    public static byte[] Open(byte[] k, string tbl, string rid, string sealedData)
    {
        try { return Decrypt(k, Convert.FromBase64String(sealedData), Aad(tbl, rid)); }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException)
        { throw new CloudException("Dữ liệu chung bị hỏng hoặc không đúng khoá.", e); }
    }

    private static byte[] Aad(string tbl, string rid) => Encoding.UTF8.GetBytes(tbl + "\n" + rid);

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? ""), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Encrypt(byte[] key, ReadOnlySpan<byte> plain, ReadOnlySpan<byte> aad)
    {
        var output = new byte[NonceLen + TagLen + plain.Length];
        var nonce = output.AsSpan(0, NonceLen);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(key, TagLen);
        gcm.Encrypt(nonce, plain, output.AsSpan(NonceLen + TagLen), output.AsSpan(NonceLen, TagLen), aad);
        return output;
    }

    private static byte[] Decrypt(byte[] key, byte[] blob, ReadOnlySpan<byte> aad)
    {
        if (blob.Length < NonceLen + TagLen) throw new CryptographicException("short");
        var plain = new byte[blob.Length - NonceLen - TagLen];
        using var gcm = new AesGcm(key, TagLen);
        gcm.Decrypt(blob.AsSpan(0, NonceLen), blob.AsSpan(NonceLen + TagLen), blob.AsSpan(NonceLen, TagLen), plain, aad);
        return plain;
    }
}
