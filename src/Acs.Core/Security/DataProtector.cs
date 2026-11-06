using System.Security.Cryptography;

namespace Acs.Core.Security;

/// <summary>AES-GCM sealing. Layout: nonce(12) | tag(16) | cipher.</summary>
public sealed class DataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public DataProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length is not (16 or 24 or 32))
            throw new ArgumentException("Khóa phải dài 16, 24 hoặc 32 byte.", nameof(key));
        _key = (byte[])key.Clone();
    }

    public byte[] Seal(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var result = new byte[NonceSize + TagSize + plain.Length];
        var nonce = result.AsSpan(0, NonceSize);
        var tag = result.AsSpan(NonceSize, TagSize);
        var cipher = result.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(nonce, plain, cipher, tag);
        return result;
    }

    public byte[] Open(byte[] sealedData)
    {
        ArgumentNullException.ThrowIfNull(sealedData);
        if (sealedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Dữ liệu mã hóa quá ngắn hoặc đã bị hỏng.");
        var nonce = sealedData.AsSpan(0, NonceSize);
        var tag = sealedData.AsSpan(NonceSize, TagSize);
        var cipher = sealedData.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(nonce, cipher, tag, plain); // throws AuthenticationTagMismatchException (a CryptographicException)
        return plain;
    }
}
