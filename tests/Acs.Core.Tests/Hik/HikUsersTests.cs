using System.Net;
using System.Text;
using System.Text.Json;
using Acs.Core.Hik;

namespace Acs.Core.Tests.Hik;

public class HikUsersTests
{
    private static HikClient Make(FakeIsapiHandler h) => new("192.168.1.201", "admin", "secret", h) { PagePause = TimeSpan.Zero };
    private const string Search = "/ISAPI/AccessControl/UserInfo/Search";
    private const string SetUp = "/ISAPI/AccessControl/UserInfo/SetUp";
    private const string Modify = "/ISAPI/AccessControl/UserInfo/Modify";
    private const string Record = "/ISAPI/AccessControl/UserInfo/Record";
    private const string Ok = "{\"statusCode\":1,\"statusString\":\"OK\"}";
    private static string Err(string sub) => "{\"statusCode\":4,\"statusString\":\"Invalid Operation\",\"subStatusCode\":\"" + sub + "\"}";

    private static readonly HikUserWrite Sample = new("00123", "Nguyễn Thị Ánh Tuyết", "2026-01-01", "", true, 1);

    [Fact]
    public async Task ListUsers_follows_MORE_pages()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Post, Search, s =>
        {
            var pos = JsonDocument.Parse(s.Body).RootElement.GetProperty("UserInfoSearchCond").GetProperty("searchResultPosition").GetInt32();
            var n = Math.Min(30, 65 - pos);
            var users = string.Join(",", Enumerable.Range(pos, n).Select(i =>
                $"{{\"employeeNo\":\"{i:D5}\",\"name\":\"U{i}\",\"numOfFace\":1,\"numOfFP\":2,\"Valid\":{{\"enable\":false,\"beginTime\":\"2026-01-01T00:00:00\",\"endTime\":\"2035-12-31T23:59:59\"}}}}"));
            var status = pos + n < 65 ? "MORE" : "OK";
            return new FakeIsapiHandler.Reply(HttpStatusCode.OK, $"{{\"UserInfoSearch\":{{\"responseStatusStrg\":\"{status}\",\"numOfMatches\":{n},\"totalMatches\":65,\"UserInfo\":[{users}]}}}}");
        });
        var all = new List<HikUser>();
        await foreach (var u in Make(h).ListUsersAsync(default)) all.Add(u);
        Assert.Equal(65, all.Count);
        Assert.Equal(3, h.Requests.Count);
        Assert.Equal("00064", all[^1].EmployeeNo);
        Assert.False(all[0].Enabled);
        Assert.Equal(1, all[0].FaceCount);
        Assert.Equal(2, all[0].FingerCount);
        Assert.Equal("2035-12-31T23:59:59", all[0].EndTime);
        Assert.Contains("\"maxResults\":30", h.Requests[0].Body);
    }

    [Fact]
    public async Task FindUser_sends_EmployeeNoList_and_returns_null_when_empty()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Post, Search, "{\"UserInfoSearch\":{\"responseStatusStrg\":\"NO MATCH\",\"numOfMatches\":0}}");
        Assert.Null(await Make(h).FindUserAsync("00123", default));
        Assert.Contains("\"EmployeeNoList\":[{\"employeeNo\":\"00123\"}]", h.Requests[0].Body);
    }

    [Fact]
    public async Task Upsert_falls_back_SetUp_to_Modify_to_Record()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Put, SetUp, "{}", HttpStatusCode.NotFound);
        h.On(HttpMethod.Put, Modify, Err("employeeNoNotExist"));
        h.On(HttpMethod.Post, Record, Ok);
        await Make(h).UpsertUserAsync(Sample, default);
        Assert.Equal(new[] { "PUT " + SetUp, "PUT " + Modify, "POST " + Record },
            h.Requests.Select(r => r.Method.Method + " " + r.PathAndQuery.Split('?')[0]).ToArray());
    }

    [Fact]
    public async Task Upsert_stops_at_SetUp_or_Modify_when_they_succeed()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Put, SetUp, Ok);
        await Make(h).UpsertUserAsync(Sample, default);
        Assert.Single(h.Requests);

        var h2 = new FakeIsapiHandler();
        h2.On(HttpMethod.Put, SetUp, "{}", HttpStatusCode.NotFound);
        h2.On(HttpMethod.Put, Modify, Ok);
        await Make(h2).UpsertUserAsync(Sample, default);
        Assert.Equal(2, h2.Requests.Count);
    }

    [Fact]
    public async Task Upsert_does_not_fall_to_Record_on_other_Modify_errors()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Put, SetUp, "{}", HttpStatusCode.NotFound);
        h.On(HttpMethod.Put, Modify, Err("maxUserNum"));
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).UpsertUserAsync(Sample, default));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Upsert_sends_utf8_name_and_string_employeeNo()
    {
        byte[]? raw = null;
        var c = new HikClient("192.168.1.201", "admin", "secret", new CapturingHandler(b => raw = b));
        await c.UpsertUserAsync(Sample, default);
        var text = Encoding.UTF8.GetString(raw!);
        Assert.Contains("\"employeeNo\":\"00123\"", text);
        Assert.Contains("\"name\":\"Nguyễn Thị Ánh Tuyết\"", text);
    }

    [Fact]
    public async Task Upsert_with_no_end_sends_2035()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Put, SetUp, Ok);
        await Make(h).UpsertUserAsync(Sample, default);
        var v = JsonDocument.Parse(h.Requests[0].Body).RootElement.GetProperty("UserInfo");
        Assert.Equal("2035-12-31T23:59:59", v.GetProperty("Valid").GetProperty("endTime").GetString());
        Assert.Equal("2026-01-01T00:00:00", v.GetProperty("Valid").GetProperty("beginTime").GetString());
        Assert.Equal("local", v.GetProperty("Valid").GetProperty("timeType").GetString());
        Assert.Equal("normal", v.GetProperty("userType").GetString());
        Assert.Equal("1", v.GetProperty("doorRight").GetString());
        Assert.Equal("1", v.GetProperty("RightPlan")[0].GetProperty("planTemplateNo").GetString());
    }

    [Fact]
    public async Task Delete_puts_EmployeeNoList()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Delete", Ok);
        await Make(h).DeleteUserAsync("00123", default);
        Assert.Contains("\"UserInfoDelCond\":{\"EmployeeNoList\":[{\"employeeNo\":\"00123\"}]}", h.Requests[0].Body);
    }

    private sealed class CapturingHandler(Action<byte[]> cap) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            cap(await r.Content!.ReadAsByteArrayAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ok, Encoding.UTF8, "application/json") };
        }
    }
}
