using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Acs.Core.Update;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Update;

public sealed class UpdaterTests : IDisposable
{
    /// <summary>A tiny HTTP server: Files by path, every other path gets Data.</summary>
    private sealed class TestServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public byte[] Data { get; } = new byte[300_000];
        public int StatusCode { get; set; } = 200;
        public string BaseUrl { get; }
        public string Url => BaseUrl + "/files/test.bin";
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();
        public ConcurrentQueue<string> Queries { get; } = new();

        public TestServer()
        {
            new Random(1).NextBytes(Data);
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            BaseUrl = $"http://localhost:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    Queries.Enqueue(ctx.Request.Url!.Query);
                    var res = ctx.Response;
                    try
                    {
                        if (StatusCode != 200) { res.StatusCode = StatusCode; res.Close(); continue; }
                        var data = Files.TryGetValue(ctx.Request.Url.AbsolutePath, out var f) ? f : Data;
                        res.ContentLength64 = data.Length;
                        await res.OutputStream.WriteAsync(data);
                        res.Close();
                    }
                    catch { try { res.Abort(); } catch { } }
                }
            });
        }

        public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
    }

    private readonly TestServer _server = new();
    private readonly HttpClient _http = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("acs-update-").FullName;

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
        try { Directory.Delete(_folder, true); } catch { }
    }

    private string Feed(string version, string? sha = null, long? size = null)
    {
        var json = $$"""
            {
              "version": "{{version}}",
              "notes": "- Sao lưu tự động\n- Sửa lỗi đồng bộ",
              "page": "https://github.com/someone/AccessControlSystem/releases/tag/v{{version}}",
              "windows": { "url": "{{_server.Url}}", "size": {{size ?? _server.Data.Length}}, "sha256": "{{sha ?? Convert.ToHexString(SHA256.HashData(_server.Data))}}" }
            }
            """;
        _server.Files["/feed/latest.json"] = Encoding.UTF8.GetBytes(json);
        return _server.BaseUrl + "/feed/latest.json";
    }

    [Fact]
    public async Task A_newer_version_is_offered_with_its_installer()
    {
        var info = await new Updater(_http, Feed("1.3.0")).CheckAsync("1.2.5", "windows", default);

        Assert.NotNull(info);
        Assert.Equal("1.3.0", info.Version);
        Assert.Equal(_server.Url, info.Package!.Url);
        Assert.Contains("đồng bộ", info.Notes);
    }

    [Theory]
    [InlineData("1.3.0", "1.3.0")]
    [InlineData("1.2.9", "1.3.0")]
    public async Task The_same_or_an_older_version_is_not_offered(string published, string running)
    {
        Assert.Null(await new Updater(_http, Feed(published)).CheckAsync(running, "windows", default));
    }

    [Theory]
    [InlineData("1.10.0", "1.9.3", true)]
    [InlineData("1.2.10", "1.2.9", true)]
    [InlineData("2.0", "1.99.99", true)]
    [InlineData("1.2.5", "1.2.5", false)]
    [InlineData("bad", "1.2.5", false)]
    public void Versions_compare_by_number_not_by_text(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, Updater.IsNewer(candidate, current));
    }

    [Fact]
    public async Task A_system_without_an_installer_still_hears_about_the_new_version()
    {
        var info = await new Updater(_http, Feed("1.3.0")).CheckAsync("1.2.5", "mac", default);
        Assert.NotNull(info);
        Assert.Null(info.Package);
        Assert.Equal("https://github.com/someone/AccessControlSystem/releases/tag/v1.3.0", info.Page);
    }

    [Fact]
    public async Task A_missing_or_broken_feed_says_so_in_words()
    {
        var missing = new Updater(_http, _server.BaseUrl + "/feed/none.json");
        _server.StatusCode = 404;
        await Assert.ThrowsAsync<UpdateException>(() => missing.CheckAsync("1.2.5", "windows", default));

        _server.StatusCode = 200;
        _server.Files["/feed/latest.json"] = Encoding.UTF8.GetBytes("<html>not json</html>");
        await Assert.ThrowsAsync<UpdateException>(() => new Updater(_http, _server.BaseUrl + "/feed/latest.json").CheckAsync("1.2.5", "windows", default));
    }

    [Fact]
    public async Task The_installer_is_downloaded_and_checked()
    {
        var updater = new Updater(_http, Feed("1.3.0"));
        var info = await updater.CheckAsync("1.2.5", "windows", default);
        var seen = new List<double>();

        var path = await updater.DownloadAsync(info!, _folder, seen.Add, default);

        Assert.Equal("AccessControlSystem-Setup-1.3.0.exe", Path.GetFileName(path));
        Assert.Equal(_server.Data, await File.ReadAllBytesAsync(path));
        Assert.Equal(1.0, seen[^1]);
    }

    [Fact]
    public async Task Download_rejects_bad_sha256()
    {
        var updater = new Updater(_http, Feed("1.3.0", sha: new string('0', 64)));
        var info = await updater.CheckAsync("1.2.5", "windows", default);

        await Assert.ThrowsAsync<UpdateException>(() => updater.DownloadAsync(info!, _folder, null, default));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task The_feed_request_carries_a_cache_buster()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        await new Updater(_http, Feed("1.3.0"), time).CheckAsync("1.2.5", "windows", default);
        Assert.Contains("?t=1800000000", _server.Queries);
    }

    [Theory]
    [InlineData("https://x/latest.json", "https://x/latest.json?t=5")]
    [InlineData("https://x/latest.json?a=1", "https://x/latest.json?a=1&t=5")]
    public void Cache_buster_uses_question_mark_or_ampersand(string url, string expected)
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(5));
        Assert.Equal(expected, Updater.WithCacheBuster(url, time));
    }

    [Fact]
    public void Default_feed_points_at_the_github_release_and_env_var_overrides_it()
    {
        Assert.Equal("https://github.com/tdphuong97/AccessControlSystem/releases/latest/download/latest.json", Updater.DefaultFeed);
        var old = Environment.GetEnvironmentVariable(Updater.FeedEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(Updater.FeedEnvVar, "http://localhost:1/feed.json");
            Assert.Equal("http://localhost:1/feed.json", Updater.ResolveFeed());
            Environment.SetEnvironmentVariable(Updater.FeedEnvVar, null);
            Assert.Equal(Updater.DefaultFeed, Updater.ResolveFeed());
        }
        finally { Environment.SetEnvironmentVariable(Updater.FeedEnvVar, old); }
    }
}
