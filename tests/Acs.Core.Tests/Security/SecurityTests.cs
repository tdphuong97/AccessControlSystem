using System.Security.Cryptography;
using System.Text;
using Acs.Core.Security;
using Xunit;

namespace Acs.Core.Tests.Security;

public class SecurityTests
{
    private sealed class PassThroughProtector : IKeyProtector
    {
        public byte[] Protect(byte[] data) => (byte[])data.Clone();
        public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
    }

    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "acs-sec-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Seal_then_Open_round_trips()
    {
        var p = new DataProtector(RandomNumberGenerator.GetBytes(32));
        var plain = Encoding.UTF8.GetBytes("Nguyễn");
        var sealedData = p.Seal(plain);
        Assert.Equal(12 + 16 + plain.Length, sealedData.Length);
        Assert.Equal(plain, p.Open(sealedData));
    }

    [Fact]
    public void Open_rejects_tampered_data()
    {
        var p = new DataProtector(RandomNumberGenerator.GetBytes(32));
        var sealedData = p.Seal(Encoding.UTF8.GetBytes("Nguyễn"));
        sealedData[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => p.Open(sealedData));
    }

    [Fact]
    public void GetOrCreateDataKey_is_stable_across_instances()
    {
        var folder = TempFolder();
        try
        {
            var a = new KeyStore(folder, new PassThroughProtector()).GetOrCreateDataKey();
            var b = new KeyStore(folder, new PassThroughProtector()).GetOrCreateDataKey();
            Assert.Equal(32, a.Length);
            Assert.Equal(a, b);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ReplaceDataKey_and_backup_key_round_trip()
    {
        var folder = TempFolder();
        try
        {
            var ks = new KeyStore(folder, new PassThroughProtector());
            Assert.Null(ks.GetBackupKey());
            var key = RandomNumberGenerator.GetBytes(32);
            ks.ReplaceDataKey(key);
            Assert.Equal(key, new KeyStore(folder, new PassThroughProtector()).GetOrCreateDataKey());
            var bk = RandomNumberGenerator.GetBytes(32);
            var salt = RandomNumberGenerator.GetBytes(16);
            ks.SetBackupKey(bk, salt);
            var got = new KeyStore(folder, new PassThroughProtector()).GetBackupKey();
            Assert.NotNull(got);
            Assert.Equal(bk, got!.Value.Key);
            Assert.Equal(salt, got.Value.Salt);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Dpapi_round_trips_on_windows()
    {
        var p = new DpapiKeyProtector();
        var data = RandomNumberGenerator.GetBytes(32);
        var prot = p.Protect(data);
        Assert.NotEqual(data, prot);
        Assert.Equal(data, p.Unprotect(prot));
    }
}
