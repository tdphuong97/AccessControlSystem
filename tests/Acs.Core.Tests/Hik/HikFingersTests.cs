using System.Net;
using Acs.Core.Hik;

namespace Acs.Core.Tests.Hik;

public class HikFingersTests
{
    private static HikClient Make(FakeIsapiHandler h)
        => new("192.168.1.201", "admin", "secret", h) { PollInterval = TimeSpan.FromMilliseconds(1), PollTimeout = TimeSpan.FromMilliseconds(60) };
    private const string Base = "/ISAPI/AccessControl";
    private const string SetUp = Base + "/FingerPrint/SetUp";
    private const string Download = Base + "/FingerPrintDownload";
    private const string Progress = Base + "/FingerPrintProgress";
    private const string Upload = Base + "/FingerPrintUpload";
    private const string Capture = Base + "/CaptureFingerPrint";
    private const string Delete = Base + "/FingerPrint/Delete";
    private const string DeleteProc = Base + "/FingerPrint/DeleteProcess";
    private const string NotSupport = "{\"statusCode\":4,\"subStatusCode\":\"notSupport\"}";
    private static string Status(int s) => "{\"StatusList\":[{\"id\":1,\"cardReaderRecvStatus\":" + s + "}]}";

    [Fact]
    public async Task Capture_returns_quality()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture,
            "{\"CaptureFingerPrint\":{\"fingerData\":\"QUJD\",\"fingerNo\":2,\"fingerPrintQuality\":87}}");
        var f = await Make(h).CaptureFingerprintAsync(2, default);
        Assert.Equal("QUJD", f.TemplateBase64);
        Assert.Equal(87, f.Quality);
        Assert.Contains("\"fingerNo\":2", h.Requests[0].Body);
        Assert.Contains("CaptureFingerPrintCond", h.Requests[0].Body);
    }

    [Fact]
    public async Task Capture_falls_back_to_xml()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Post, Capture, r => r.PathAndQuery.Contains("format=json")
                ? new(HttpStatusCode.OK, "{\"statusCode\":4,\"subStatusCode\":\"badXmlFormat\"}")
                : new(HttpStatusCode.OK, "<CaptureFingerPrint><fingerData>\nQUJD\n</fingerData><fingerPrintQuality>66</fingerPrintQuality></CaptureFingerPrint>", "application/xml"));
        var f = await Make(h).CaptureFingerprintAsync(1, default);
        Assert.Equal(new CapturedFinger("QUJD", 66), f);
    }

    [Fact]
    public async Task Capture_nothing_is_rejected_with_vietnamese_message()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Capture, "{\"CaptureFingerPrint\":{}}");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).CaptureFingerprintAsync(1, default));
        Assert.Equal("captureFailed", e.Code);
        Assert.Contains("bấm lại", e.Message);
    }

    [Fact]
    public async Task SetFinger_uses_SetUp_then_Download()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, SetUp, Status(1));
        await Make(h).SetFingerprintAsync("1001", 3, "QUJD", 1, default);
        Assert.Single(h.Requests);
        var b = h.Requests[0].Body;
        Assert.Contains("\"FingerPrintCfg\"", b);
        Assert.Contains("\"employeeNo\":\"1001\"", b);
        Assert.Contains("\"enableCardReader\":[1]", b);
        Assert.Contains("\"fingerPrintID\":3", b);
        Assert.Contains("\"fingerType\":\"normalFP\"", b);
        Assert.Contains("\"fingerData\":\"QUJD\"", b);

        var polls = 0;
        var h2 = new FakeIsapiHandler()
            .On(HttpMethod.Put, SetUp, NotSupport)
            .On(HttpMethod.Post, Download, "{\"statusCode\":1}")
            .On(HttpMethod.Get, Progress, _ => ++polls < 3
                ? new(HttpStatusCode.OK, "{\"FingerPrintStatus\":{\"totalStatus\":0}}")
                : new(HttpStatusCode.OK, "{\"FingerPrintStatus\":{\"totalStatus\":1,\"StatusList\":[{\"cardReaderRecvStatus\":1}]}}"));
        await Make(h2).SetFingerprintAsync("1001", 3, "QUJD", 1, default);
        Assert.Equal(3, polls);
        Assert.Contains(h2.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery.StartsWith(Download));
    }

    [Fact]
    public async Task SetFinger_progress_timeout_is_failure()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, SetUp, NotSupport)
            .On(HttpMethod.Post, Download, "{\"statusCode\":1}")
            .On(HttpMethod.Get, Progress, "{\"FingerPrintStatus\":{\"totalStatus\":0}}");
        var e = await Assert.ThrowsAsync<HikTransientException>(() => Make(h).SetFingerprintAsync("1001", 1, "QUJD", 1, default));
        Assert.Contains("Thử lại", e.Message);
    }

    [Fact]
    public async Task SetFinger_status3_is_fpMismatch()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, SetUp, Status(3));
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFingerprintAsync("1001", 1, "QUJD", 1, default));
        Assert.Equal("fpMismatch", e.Code);
        Assert.Equal("Vân tay này không hợp với cảm biến của máy", e.Message);
    }

    [Theory]
    [InlineData(2, "Máy chưa sẵn sàng nhận vân tay, thử lại")]
    [InlineData(4, "Máy đầy")]
    public async Task SetFinger_other_statuses_map_to_vietnamese(int status, string message)
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, SetUp, Status(status));
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFingerprintAsync("1001", 1, "QUJD", 1, default));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public async Task SetFinger_unconfirmed_200_is_failure()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Put, SetUp, "{}");
        await Assert.ThrowsAsync<HikTransientException>(() => Make(h).SetFingerprintAsync("1001", 1, "QUJD", 1, default));
    }

    [Fact]
    public async Task SetFinger_progress_with_bad_status_list_is_failure()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, SetUp, NotSupport)
            .On(HttpMethod.Post, Download, "{\"statusCode\":1}")
            .On(HttpMethod.Get, Progress, "{\"FingerPrintStatus\":{\"totalStatus\":1,\"StatusList\":[{\"cardReaderRecvStatus\":4}]}}");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).SetFingerprintAsync("1001", 1, "QUJD", 1, default));
        Assert.Equal("Máy đầy", e.Message);
    }

    [Fact]
    public async Task SetFinger_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, SetUp, NotSupport)
            .On(HttpMethod.Post, Download, "{\"statusCode\":1}")
            .On(HttpMethod.Get, Progress, _ => { cts.Cancel(); return new(HttpStatusCode.OK, "{\"totalStatus\":0}"); });
        var c = Make(h); c.PollTimeout = TimeSpan.FromSeconds(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.SetFingerprintAsync("1001", 1, "QUJD", 1, cts.Token));
    }

    [Fact]
    public async Task ReadFingers_loops_until_NoFP()
    {
        var n = 0;
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload, _ => ++n == 1
            ? new(HttpStatusCode.OK, "{\"FingerPrintInfo\":{\"status\":\"OK\",\"FingerPrintList\":[{\"cardReaderNo\":1,\"fingerPrintID\":2,\"fingerData\":\"AAA=\"}]}}")
            : n == 2
                ? new(HttpStatusCode.OK, "{\"FingerPrintInfo\":{\"status\":\"OK\",\"FingerPrintList\":[{\"fingerPrintID\":5,\"fingerData\":\"BBB=\"}]}}")
                : new(HttpStatusCode.OK, "{\"FingerPrintInfo\":{\"status\":\"NoFP\"}}"));
        var list = await Make(h).ReadFingerprintsAsync("1001", default);
        Assert.Equal(3, n);
        Assert.Equal([new HikFinger(2, "AAA="), new HikFinger(5, "BBB=")], list);
        Assert.Contains("\"FingerPrintCond\"", h.Requests[0].Body);
        Assert.Contains("\"cardReaderNo\":1", h.Requests[0].Body);
    }

    [Fact]
    public async Task ReadFingers_retries_with_flat_body_when_rejected()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload, r => r.Body.Contains("FingerPrintCond")
            ? new(HttpStatusCode.OK, "{\"statusCode\":4,\"subStatusCode\":\"badJsonContent\"}")
            : r.Body.Contains("\"searchResultPosition\":0")
                ? new(HttpStatusCode.OK, "{\"status\":\"OK\",\"FingerPrintList\":[{\"fingerPrintID\":1,\"fingerData\":\"AAA=\"}]}")
                : new(HttpStatusCode.OK, "{\"status\":\"NoFP\"}"));
        var list = await Make(h).ReadFingerprintsAsync("1001", default);
        Assert.Equal([new HikFinger(1, "AAA=")], list);
        Assert.Contains("\"employeeNo\":\"1001\"", h.Requests[1].Body);
    }

    [Fact]
    public async Task ReadFingers_non_json_answer_is_rejected_not_an_empty_list()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload, "<html>busy</html>", HttpStatusCode.OK, "text/html");
        var e = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).ReadFingerprintsAsync("1001", default));
        Assert.Equal("fpListUnreadable", e.Code);
        Assert.Equal(HikClient.FpUnreadableMessage, e.Message);
        Assert.Contains("<html>busy</html>", e.Detail);
        Assert.Equal(2, h.Requests.Count);   // nested form, then the flat form

        var n = 0;   // a later page that is not JSON: the list is incomplete, so unknown too
        var h2 = new FakeIsapiHandler().On(HttpMethod.Post, Upload, _ => ++n % 2 == 1
            ? new(HttpStatusCode.OK, "{\"FingerPrintInfo\":{\"status\":\"MORE\",\"FingerPrintList\":[{\"fingerPrintID\":" + n + ",\"fingerData\":\"AAA=\"}]}}")
            : new(HttpStatusCode.OK, "", "text/plain"));
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h2).ReadFingerprintsAsync("1001", default));
    }

    [Fact]
    public async Task ReadFingers_skips_fingers_of_another_employee()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload, _ => new(HttpStatusCode.OK,
            "{\"FingerPrintInfo\":{\"status\":\"NoFP\",\"FingerPrintList\":[" +
            "{\"employeeNo\":\"2002\",\"fingerPrintID\":1,\"fingerData\":\"XXX=\"}," +
            "{\"employeeNo\":\"1001\",\"fingerPrintID\":3,\"fingerData\":\"AAA=\"}," +
            "{\"fingerPrintID\":4,\"fingerData\":\"BBB=\"}]}}"));
        var list = await Make(h).ReadFingerprintsAsync("1001", default);
        Assert.Equal([new HikFinger(3, "AAA="), new HikFinger(4, "BBB=")], list);   // no employeeNo = the one asked for
    }

    [Fact]
    public async Task ReadFingers_only_other_employees_gives_nothing()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload,
            "{\"FingerPrintInfo\":{\"status\":\"MORE\",\"FingerPrintList\":[{\"employeeNo\":\"2002\",\"fingerPrintID\":1,\"fingerData\":\"XXX=\"}]}}");
        Assert.Empty(await Make(h).ReadFingerprintsAsync("1001", default));
    }

    [Fact]
    public async Task ReadFingers_stops_on_repeated_page()
    {
        var h = new FakeIsapiHandler().On(HttpMethod.Post, Upload,
            "{\"FingerPrintInfo\":{\"status\":\"MORE\",\"FingerPrintList\":[{\"fingerPrintID\":1,\"fingerData\":\"AAA=\"}]}}");
        var list = await Make(h).ReadFingerprintsAsync("1001", default);
        Assert.Single(list);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task DeleteFinger_polls_until_success()
    {
        var n = 0;
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, Delete, "{\"statusCode\":1}")
            .On(HttpMethod.Get, DeleteProc, _ => new(HttpStatusCode.OK,
                ++n < 3 ? "{\"FingerPrintDeleteProcess\":{\"status\":\"processing\"}}" : "{\"FingerPrintDeleteProcess\":{\"status\":\"success\"}}"));
        await Make(h).DeleteFingerprintAsync("1001", 4, default);
        Assert.Equal(3, n);
        var b = h.Requests[0].Body;
        Assert.Contains("\"mode\":\"byEmployeeNo\"", b);
        Assert.Contains("\"EmployeeNoDetail\":{\"employeeNo\":\"1001\",\"fingerPrintID\":[4]}", b);
    }

    [Fact]
    public async Task DeleteFinger_timeout_is_failure()
    {
        var h = new FakeIsapiHandler()
            .On(HttpMethod.Put, Delete, "{\"statusCode\":1}")
            .On(HttpMethod.Get, DeleteProc, "{\"FingerPrintDeleteProcess\":{\"status\":\"processing\"}}");
        await Assert.ThrowsAsync<HikTransientException>(() => Make(h).DeleteFingerprintAsync("1001", 4, default));
    }
}
