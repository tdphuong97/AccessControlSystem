using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Acs.Core.Tests.Cloud;

/// <summary>In-memory acs-sync Worker with the same rules as cloud/src/worker.js (key "k").</summary>
public sealed class FakeCloud : HttpMessageHandler
{
    public sealed record Row(string Tbl, string Rid, long Ver, bool Deleted, string? Data, string? Pc, string At);
    private readonly object _gate = new();
    public Dictionary<(string, string), Row> Rows { get; } = new();
    public JsonObject? Space { get; private set; }
    public long Seq { get; private set; }
    public (string Holder, bool Active, string? Label, long Until)? Lease { get; set; }
    public bool Offline { get; set; }
    public int Calls { get; private set; }
    public Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public const string Key = "k";

    public HttpClient Client() => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Calls++;
        if (Offline) throw new HttpRequestException("offline");
        var path = req.RequestUri!.AbsolutePath;
        if (req.Method == HttpMethod.Get && path == "/health") return J(new JsonObject { ["ok"] = true });
        if (req.Headers.Authorization?.ToString() != "Bearer " + Key) return J(new JsonObject { ["error"] = "unauthorized" }, HttpStatusCode.Unauthorized);
        var body = req.Content is null ? null : JsonNode.Parse(await req.Content.ReadAsStringAsync(ct))?.AsObject();
        lock (_gate)
        {
            return (req.Method.Method, path) switch
            {
                ("GET", "/space") => Space is null ? J(new JsonObject { ["error"] = "no space" }, HttpStatusCode.NotFound) : J(Space.DeepClone()),
                ("PUT", "/space") => PutSpace(body!),
                ("POST", "/push") => Push(body!),
                ("GET", "/pull") => Pull(req.RequestUri),
                ("POST", "/lease") => TakeLease(body!),
                ("DELETE", "/lease") => Release(body!),
                _ => J(new JsonObject(), HttpStatusCode.NotFound),
            };
        }
    }

    private HttpResponseMessage PutSpace(JsonObject b)
    {
        var baseVer = b["baseVer"]!.GetValue<long>();
        var cur = Space?["ver"]!.GetValue<long>() ?? 0;
        if (baseVer != cur) return J(new JsonObject { ["ver"] = cur }, HttpStatusCode.Conflict);
        Space = new JsonObject
        {
            ["wrapped"] = b["wrapped"]!.GetValue<string>(), ["salt"] = b["salt"]!.GetValue<string>(),
            ["iterations"] = b["iterations"]!.GetValue<int>(), ["created"] = Space?["created"]?.GetValue<string>() ?? "2026-11-09T00:00:00Z", ["ver"] = cur + 1,
        };
        return J(new JsonObject { ["ver"] = cur + 1 });
    }

    private HttpResponseMessage Push(JsonObject b)
    {
        var results = new JsonArray();
        foreach (var c in b["changes"]!.AsArray())
        {
            var tbl = c!["tbl"]!.GetValue<string>(); var rid = c["rid"]!.GetValue<string>();
            var bse = c["base"]!.GetValue<long>(); var del = c["deleted"]!.GetValue<bool>();
            Rows.TryGetValue((tbl, rid), out var cur);
            if ((cur is null && bse == 0) || (cur is not null && bse == cur.Ver))
            {
                var row = new Row(tbl, rid, ++Seq, del, c["data"]?.GetValue<string>(), c["pc"]?.GetValue<string>(), "2026-11-09T07:00:00Z");
                Rows[(tbl, rid)] = row;
                results.Add(new JsonObject { ["tbl"] = tbl, ["rid"] = rid, ["ok"] = true, ["ver"] = row.Ver });
            }
            else results.Add(new JsonObject { ["tbl"] = tbl, ["rid"] = rid, ["ok"] = false, ["current"] = Out(cur) });
        }
        return J(new JsonObject { ["results"] = results });
    }

    private HttpResponseMessage Pull(Uri u)
    {
        var q = System.Web.HttpUtility.ParseQueryString(u.Query);
        var since = long.Parse(q["since"] ?? "0");
        var limit = Math.Min(500, int.Parse(q["limit"] ?? "200"));
        var all = Rows.Values.Where(r => r.Ver > since).OrderBy(r => r.Ver).ToList();
        var page = all.Take(limit).ToList();
        var arr = new JsonArray(); foreach (var r in page) arr.Add(Out(r));
        return J(new JsonObject { ["rows"] = arr, ["next"] = page.Count > 0 ? page[^1].Ver : since, ["more"] = all.Count > limit });
    }

    private HttpResponseMessage TakeLease(JsonObject b)
    {
        var holder = b["holder"]!.GetValue<string>(); var active = b["active"]!.GetValue<bool>(); var label = b["label"]?.GetValue<string>();
        var now = NowMs();
        if (Lease is not { } l || l.Until <= now || l.Holder == holder || (active && !l.Active))
        {
            Lease = (holder, active, label, now + 90_000);
            return J(new JsonObject { ["granted"] = true, ["holder"] = holder, ["active"] = active, ["label"] = label, ["until"] = now + 90_000 });
        }
        return J(new JsonObject { ["granted"] = false, ["holder"] = l.Holder, ["active"] = l.Active, ["label"] = l.Label, ["until"] = l.Until });
    }

    private HttpResponseMessage Release(JsonObject b)
    {
        var ok = Lease is { } l && l.Holder == b["holder"]!.GetValue<string>();
        if (ok) Lease = null;
        return J(new JsonObject { ["released"] = ok });
    }

    private static JsonObject? Out(Row? r) => r is null ? null : new JsonObject
    {
        ["tbl"] = r.Tbl, ["rid"] = r.Rid, ["ver"] = r.Ver, ["deleted"] = r.Deleted, ["data"] = r.Data, ["pc"] = r.Pc, ["at"] = r.At,
    };

    private static HttpResponseMessage J(JsonNode? n, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(n?.ToJsonString() ?? "null", Encoding.UTF8, "application/json") };

    /// <summary>Every stored payload, base64-decoded, for the "server never sees plaintext" check.</summary>
    public IEnumerable<byte[]> StoredBytes() => Rows.Values.Where(r => r.Data is not null).Select(r => Convert.FromBase64String(r.Data!))
        .Concat(Rows.Values.Select(r => Encoding.UTF8.GetBytes(r.Rid + r.Tbl + r.Pc)));
}
