using System.Net;
using System.Text;
using Acs.Core.Cloud;

namespace Acs.Core.Tests.Cloud;

public sealed class CloudClientTests
{
    private sealed class Scripted(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Auth, string? Body)> Seen { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!.PathAndQuery, r.Headers.Authorization?.ToString(), body));
            return answer(r, body);
        }
    }

    private static HttpResponseMessage Json(string s, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    private static readonly CloudCode Code = CloudCode.Parse("acs1:acs.test:key123");

    private static (CloudClient Client, Scripted H) Make(Func<HttpRequestMessage, string?, HttpResponseMessage> answer)
    {
        var h = new Scripted(answer);
        return (new CloudClient(new HttpClient(h), Code), h);
    }

    [Fact]
    public async Task sends_bearer_key_and_json()
    {
        var (c, h) = Make((_, _) => Json("""{"results":[{"tbl":"people","rid":"r1","ok":true,"ver":7}]}"""));
        await c.PushAsync([new RowChange("people", "r1", 0, false, "DATA", "pc1")], default);
        var s = h.Seen.Single();
        Assert.Equal(HttpMethod.Post, s.Method);
        Assert.Equal("/push", s.Path);
        Assert.Equal("Bearer key123", s.Auth);
        Assert.Contains("\"changes\":[{\"tbl\":\"people\",\"rid\":\"r1\",\"base\":0,\"deleted\":false,\"data\":\"DATA\",\"pc\":\"pc1\"}]", s.Body);
    }

    [Fact]
    public async Task pull_parses_rows_and_paging()
    {
        var (c, h) = Make((_, _) => Json("""{"rows":[{"tbl":"faces","rid":"r9","ver":12,"deleted":true,"data":null,"pc":"p2","at":"2026-11-09T07:00:00.000Z"}],"next":12,"more":true}"""));
        var page = await c.PullAsync(5, 100, default);
        Assert.Equal("/pull?since=5&limit=100", h.Seen.Single().Path);
        Assert.True(page.More);
        Assert.Equal(12, page.Next);
        var row = page.Rows.Single();
        Assert.Equal(("faces", "r9", 12L, true, (string?)null, "p2"), (row.Tbl, row.Rid, row.Ver, row.Deleted, row.Data, row.Pc));
    }

    [Fact]
    public async Task push_maps_ok_and_conflict_results()
    {
        var (c, _) = Make((_, _) => Json("""{"results":[{"tbl":"people","rid":"a","ok":true,"ver":3},{"tbl":"people","rid":"b","ok":false,"current":{"tbl":"people","rid":"b","ver":9,"deleted":false,"data":"CUR","pc":"p2","at":"x"}}]}"""));
        var r = await c.PushAsync([new RowChange("people", "a", 0, false, "1", "p"), new RowChange("people", "b", 4, false, "2", "p")], default);
        Assert.True(r[0].Ok); Assert.Equal(3, r[0].Ver); Assert.Null(r[0].Current);
        Assert.False(r[1].Ok); Assert.Equal(9, r[1].Current!.Ver); Assert.Equal("CUR", r[1].Current!.Data);
    }

    [Fact]
    public async Task get_space_returns_null_on_404_and_parses_it_otherwise()
    {
        var (none, _) = Make((_, _) => Json("""{"error":"no space"}""", HttpStatusCode.NotFound));
        Assert.Null(await none.GetSpaceAsync(default));
        var (some, _) = Make((_, _) => Json("""{"wrapped":"W","salt":"S","iterations":600000,"created":"C","ver":2}"""));
        var s = await some.GetSpaceAsync(default);
        Assert.Equal(new WrappedKey("W", "S", 600000), s!.Key);
        Assert.Equal(2, s.Ver);
    }

    [Fact]
    public async Task error_401_is_CloudException_with_message()
    {
        var (c, _) = Make((_, _) => Json("""{"error":"unauthorized"}""", HttpStatusCode.Unauthorized));
        var e = await Assert.ThrowsAsync<CloudException>(() => c.PullAsync(0, 10, default));
        Assert.Equal("Mã kết nối không đúng hoặc đã được đổi.", e.Message);
    }

    [Fact]
    public async Task server_errors_and_network_errors_are_offline()
    {
        var (c5, _) = Make((_, _) => Json("<h1>504</h1>", HttpStatusCode.GatewayTimeout));
        var e = await Assert.ThrowsAsync<CloudOfflineException>(() => c5.PullAsync(0, 10, default));
        Assert.Equal("Không kết nối được dữ liệu chung. Phần mềm vẫn chạy bằng dữ liệu trên máy và sẽ tự thử lại.", e.Message);
        var (cn, _) = Make((_, _) => throw new HttpRequestException("no route"));
        await Assert.ThrowsAsync<CloudOfflineException>(() => cn.LeaseAsync("pc", true, null, default));
    }

    [Fact]
    public async Task put_space_conflict_message()
    {
        var (c, h) = Make((_, _) => Json("""{"ver":4}""", HttpStatusCode.Conflict));
        var e = await Assert.ThrowsAsync<CloudException>(() => c.PutSpaceAsync(new WrappedKey("w", "s", 1), 3, default));
        Assert.Equal("Dữ liệu chung vừa được máy khác thay đổi. Hãy thử lại.", e.Message);
        Assert.Contains("\"baseVer\":3", h.Seen.Single().Body);
    }

    [Fact]
    public async Task lease_round_trip()
    {
        var (c, h) = Make((_, _) => Json("""{"granted":false,"holder":"B","active":true,"label":"LBL","until":1794215000000}"""));
        var l = await c.LeaseAsync("A", false, "mine", default);
        Assert.False(l.Granted); Assert.Equal("B", l.Holder); Assert.True(l.Active); Assert.Equal("LBL", l.Label);
        Assert.Contains("\"holder\":\"A\"", h.Seen.Single().Body);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1794215000000).UtcDateTime, l.Until);
    }
}
