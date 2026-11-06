using System.Net;
using System.Text;
using Acs.Core.Hik;

namespace Acs.Core.Tests.Hik;

public class HikFacesTests
{
    private static HikClient Make(FakeIsapiHandler h) => new("192.168.1.201", "admin", "secret", h);
    private const string Capture = "/ISAPI/AccessControl/CaptureFaceData";
    private const string FdSetUp = "/ISAPI/Intelligent/FDLib/FDSetUp";
    private const string FaceRecord = "/ISAPI/Intelligent/FDLib/FaceDataRecord";
    private const string FdSearch = "/ISAPI/Intelligent/FDLib/FDSearch";
    private const string FdDelete = "/ISAPI/Intelligent/FDLib/FDSearch/Delete";
    private const string UserSearch = "/ISAPI/AccessControl/UserInfo/Search";
    private const string Ok = "{\"statusCode\":1,\"statusString\":\"OK\"}";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 0xFF, 0xD9];
    private static FakeIsapiHandler.Reply Bin(byte[] b, string ct = "image/jpeg") => new(HttpStatusCode.OK, "", ct, b);

    [Fact]
    public async Task Capture_accepts_raw_jpeg_and_base64_xml()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture, _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h).CaptureFaceAsync(default));

        var b64 = Convert.ToBase64String(Jpeg);
        var h2 = new FakeIsapiHandler().On(HttpMethod.Post, Capture,
            $"<CaptureFaceData><faceData>\n{b64}\n</faceData></CaptureFaceData>", HttpStatusCode.OK, "application/xml");
        Assert.Equal(Jpeg, await Make(h2).CaptureFaceAsync(default));

        var h3 = new FakeIsapiHandler().On(HttpMethod.Post, Capture, $"{{\"faceData\":\"{b64}\"}}");
        Assert.Equal(Jpeg, await Make(h3).CaptureFaceAsync(default));

        var h4 = new FakeIsapiHandler().On(HttpMethod.Post, Capture, $"<R><picData>{b64}</picData></R>", HttpStatusCode.OK, "application/xml");
        Assert.Equal(Jpeg, await Make(h4).CaptureFaceAsync(default));
    }

    [Fact]
    public async Task Capture_accepts_jpeg_inside_multipart()
    {
        var body = Encoding.ASCII.GetBytes("--B\r\nContent-Type: application/xml\r\n\r\n<x/>\r\n--B\r\nContent-Type: image/jpeg\r\n\r\n")
            .Concat(Jpeg).Concat(Encoding.ASCII.GetBytes("\r\n--B--")).ToArray();
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture, _ => Bin(body, "multipart/form-data; boundary=B"));
        var got = await Make(h).CaptureFaceAsync(default);
        Assert.Equal(Jpeg, got.Take(Jpeg.Length).ToArray());
    }

    [Fact]
    public async Task Capture_tries_next_body_after_rejection_then_fails_in_vietnamese()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture,
            "{\"statusCode\":4,\"statusString\":\"Invalid Operation\",\"subStatusCode\":\"badXmlFormat\"}");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).CaptureFaceAsync(default));
        Assert.Equal("captureFailed", e.Code);
        Assert.Contains("đứng thẳng", e.Message);
        Assert.Equal(3, h.Requests.Count);
        Assert.Contains("dataType", h.Requests[0].Body);
        Assert.Contains("version=\"2.0\"", h.Requests[1].Body);
        Assert.Equal("", h.Requests[2].Body);
    }

    [Fact]
    public async Task Capture_non_jpeg_payload_is_a_failure()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture, $"<R><faceData>{Convert.ToBase64String([1, 2, 3])}</faceData></R>", HttpStatusCode.OK, "application/xml");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).CaptureFaceAsync(default));
        Assert.Equal("captureFailed", e.Code);
    }

    [Fact]
    public async Task SetFace_sends_multipart_FDSetUp_through_digest()
    {
        var h = new FakeIsapiHandler { RequireDigest = true }.On(HttpMethod.Put, FdSetUp, Ok);
        await Make(h).SetFaceAsync("00123", Jpeg, default);
        var ok = h.Requests.Last();
        Assert.StartsWith("Digest ", ok.Authorization);
        Assert.Equal(2, h.Requests.Count);   // 401 then the replayed multipart
        Assert.Contains("format=json", ok.PathAndQuery);
        Assert.Contains("name=FaceDataRecord", ok.Body.Replace("\"", ""));
        Assert.Contains("\"FPID\":\"00123\"", ok.Body);
        Assert.Contains("\"faceLibType\":\"blackFD\"", ok.Body);
        Assert.Contains("\"FDID\":\"1\"", ok.Body);
        Assert.Contains("name=img", ok.Body.Replace("\"", ""));
        Assert.True(ok.BodyBytes!.AsSpan().IndexOf(Jpeg) >= 0);
    }

    [Fact]
    public async Task SetFace_falls_back_to_FaceDataRecord()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, FdSetUp, "{}", HttpStatusCode.NotFound)
            .On(HttpMethod.Put, FdDelete, Ok)
            .On(HttpMethod.Post, FaceRecord, Ok);
        await Make(h).SetFaceAsync("00123", Jpeg, default);
        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Put, HttpMethod.Post }, h.Requests.Select(r => r.Method));
        Assert.Contains(FdDelete, h.Requests[1].PathAndQuery);
        Assert.Contains(FaceRecord, h.Requests[2].PathAndQuery);
        Assert.Contains("name=img", h.Requests[2].Body.Replace("\"", ""));
    }

    [Fact]
    public async Task SetFace_fallback_ignores_delete_failure_when_no_face_exists()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, FdSetUp, "{}", HttpStatusCode.NotFound)
            .On(HttpMethod.Put, FdDelete, "{\"statusCode\":4,\"statusString\":\"x\",\"subStatusCode\":\"MessageParametersLack\"}")
            .On(HttpMethod.Post, FaceRecord, Ok);
        await Make(h).SetFaceAsync("00123", Jpeg, default);
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task SetFace_200_with_modeling_error_is_rejected()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, FdSetUp,
            "{\"statusCode\":4,\"statusString\":\"Invalid Operation\",\"subStatusCode\":\"modelingFailed\"}");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFaceAsync("00123", Jpeg, default));
        Assert.Equal("modelingFailed", e.Code);
        Assert.Contains("Chụp lại", e.Message);
    }

    [Fact]
    public async Task SetFace_rejects_bad_jpeg_before_sending()
    {
        var h = new FakeIsapiHandler();
        var big = new byte[200 * 1024 + 1];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFaceAsync("1", [1, 2, 3, 4], default));
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFaceAsync("1", big, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task HasFace_reads_numOfMatches()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, FdSearch, s => new FakeIsapiHandler.Reply(HttpStatusCode.OK,
            s.Body.Contains("\"FPID\":\"1\"") ? "{\"numOfMatches\":1,\"MatchList\":[{\"FPID\":\"1\"}]}" : "{\"numOfMatches\":0,\"totalMatches\":0}"));
        var c = Make(h);
        Assert.True(await c.HasFaceAsync("1", default));
        Assert.False(await c.HasFaceAsync("2", default));
    }

    [Fact]
    public async Task DownloadFace_null_when_no_url()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":0}")
            .On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"numOfMatches\":1,\"UserInfo\":[{\"employeeNo\":\"1\"}]}}");
        Assert.Null(await Make(h).DownloadFaceAsync("1", default));
    }

    [Fact]
    public async Task DownloadFace_gets_absolute_or_relative_url_with_digest()
    {
        var h = new FakeIsapiHandler { RequireDigest = true }
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":1,\"MatchList\":[{\"FPID\":\"1\",\"faceURL\":\"http://192.168.1.201/LOCALS/pic/a.jpg@WEB000\"}]}")
            .On(HttpMethod.Get, "/LOCALS/pic/a.jpg@WEB000", _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h).DownloadFaceAsync("1", default));

        var h2 = new FakeIsapiHandler()
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":1,\"MatchList\":[{\"FPID\":\"1\",\"faceURL\":\"/LOCALS/pic/b.jpg\"}]}")
            .On(HttpMethod.Get, "/LOCALS/pic/b.jpg", _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h2).DownloadFaceAsync("1", default));
    }

    [Fact]
    public async Task DownloadFace_falls_back_to_userinfo_picURL()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":0}")
            .On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"UserInfo\":[{\"employeeNo\":\"1\",\"picURL\":\"/pic/c.jpg\"}]}}")
            .On(HttpMethod.Get, "/pic/c.jpg", _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h).DownloadFaceAsync("1", default));
    }

    [Fact]
    public async Task DownloadFace_userinfo_fallback_never_takes_another_persons_photo()
    {
        // Loosely-filtering firmware answers with another user first: that photo must never become 1's face.
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":0}")
            .On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"UserInfo\":[{\"employeeNo\":\"2\",\"faceURL\":\"/pic/other.jpg\"}]}}")
            .On(HttpMethod.Get, "/pic/other.jpg", _ => Bin(Jpeg));
        Assert.Null(await Make(h).DownloadFaceAsync("1", default));
        Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Get);

        var h2 = new FakeIsapiHandler()
            .On(HttpMethod.Post, FdSearch, "{\"numOfMatches\":0}")
            .On(HttpMethod.Post, UserSearch,
                "{\"UserInfoSearch\":{\"UserInfo\":[{\"employeeNo\":\"10\",\"picURL\":\"/pic/other.jpg\"},{\"employeeNo\":\"1\",\"picURL\":\"/pic/mine.jpg\"}]}}")
            .On(HttpMethod.Get, "/pic/mine.jpg", _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h2).DownloadFaceAsync("1", default));
        Assert.Equal("/pic/mine.jpg", h2.Requests.Single(r => r.Method == HttpMethod.Get).PathAndQuery);
    }

    [Fact]
    public async Task DeleteFace_puts_FPID_list()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, FdDelete, Ok);
        await Make(h).DeleteFaceAsync("00123", default);
        var r = h.Requests.Single();
        Assert.Contains("FDID=1", r.PathAndQuery);
        Assert.Contains("faceLibType=blackFD", r.PathAndQuery);
        Assert.Contains("\"FPID\":[{\"value\":\"00123\"}]", r.Body);
    }

    [Fact]
    public async Task Face_search_requires_exact_FPID()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, FdSearch,
            "{\"numOfMatches\":1,\"MatchList\":[{\"FPID\":\"001234\",\"faceURL\":\"/pic/x.jpg\"}]}")
            .On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"UserInfo\":[]}}");
        var c = Make(h);
        Assert.False(await c.HasFaceAsync("00123", default));
        Assert.Null(await c.DownloadFaceAsync("00123", default));
        Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Face_search_finds_exact_FPID_when_listed_second()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, FdSearch,
            "{\"numOfMatches\":2,\"MatchList\":[{\"FPID\":\"001234\",\"faceURL\":\"/pic/x.jpg\"},{\"FPID\":\"00123\",\"faceURL\":\"/pic/ok.jpg\"}]}")
            .On(HttpMethod.Get, "/pic/ok.jpg", _ => Bin(Jpeg));
        var c = Make(h);
        Assert.True(await c.HasFaceAsync("00123", default));
        Assert.Equal(Jpeg, await c.DownloadFaceAsync("00123", default));
    }

    [Fact]
    public async Task DownloadFace_never_requests_another_host()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"UserInfo\":[]}}")
            .On(HttpMethod.Post, FdSearch,
            "{\"MatchList\":[{\"FPID\":\"1\",\"faceURL\":\"http://other-host/pic.jpg\"}]}")
            .On(HttpMethod.Get, "/pic.jpg", _ => Bin(Jpeg));
        Assert.Equal(Jpeg, await Make(h).DownloadFaceAsync("1", default));   // fetched from the device host, path only

        var h2 = new FakeIsapiHandler().On(HttpMethod.Post, UserSearch, "{\"UserInfoSearch\":{\"UserInfo\":[]}}")
            .On(HttpMethod.Post, FdSearch,
            "{\"MatchList\":[{\"FPID\":\"1\",\"faceURL\":\"//evil/x\"}]}");
        Assert.Null(await Make(h2).DownloadFaceAsync("1", default));
        Assert.DoesNotContain(h2.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Capture_empty_200_body_is_a_failure()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture, "", HttpStatusCode.OK, "application/xml");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).CaptureFaceAsync(default));
        Assert.Equal("captureFailed", e.Code);
    }
}
