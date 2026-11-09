using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acs.Core.Cloud;

public sealed record SpaceInfo(WrappedKey Key, long Ver, string Created);
public sealed record RowChange(string Tbl, string Rid, long Base, bool Deleted, string? Data, string Pc);
public sealed record CloudRow(string Tbl, string Rid, long Ver, bool Deleted, string? Data, string? Pc, string At);
public sealed record PushResult(string Tbl, string Rid, bool Ok, long Ver, CloudRow? Current);
public sealed record PullPage(IReadOnlyList<CloudRow> Rows, long Next, bool More);
public sealed record LeaseState(bool Granted, string? Holder, bool Active, string? Label, DateTime Until, bool Took = false);

/// <summary>The acs-sync Worker's HTTP API (see cloud/src/worker.js). Every call has its own 20 s limit.</summary>
public sealed class CloudClient(HttpClient http, CloudCode code)
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(60);
    private const string Offline = "Không kết nối được dữ liệu chung. Phần mềm vẫn chạy bằng dữ liệu trên máy và sẽ tự thử lại.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<bool> HealthAsync(CancellationToken ct)
    {
        try { using var r = await SendAsync(HttpMethod.Get, "/health", null, ct, auth: false); return r.IsSuccessStatusCode; }
        catch (CloudOfflineException) { return false; }
    }

    public async Task<SpaceInfo?> GetSpaceAsync(CancellationToken ct)
    {
        using var r = await SendAsync(HttpMethod.Get, "/space", null, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        var s = await ReadAsync<SpaceDto>(r, ct);
        return new SpaceInfo(new WrappedKey(s.Wrapped, s.Salt, s.Iterations), s.Ver, s.Created);
    }

    public async Task<long> PutSpaceAsync(WrappedKey key, long baseVer, CancellationToken ct)
    {
        using var r = await SendAsync(HttpMethod.Put, "/space", new { wrapped = key.Wrapped, salt = key.Salt, iterations = key.Iterations, baseVer }, ct);
        if (r.StatusCode == HttpStatusCode.Conflict) throw new CloudException("Dữ liệu chung vừa được máy khác thay đổi. Hãy thử lại.");
        return (await ReadAsync<VerDto>(r, ct)).Ver;
    }

    public async Task<IReadOnlyList<PushResult>> PushAsync(IReadOnlyList<RowChange> changes, CancellationToken ct)
    {
        var body = new { changes = changes.Select(c => new { tbl = c.Tbl, rid = c.Rid, @base = c.Base, deleted = c.Deleted, data = c.Data, pc = c.Pc }) };
        using var r = await SendAsync(HttpMethod.Post, "/push", body, ct, timeout: PushTimeout);
        return (await ReadAsync<PushDto>(r, ct)).Results.Select(x => new PushResult(x.Tbl, x.Rid, x.Ok, x.Ver, x.Current)).ToList();
    }

    public async Task<PullPage> PullAsync(long since, int limit, CancellationToken ct)
    {
        using var r = await SendAsync(HttpMethod.Get, $"/pull?since={since}&limit={limit}", null, ct);
        var p = await ReadAsync<PullDto>(r, ct);
        return new PullPage(p.Rows, p.Next, p.More);
    }

    public async Task<LeaseState> LeaseAsync(string holder, bool active, string? label, CancellationToken ct)
    {
        using var r = await SendAsync(HttpMethod.Post, "/lease", new { holder, active, label }, ct);
        var l = await ReadAsync<LeaseDto>(r, ct);
        return new LeaseState(l.Granted, l.Holder, l.Active, l.Label, DateTimeOffset.FromUnixTimeMilliseconds(l.Until).UtcDateTime, l.Took);
    }

    public async Task ReleaseLeaseAsync(string holder, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Delete, "/lease", new { holder }, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool auth = true, TimeSpan? timeout = null)
    {
        using var req = new HttpRequestMessage(method, new Uri(code.BaseUrl, path));
        if (auth) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", code.Key);
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? CallTimeout);
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, cts.Token); }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested) { throw new CloudOfflineException(Offline, e); }
        catch (HttpRequestException e) { throw new CloudOfflineException(Offline, e); }
        if (res.StatusCode == HttpStatusCode.Unauthorized) { res.Dispose(); throw new CloudException("Mã kết nối không đúng hoặc đã được đổi."); }
        if ((int)res.StatusCode >= 500)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            res.Dispose();
            throw new CloudOfflineException(Offline, new HttpRequestException($"HTTP {(int)res.StatusCode}: {text[..Math.Min(200, text.Length)]}"));
        }
        return res;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r, CancellationToken ct)
    {
        if (!r.IsSuccessStatusCode)
        {
            var text = await r.Content.ReadAsStringAsync(ct);
            throw new CloudOfflineException(Offline, new HttpRequestException($"HTTP {(int)r.StatusCode}: {text[..Math.Min(200, text.Length)]}"));
        }
        try { return JsonSerializer.Deserialize<T>(await r.Content.ReadAsStringAsync(ct), Json) ?? throw new JsonException("empty"); }
        catch (JsonException e) { throw new CloudOfflineException(Offline, e); }
    }

    private sealed record SpaceDto(string Wrapped, string Salt, int Iterations, string Created, long Ver);
    private sealed record VerDto(long Ver);
    private sealed record PushItem(string Tbl, string Rid, bool Ok, long Ver, CloudRow? Current);
    private sealed record PushDto(List<PushItem> Results);
    private sealed record PullDto(List<CloudRow> Rows, long Next, bool More);
    private sealed record LeaseDto(bool Granted, string? Holder, bool Active, string? Label, long Until, bool Took);
}
