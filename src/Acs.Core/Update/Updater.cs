using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Acs.Core.Update;

/// <summary>A newer version, as the newest release's latest.json describes it.</summary>
public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string? Notes { get; init; }
    /// <summary>The release page, for reading what changed or downloading by hand.</summary>
    public string? Page { get; init; }
    /// <summary>The installer for this system; null where there is none.</summary>
    public UpdatePackage? Package { get; init; }
}

public sealed class UpdatePackage
{
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// New versions are GitHub releases. Each carries latest.json naming the installer for each system with
/// its size and SHA-256; the "latest/download" address always points at the newest release's copy.
/// When github.com fails (2026-11-09: 504 for releases/latest/* while api.github.com worked), the feed and the
/// installer are fetched through the release API instead. The installer's size and SHA-256 are checked either way.
/// </summary>
public sealed class Updater(HttpClient http, string feedUrl, TimeProvider? time = null, string? releaseApi = null)
{
    /// <summary>The GitHub repository the releases are published to.</summary>
    public const string Repo = "tdphuong97/AccessControlSystem";
    public const string FeedEnvVar = "ACS_UPDATE_FEED";
    public static readonly string DefaultFeed = $"https://github.com/{Repo}/releases/latest/download/latest.json";
    public static readonly string DefaultReleaseApi = $"https://api.github.com/repos/{Repo}/releases/latest";

    // Only the GitHub feed has an API to fall back on; a test feed (ACS_UPDATE_FEED) never calls GitHub.
    private readonly string? _api = releaseApi ?? (feedUrl == DefaultFeed ? DefaultReleaseApi : null);
    private Dictionary<string, string>? _assets;   // asset file name -> API address, filled by the fallback

    /// <summary>The feed to use: the ACS_UPDATE_FEED environment variable if set, otherwise the GitHub release.</summary>
    public static string ResolveFeed()
    {
        var v = Environment.GetEnvironmentVariable(FeedEnvVar);
        return string.IsNullOrWhiteSpace(v) ? DefaultFeed : v;
    }

    /// <summary>Adds ?t=unix-seconds (or &amp;t= when the URL already has a query) so caches never serve an old feed.</summary>
    public static string WithCacheBuster(string url, TimeProvider? time = null)
    {
        var t = (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var hash = url.IndexOf('#');
        var main = hash >= 0 ? url[..hash] : url;
        var frag = hash >= 0 ? url[hash..] : "";
        return main + (main.Contains('?') ? "&" : "?") + "t=" + t + frag;
    }

    public async Task<UpdateInfo?> CheckAsync(string currentVersion, string platform, CancellationToken ct)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(await ReadFeedAsync(ct)).RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new UpdateException("Không kiểm tra được bản mới. Hãy thử lại khi có mạng.", ex);
        }

        var version = Text(root, "version");
        if (version is null || !IsNewer(version, currentVersion)) return null;
        UpdatePackage? package = null;
        if (root.TryGetProperty(platform, out var p) && Text(p, "url") is { } url && Text(p, "sha256") is { } sha)
            package = new UpdatePackage { Url = url, Sha256 = sha, Size = p.TryGetProperty("size", out var size) ? size.GetInt64() : 0 };
        return new UpdateInfo { Version = version, Notes = Text(root, "notes"), Page = Text(root, "page"), Package = package };
    }

    private async Task<string> ReadFeedAsync(CancellationToken ct)
    {
        UpdateException failure;
        try
        {
            using var res = await http.GetAsync(WithCacheBuster(feedUrl, time), ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            failure = new UpdateException($"Không kiểm tra được bản mới (máy chủ trả lời {(int)res.StatusCode}).");
        }
        catch (HttpRequestException ex) when (_api is not null)
        {
            failure = new UpdateException("Không kiểm tra được bản mới. Hãy thử lại khi có mạng.", ex);
        }
        if (_api is null) throw failure;
        try
        {
            if ((await AssetsAsync(ct)).TryGetValue("latest.json", out var asset))
            {
                using var res = await GetAssetAsync(asset, HttpCompletionOption.ResponseContentRead, ct);
                if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
        throw failure;
    }

    /// <summary>The newest release's files, by name, as the release API lists them.</summary>
    private async Task<Dictionary<string, string>> AssetsAsync(CancellationToken ct)
    {
        if (_assets is not null) return _assets;
        using var req = new HttpRequestMessage(HttpMethod.Get, _api);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await http.SendAsync(req, ct);
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!res.IsSuccessStatusCode) return found;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("assets", out var list)
            && list.ValueKind == JsonValueKind.Array)
            foreach (var a in list.EnumerateArray())
                if (Text(a, "name") is { } name && Text(a, "url") is { } url) found[name] = url;
        return _assets = found;
    }

    /// <summary>Asks the API for the file itself (it redirects to the download), not for its description.</summary>
    private Task<HttpResponseMessage> GetAssetAsync(string url, HttpCompletionOption option, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/octet-stream");
        return http.SendAsync(req, option, ct);
    }

    /// <summary>The installer response: its own link first, then the same file through the release API.</summary>
    private async Task<HttpResponseMessage> OpenInstallerAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage? res = null;
        try
        {
            res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (res.IsSuccessStatusCode || _api is null) return res;
        }
        catch (HttpRequestException) when (_api is not null) { }
        var name = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Path.GetFileName(u.AbsolutePath) : "";
        try
        {
            if (name.Length > 0 && (await AssetsAsync(ct)).TryGetValue(name, out var asset))
            {
                var alt = await GetAssetAsync(asset, HttpCompletionOption.ResponseHeadersRead, ct);
                if (alt.IsSuccessStatusCode) { res?.Dispose(); return alt; }
                alt.Dispose();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
        return res ?? throw new HttpRequestException("Không tải được bản mới.");
    }

    static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>1.10.0 is newer than 1.9.3: versions compare part by part, as numbers.</summary>
    public static bool IsNewer(string candidate, string current) =>
        System.Version.TryParse(candidate, out var a) && System.Version.TryParse(current, out var b) && Normal(a) > Normal(b);

    static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>
    /// Downloads the installer into <paramref name="folder"/> and returns its path, once its size and
    /// SHA-256 match latest.json; anything else is deleted. Progress goes from 0 to 1.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string folder, Action<double>? progress, CancellationToken ct)
    {
        var package = info.Package ?? throw new UpdateException("Bản mới chưa có bộ cài cho máy này.");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"AccessControlSystem-Setup-{info.Version}.exe");
        var part = path + ".part";
        try
        {
            using (var res = await OpenInstallerAsync(package.Url, ct))
            {
                if (!res.IsSuccessStatusCode) throw new UpdateException($"Không tải được bản mới (máy chủ trả lời {(int)res.StatusCode}).");
                var total = package.Size > 0 ? package.Size : res.Content.Headers.ContentLength ?? 0;
                await using var input = await res.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(part);
                var buffer = new byte[256 * 1024];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Invoke(Math.Min(0.99, (double)done / total));
                }
            }

            await using (var check = File.OpenRead(part))
            {
                if (package.Size > 0 && check.Length != package.Size)
                    throw new UpdateException("Bản mới tải về không đủ, hãy thử lại.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("Bản mới tải về bị hỏng hoặc không đúng, phần mềm đã bỏ nó đi.");
            }
            File.Move(part, path, overwrite: true);
            progress?.Invoke(1);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try { File.Delete(part); } catch { }
            throw ex as UpdateException ?? new UpdateException("Không tải được bản mới. Hãy thử lại khi có mạng.", ex);
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }
}
