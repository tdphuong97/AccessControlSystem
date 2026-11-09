using System.Text;
using Acs.Core.Cloud;

namespace Acs.Core.Tests.Cloud;

public sealed class CloudCryptoTests
{
    private const int FastIterations = 1000;

    [Fact]
    public void Parse_round_trips()
    {
        var code = CloudCode.Parse("  acs1:acs-sync.vienba.workers.dev:abcDEF_123-xyz ");
        Assert.Equal(new Uri("https://acs-sync.vienba.workers.dev"), code.BaseUrl);
        Assert.Equal("abcDEF_123-xyz", code.Key);
        Assert.Equal("acs1:acs-sync.vienba.workers.dev:abcDEF_123-xyz", code.ToString());
        Assert.Equal(code, CloudCode.Parse(code.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("acs2:host:key")]
    [InlineData("acs1:host")]
    [InlineData("acs1::key")]
    [InlineData("acs1:host:")]
    [InlineData("acs1:bad host:key")]
    public void Parse_rejects_garbage(string text)
    {
        var e = Assert.Throws<CloudException>(() => CloudCode.Parse(text));
        Assert.Equal("Mã kết nối không đúng. Hãy chép lại nguyên dòng mã.", e.Message);
    }

    [Fact]
    public void Wrap_then_Unwrap_returns_K()
    {
        var k = CloudCrypto.NewKey();
        Assert.Equal(32, k.Length);
        var w = CloudCrypto.Wrap(k, "mat-khau-chung", FastIterations);
        Assert.Equal(FastIterations, w.Iterations);
        Assert.Equal(k, CloudCrypto.Unwrap(w, "mat-khau-chung"));
    }

    [Fact]
    public void Wrap_defaults_to_600000_iterations()
    {
        Assert.Equal(600_000, CloudCrypto.Wrap(CloudCrypto.NewKey(), "12345678").Iterations);
    }

    [Fact]
    public void Unwrap_with_wrong_password_throws_vietnamese_message()
    {
        var w = CloudCrypto.Wrap(CloudCrypto.NewKey(), "dung-roi", FastIterations);
        var e = Assert.Throws<CloudException>(() => CloudCrypto.Unwrap(w, "sai-roi"));
        Assert.Equal("Mật khẩu dữ liệu chung chưa đúng.", e.Message);
    }

    [Fact]
    public void RowId_is_stable_and_hides_the_key()
    {
        var k = CloudCrypto.NewKey();
        var a = CloudCrypto.RowId(k, "people", "00000118");
        Assert.Equal(a, CloudCrypto.RowId(k, "people", "00000118"));
        Assert.DoesNotContain("118", a);
        Assert.NotEqual(a, CloudCrypto.RowId(k, "faces", "00000118"));
        Assert.NotEqual(a, CloudCrypto.RowId(CloudCrypto.NewKey(), "people", "00000118"));
        Assert.Matches("^[A-Za-z0-9_-]{22}$", a);   // 16 bytes, base64url without padding
    }

    [Fact]
    public void Seal_then_Open_round_trips()
    {
        var k = CloudCrypto.NewKey();
        var plain = Encoding.UTF8.GetBytes("{\"full_name\":\"Nguyễn Văn A\"}");
        var sealedData = CloudCrypto.Seal(k, "people", "rid1", plain);
        Assert.Equal(plain, CloudCrypto.Open(k, "people", "rid1", sealedData));
    }

    [Fact]
    public void Open_rejects_row_moved_to_another_rid()
    {
        var k = CloudCrypto.NewKey();
        var sealedData = CloudCrypto.Seal(k, "people", "rid1", [1, 2, 3]);
        Assert.Throws<CloudException>(() => CloudCrypto.Open(k, "people", "rid2", sealedData));
        Assert.Throws<CloudException>(() => CloudCrypto.Open(k, "faces", "rid1", sealedData));
        Assert.Throws<CloudException>(() => CloudCrypto.Open(CloudCrypto.NewKey(), "people", "rid1", sealedData));
    }

    [Fact]
    public void Seal_twice_gives_different_ciphertext()
    {
        var k = CloudCrypto.NewKey();
        Assert.NotEqual(CloudCrypto.Seal(k, "people", "r", [9, 9]), CloudCrypto.Seal(k, "people", "r", [9, 9]));
    }
}
