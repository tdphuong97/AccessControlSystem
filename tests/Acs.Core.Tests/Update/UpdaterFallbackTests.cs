using System.Net;
using System.Security.Cryptography;
using System.Text;
using Acs.Core.Update;

namespace Acs.Core.Tests.Update;

/// <summary>
/// 2026-11-09: github.com answered 504 for releases/latest/* (and at times for versioned downloads) while
/// api.github.com kept working, so "Kiểm tra bản mới" failed. The updater then goes through the release API.
/// </summary>
public sealed class UpdaterFallbackTests : IDisposable
{
    private const string Feed = "https://gh.test/releases/latest/download/latest.json";
    private const string Api = "https://api.test/repos/o/r/releases/latest";
    private const string FeedAsset = "https://api.test/repos/o/r/releases/assets/1";
    private const string SetupAsset = "https://api.test/repos/o/r/releases/assets/2";
    private const string SetupUrl = "https://gh.test/releases/download/v1.3.0/AccessControlSystem-Setup-1.3.0.exe";

    private sealed class Fake : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Routes { get; } = new();
        public List<(string Url, string? Accept)> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var url = req.RequestUri!.GetLeftPart(UriPartial.Path);
            Seen.Add((url, req.Headers.Accept.Count > 0 ? req.Headers.Accept.ToString() : null));
            return Task.FromResult(Routes.TryGetValue(url, out var r) ? r(req) : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly byte[] _setup = Encoding.ASCII.GetBytes("installer bytes");
    private readonly string _folder = Directory.CreateTempSubdirectory("acs-update-fb-").FullName;
    private readonly Fake _h = new();

    private static HttpResponseMessage Status(HttpStatusCode c) => new(c) { Content = new StringContent("<h1>504 Gateway Time-out</h1>") };
    private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    private string FeedJson() =>
        $$$"""{"version":"1.3.0","notes":"n","windows":{"url":"{{{SetupUrl}}}","size":{{{_setup.Length}}},"sha256":"{{{Convert.ToHexString(SHA256.HashData(_setup))}}}"}}""";

    private void ApiWorks()
    {
        _h.Routes[Api] = _ => Json($$$"""{"tag_name":"v1.3.0","assets":[{"name":"latest.json","url":"{{{FeedAsset}}}"},{"name":"AccessControlSystem-Setup-1.3.0.exe","url":"{{{SetupAsset}}}"}]}""");
        _h.Routes[FeedAsset] = r => r.Headers.Accept.ToString() == "application/octet-stream" ? Json(FeedJson()) : Json("""{"id":1}""");
        _h.Routes[SetupAsset] = r => r.Headers.Accept.ToString() == "application/octet-stream"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_setup) }
            : Json("""{"id":2}""");
    }

    private Updater Make() => new(new HttpClient(_h), Feed, releaseApi: Api);

    [Fact]
    public async Task Check_uses_the_release_api_when_the_feed_answers_504()
    {
        _h.Routes[Feed] = _ => Status(HttpStatusCode.GatewayTimeout);
        ApiWorks();
        var info = await Make().CheckAsync("1.2.0", "windows", default);
        Assert.Equal("1.3.0", info!.Version);
        Assert.Equal(SetupUrl, info.Package!.Url);
        Assert.Contains(_h.Seen, s => s.Url == FeedAsset && s.Accept == "application/octet-stream");
    }

    [Fact]
    public async Task Check_uses_the_release_api_when_the_feed_cannot_be_reached()
    {
        _h.Routes[Feed] = _ => throw new HttpRequestException("reset");
        ApiWorks();
        Assert.Equal("1.3.0", (await Make().CheckAsync("1.2.0", "windows", default))!.Version);
    }

    [Fact]
    public async Task Check_does_not_call_the_api_when_the_feed_works()
    {
        _h.Routes[Feed] = _ => Json(FeedJson());
        ApiWorks();
        await Make().CheckAsync("1.2.0", "windows", default);
        Assert.DoesNotContain(_h.Seen, s => s.Url.StartsWith("https://api.test"));
    }

    [Fact]
    public async Task Check_reports_the_feed_status_when_the_api_fails_too()
    {
        _h.Routes[Feed] = _ => Status(HttpStatusCode.GatewayTimeout);
        _h.Routes[Api] = _ => Status(HttpStatusCode.Forbidden);
        var e = await Assert.ThrowsAsync<UpdateException>(() => Make().CheckAsync("1.2.0", "windows", default));
        Assert.Contains("504", e.Message);
    }

    [Fact]
    public async Task Custom_feed_without_api_never_calls_github()
    {
        _h.Routes["http://x.test/latest.json"] = _ => Status(HttpStatusCode.GatewayTimeout);
        var u = new Updater(new HttpClient(_h), "http://x.test/latest.json");
        await Assert.ThrowsAsync<UpdateException>(() => u.CheckAsync("1.2.0", "windows", default));
        Assert.All(_h.Seen, s => Assert.Equal("http://x.test/latest.json", s.Url));
    }

    [Fact]
    public async Task Download_uses_the_release_api_when_the_installer_link_answers_504()
    {
        _h.Routes[Feed] = _ => Json(FeedJson());
        _h.Routes[SetupUrl] = _ => Status(HttpStatusCode.GatewayTimeout);
        ApiWorks();
        var u = Make();
        var info = await u.CheckAsync("1.2.0", "windows", default);
        var path = await u.DownloadAsync(info!, _folder, null, default);
        Assert.Equal(_setup, File.ReadAllBytes(path));
        Assert.Contains(_h.Seen, s => s.Url == SetupAsset && s.Accept == "application/octet-stream");
    }

    [Fact]
    public async Task Download_from_the_api_still_checks_the_sha256()
    {
        _h.Routes[Feed] = _ => Json(FeedJson());
        _h.Routes[SetupUrl] = _ => Status(HttpStatusCode.GatewayTimeout);
        ApiWorks();
        _h.Routes[SetupAsset] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("tampered bytes!")) };
        var u = Make();
        var info = await u.CheckAsync("1.2.0", "windows", default);
        await Assert.ThrowsAsync<UpdateException>(() => u.DownloadAsync(info!, _folder, null, default));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }
}
