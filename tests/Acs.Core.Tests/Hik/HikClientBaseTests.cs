using System.Net;
using Acs.Core.Hik;
using Acs.Core.Model;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Hik;

public class HikClientBaseTests
{
    private static HikClient Make(FakeIsapiHandler h) => new("192.168.1.201", "admin", "secret", h);

    [Fact]
    public async Task Digest_challenge_is_answered()
    {
        var h = new FakeIsapiHandler { RequireDigest = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{\"model\":\"DS-K1T\",\"serialNumber\":\"S1\",\"firmwareVersion\":\"V1\"}}");
        var info = await Make(h).GetDeviceInfoAsync(default);
        Assert.Equal("DS-K1T", info.Model);
        Assert.Equal(2, h.Requests.Count);
        Assert.Null(h.Requests[0].Authorization);
        var auth = h.Requests[1].Authorization!;
        Assert.Contains("qop=auth", auth);
        var ha1 = FakeIsapiHandler.Md5("admin:DS-Fake:secret");
        var ha2 = FakeIsapiHandler.Md5("GET:" + FakeIsapiHandler.Param(auth, "uri"));
        var expect = FakeIsapiHandler.Md5($"{ha1}:abc:{FakeIsapiHandler.Param(auth, "nc")}:{FakeIsapiHandler.Param(auth, "cnonce")}:auth:{ha2}");
        Assert.Equal(expect, FakeIsapiHandler.Param(auth, "response"));
    }

    [Fact]
    public async Task Nonce_is_reused_on_next_request()
    {
        var h = new FakeIsapiHandler { RequireDigest = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = Make(h);
        await c.GetDeviceInfoAsync(default);
        await c.GetDeviceInfoAsync(default);
        Assert.Equal(3, h.Requests.Count);
        Assert.Equal("00000002", FakeIsapiHandler.Param(h.Requests[2].Authorization!, "nc"));
    }

    [Fact]
    public async Task Second_401_throws_HikAuthException_and_stops()
    {
        var h = new FakeIsapiHandler { RequireDigest = true, Password = "other" };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{}");
        await Assert.ThrowsAsync<HikAuthException>(() => Make(h).GetDeviceInfoAsync(default));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Wrong_password_costs_one_failed_login_per_call_not_two()
    {
        var h = new FakeIsapiHandler { RequireDigest = true, Password = "other" };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{}");
        var c = Make(h);
        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<HikAuthException>(() => c.GetDeviceInfoAsync(default));
        // Every call: a plain request (challenge only, no credentials sent) then exactly one digest attempt.
        Assert.Equal(6, h.Requests.Count);
        Assert.Equal(3, h.Requests.Count(r => r.Authorization is not null));
        Assert.All(new[] { 0, 2, 4 }, i => Assert.Null(h.Requests[i].Authorization));
    }

    [Fact]
    public async Task A_good_login_still_reuses_the_nonce_after_an_earlier_failure()
    {
        var h = new FakeIsapiHandler { RequireDigest = true, Password = "other" };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = new HikClient("192.168.1.201", "admin", "secret", h);
        await Assert.ThrowsAsync<HikAuthException>(() => c.GetDeviceInfoAsync(default));
        h.Password = "secret";   // e.g. the lock-out ended / the device password was put back
        await c.GetDeviceInfoAsync(default);
        await c.GetDeviceInfoAsync(default);
        Assert.Equal(5, h.Requests.Count);   // 2 failed + challenge, answer, then the cached nonce straight away
        Assert.NotNull(h.Requests[4].Authorization);
    }

    [Fact]
    public async Task An_expired_nonce_answered_without_a_challenge_starts_a_fresh_login()
    {
        // Seen on site (DS-K1T9342MFWX V4.39): the device drops a nonce ~3 s after issuing it and answers the next
        // request that still carries it with a bare 401 (no WWW-Authenticate). That is not a wrong password.
        var h = new FakeIsapiHandler { RequireDigest = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{\"model\":\"DS-K1T\"}}");
        var c = Make(h);
        await c.GetDeviceInfoAsync(default);
        h.ExpireNonce();
        var info = await c.GetDeviceInfoAsync(default);
        Assert.Equal("DS-K1T", info.Model);
        // challenge, answer | stale nonce (bare 401), plain request (fresh challenge), answer with the new nonce
        Assert.Equal(5, h.Requests.Count);
        Assert.Null(h.Requests[3].Authorization);
        Assert.Equal(h.CurrentNonce, FakeIsapiHandler.Param(h.Requests[4].Authorization!, "nonce"));
        Assert.Equal("00000001", FakeIsapiHandler.Param(h.Requests[4].Authorization!, "nc"));
    }

    [Fact]
    public async Task An_expired_nonce_then_a_wrong_password_still_fails_once_and_stops()
    {
        var h = new FakeIsapiHandler { RequireDigest = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = Make(h);
        await c.GetDeviceInfoAsync(default);
        h.ExpireNonce();
        h.Password = "changed-on-the-device";
        await Assert.ThrowsAsync<HikAuthException>(() => c.GetDeviceInfoAsync(default));
        // stale nonce, plain request, ONE answer that fails: no loop, no second password attempt
        Assert.Equal(5, h.Requests.Count);
        await Assert.ThrowsAsync<HikAuthException>(() => c.GetDeviceInfoAsync(default));
        Assert.Null(h.Requests[5].Authorization);   // after a real failure the next call starts without credentials
    }

    [Fact]
    public async Task A_fresh_nonce_dropped_by_another_login_is_retried_once()
    {
        var h = new FakeIsapiHandler { RequireDigest = true, DropNextAnswers = 1 };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        await Make(h).GetDeviceInfoAsync(default);
        // plain, answer (dropped: bare 401, no failed-login counters), plain, answer
        Assert.Equal(4, h.Requests.Count);
        Assert.Null(h.Requests[2].Authorization);
    }

    [Fact]
    public async Task Bare_401s_stop_after_two_logins()
    {
        var h = new FakeIsapiHandler { RequireDigest = true, DropNextAnswers = 99 };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        await Assert.ThrowsAsync<HikAuthException>(() => Make(h).GetDeviceInfoAsync(default));
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact]
    public async Task A_device_style_wrong_password_is_never_retried()
    {
        // Real device, wrong password: bare 401 with <retryLoginTime>. One failed login per call, no second attempt.
        var h = new FakeIsapiHandler { RequireDigest = true, DeviceStyleWrongPassword = true, Password = "other" };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{}");
        await Assert.ThrowsAsync<HikAuthException>(() => Make(h).GetDeviceInfoAsync(default));
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task A_wrong_password_on_a_cached_nonce_is_not_retried()
    {
        // The password was changed on the device while our nonce was still fresh: the bare 401 carries the counters.
        var h = new FakeIsapiHandler { RequireDigest = true, DeviceStyleWrongPassword = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = Make(h);
        await c.GetDeviceInfoAsync(default);
        h.Password = "changed-on-the-device";
        var ex = await Assert.ThrowsAsync<HikAuthException>(() => c.GetDeviceInfoAsync(default));
        Assert.Equal(3, h.Requests.Count);
        Assert.Contains("retryLoginTime", ex.Detail);   // the body still reaches the error map
    }

    [Fact]
    public async Task A_nonce_older_than_two_seconds_is_not_sent_again()
    {
        // Measured on site: a nonce still worked 2.2 s after it was issued and failed at 3.4 s. Past 2 s the handler
        // asks for a fresh challenge instead of sending a nonce the device has most likely dropped (the 30 s ping).
        var time = new FakeTimeProvider();
        var h = new FakeIsapiHandler { RequireDigest = true };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = new HikClient("192.168.1.201", "admin", "secret", h, time);
        await c.GetDeviceInfoAsync(default);
        time.Advance(TimeSpan.FromSeconds(1));
        await c.GetDeviceInfoAsync(default);
        Assert.Equal(3, h.Requests.Count);            // still fresh: reused
        time.Advance(TimeSpan.FromSeconds(30));
        await c.GetDeviceInfoAsync(default);
        Assert.Equal(5, h.Requests.Count);            // plain request + answer, no stale attempt
        Assert.Null(h.Requests[3].Authorization);
    }

    [Fact]
    public async Task Calls_to_one_device_never_overlap()
    {
        // Seen on site: four logins in flight at once, one fresh-challenge answer got 401 invalidOperation.
        var h = new FakeIsapiHandler { RequireDigest = true, Delay = TimeSpan.FromMilliseconds(30) };
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{\"DeviceInfo\":{}}");
        var c = Make(h);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => c.GetDeviceInfoAsync(default)));
        Assert.Equal(1, h.MaxInFlight);
    }

    [Fact]
    public async Task Http200_with_statusCode6_is_rejected_json()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo",
            "{\"statusCode\":6,\"statusString\":\"Invalid Content\",\"subStatusCode\":\"badJsonContent\",\"errorMsg\":\"x\"}");
        var ex = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).GetDeviceInfoAsync(default));
        Assert.Equal("badJsonContent", ex.Code);
    }

    [Fact]
    public async Task Http200_with_statusCode6_is_rejected_xml()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo",
            "<?xml version=\"1.0\"?><ResponseStatus><statusCode>6</statusCode><subStatusCode>weird</subStatusCode><statusString>Invalid</statusString></ResponseStatus>",
            contentType: "application/xml");
        var ex = await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).GetDeviceInfoAsync(default));
        Assert.Equal("weird", ex.Code);
        Assert.StartsWith("Máy từ chối yêu cầu (mã weird)", ex.Message);
    }

    [Fact]
    public void ErrorMap_cases()
    {
        Assert.Null(HikErrorMap.FromResponse(200, "{\"statusCode\":1}"));
        Assert.Null(HikErrorMap.FromResponse(200, "{\"UserInfoCount\":{\"userNumber\":3}}"));
        Assert.IsType<HikNotSupportedException>(HikErrorMap.FromResponse(404, "{}"));
        Assert.IsType<HikNotSupportedException>(HikErrorMap.FromResponse(400, "{\"statusCode\":4,\"subStatusCode\":\"notSupport\"}"));
        Assert.IsType<HikTransientException>(HikErrorMap.FromResponse(503, ""));
        Assert.IsType<HikTransientException>(HikErrorMap.FromResponse(200, "{\"statusCode\":4,\"subStatusCode\":\"deviceBusy\"}"));
        Assert.IsType<HikAuthException>(HikErrorMap.FromResponse(401, ""));
        var r = Assert.IsType<HikRejectedException>(HikErrorMap.FromResponse(200, "{\"statusCode\":4,\"subStatusCode\":\"zzz\",\"errorMsg\":\"boom\"}"));
        Assert.Equal("Máy từ chối yêu cầu (mã zzz). Xem Chi tiết và báo lại cho người làm phần mềm.", r.Message);
        Assert.DoesNotContain("boom", r.Message);
        Assert.Equal("boom", r.Detail);
        Assert.Equal("zzz", r.Code);
        var dup = Assert.IsType<HikRejectedException>(HikErrorMap.FromResponse(200, "{\"statusCode\":4,\"subStatusCode\":\"employeeNoAlreadyExist\"}"));
        Assert.Contains("đã có", dup.Message);
    }

    [Fact]
    public async Task Network_error_is_transient()
    {
        var h = new FakeIsapiHandler { Throw = new HttpRequestException("down") };
        await Assert.ThrowsAsync<HikTransientException>(() => Make(h).GetDeviceInfoAsync(default));
    }

    /// <summary>Answers 200 with a body whose stream breaks while it is read (a connection reset mid-body).</summary>
    private sealed class BrokenBody : HttpMessageHandler
    {
        private sealed class Content : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
                throw new IOException("connection reset by peer");
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new Content() });
    }

    [Fact]
    public async Task Broken_body_io_and_disposed_client_are_transient()
    {
        var e = await Assert.ThrowsAsync<HikTransientException>(() => new HikClient("192.168.1.201", "admin", "secret", new BrokenBody()).GetDeviceInfoAsync(default));
        Assert.DoesNotContain("connection reset", e.Message);   // raw text only in Detail
        await Assert.ThrowsAsync<HikTransientException>(() => Make(new FakeIsapiHandler { Throw = new IOException("reset") }).GetDeviceInfoAsync(default));
        await Assert.ThrowsAsync<HikTransientException>(() => Make(new FakeIsapiHandler { Throw = new ObjectDisposedException("HttpClient") }).GetDeviceInfoAsync(default));
        var disposed = Make(new FakeIsapiHandler().On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{}"));
        disposed.Dispose();   // the factory replaced this client after a login change while a call was due
        await Assert.ThrowsAsync<HikTransientException>(() => disposed.GetDeviceInfoAsync(default));
    }

    [Fact]
    public void Default_handler_never_uses_a_proxy()
    {
        using var c = new HikClient("192.168.1.201", "admin", "secret");
        var http = (HttpClient)typeof(HikClient).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(c)!;
        var handler = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(http)!;
        var inner = Assert.IsType<HttpClientHandler>(((DelegatingHandler)handler).InnerHandler);
        Assert.False(inner.UseProxy);
    }

    [Fact]
    public async Task Timeout_is_transient_but_caller_cancel_is_not()
    {
        var h = new FakeIsapiHandler { Throw = new TaskCanceledException("t") };
        await Assert.ThrowsAsync<HikTransientException>(() => Make(h).GetDeviceInfoAsync(default));
        h.Throw = null;
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", "{}");
        var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Make(h).GetDeviceInfoAsync(cts.Token));
    }

    [Fact]
    public async Task DeviceInfo_parses_xml()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><DeviceInfo xmlns=\"http://www.isapi.org/ver20/XMLSchema\"><deviceName>x</deviceName><model>DS-K1T343</model><serialNumber>SN9</serialNumber><firmwareVersion>V4.1</firmwareVersion></DeviceInfo>",
            contentType: "application/xml");
        var i = await Make(h).GetDeviceInfoAsync(default);
        Assert.Equal(new DeviceInfo("DS-K1T343", "SN9", "V4.1"), i);
    }

    [Fact]
    public async Task Capabilities_parse_and_missing_is_null()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/AccessControl/capabilities",
            "{\"AccessControlCap\":{\"isSupportCaptureFingerPrint\":true,\"maxPersonNum\":3000}}");
        var c = await Make(h).GetCapabilitiesAsync(default);
        Assert.Equal(new DeviceCapabilities(null, true, 3000), c);
    }

    [Fact]
    public void Factory_builds_client()
    {
        var d = new DeviceRecord(1, "Máy tầng trệt", "192.168.1.201", "admin", "p", 1, true, false, null, null, null, "g", false, null);
        Assert.IsType<HikClient>(new HikDeviceFactory().Create(d));
    }

    private const string Bom = "﻿";

    [Fact]
    public async Task Bom_prefixed_json_200_with_error_is_rejected()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", _ => new FakeIsapiHandler.Reply(HttpStatusCode.OK, Bom + "{\"statusCode\":6,\"subStatusCode\":\"badJsonContent\"}"));
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).GetDeviceInfoAsync(default));
    }

    [Fact]
    public async Task Bom_prefixed_xml_200_with_error_is_rejected()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", _ => new FakeIsapiHandler.Reply(HttpStatusCode.OK,
            Bom + "<?xml version=\"1.0\"?><ResponseStatus><statusCode>6</statusCode><subStatusCode>weird</subStatusCode></ResponseStatus>", "application/xml"));
        await Assert.ThrowsAsync<HikRejectedException>(() => Make(h).GetDeviceInfoAsync(default));
    }

    [Fact]
    public async Task Bom_prefixed_deviceInfo_xml_parses()
    {
        var h = new FakeIsapiHandler();
        h.On(HttpMethod.Get, "/ISAPI/System/deviceInfo", _ => new FakeIsapiHandler.Reply(HttpStatusCode.OK,
            Bom + "<DeviceInfo><model>M</model><serialNumber>S</serialNumber><firmwareVersion>F</firmwareVersion></DeviceInfo>", "application/xml"));
        Assert.Equal(new DeviceInfo("M", "S", "F"), await Make(h).GetDeviceInfoAsync(default));
    }

    [Fact]
    public void Factory_caches_per_device_and_rebuilds_on_change()
    {
        var f = new HikDeviceFactory();
        var d = new DeviceRecord(1, "Máy tầng trệt", "192.168.1.201", "admin", "p", 1, true, false, null, null, null, "g", false, null);
        var a = f.Create(d);
        Assert.Same(a, f.Create(d with { Name = "Máy tầng 1" }));
        var b = f.Create(d with { Password = "q" });
        Assert.NotSame(a, b);
        Assert.Same(b, f.Create(d with { Password = "q" }));
        Assert.NotSame(b, f.Create(d with { Id = 2, Password = "q" }));
    }
}
