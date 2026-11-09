using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Acs.Core.Tests.Hik;

/// <summary>In-process fake of a Hikvision ISAPI endpoint. Routes by method + path (query ignored); can require Digest.</summary>
public sealed class FakeIsapiHandler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, string PathAndQuery, string? Authorization, string Body, byte[]? BodyBytes = null, string? ContentType = null);
    public sealed record Reply(HttpStatusCode Status, string Body, string ContentType = "application/json", byte[]? Raw = null);

    public const string Realm = "DS-Fake";
    public const string Nonce = "abc";

    private readonly Dictionary<string, Func<Seen, Reply>> _routes = new();
    public List<Seen> Requests { get; } = new();
    public bool RequireDigest { get; set; }
    public string User { get; set; } = "admin";
    public string Password { get; set; } = "secret";
    /// <summary>Throws this from SendAsync (simulates a network error).</summary>
    public Exception? Throw { get; set; }
    /// <summary>Per-request delay, to keep requests in flight at the same time.</summary>
    public TimeSpan Delay { get; set; }
    public int MaxInFlight { get; private set; }
    private int _inFlight;

    private readonly object _lock = new();
    private string _nonce = Nonce;
    private readonly HashSet<string> _expired = new();
    private int _gen;
    public string CurrentNonce { get { lock (_lock) return _nonce; } }

    /// <summary>
    /// Real firmware (DS-K1T9342MFWX V4.39) drops a nonce about 3 s after issuing it. A request still carrying it gets
    /// 401 WITHOUT a WWW-Authenticate header; only a request without credentials gets a fresh challenge.
    /// </summary>
    public void ExpireNonce() { lock (_lock) { _expired.Add(_nonce); _nonce = Nonce + (++_gen); } }

    /// <summary>
    /// Another client logs in between our challenge and our answer this many times: the device drops our fresh nonce
    /// and answers with a bare 401 (no challenge, no failed-login counters). Seen on site with two logins at once.
    /// </summary>
    public int DropNextAnswers { get; set; }

    /// <summary>
    /// Wrong password the way the real device answers it: a bare 401 whose body carries the failed-login counters
    /// (lockStatus / unlockTime / retryLoginTime), instead of the RFC-style fresh challenge.
    /// </summary>
    public bool DeviceStyleWrongPassword { get; set; }

    private const string StaleBody = "<userCheck><statusValue>401</statusValue><statusString>Unauthorized</statusString><isActivated>true</isActivated></userCheck>";
    private const string WrongPasswordBody = "<userCheck><statusValue>401</statusValue><statusString>Unauthorized</statusString><isActivated>true</isActivated><lockStatus>unlock</lockStatus><unlockTime>0</unlockTime><retryLoginTime>4</retryLoginTime></userCheck>";

    private static HttpResponseMessage Bare401(string body) =>
        new(HttpStatusCode.Unauthorized) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };

    public FakeIsapiHandler On(HttpMethod m, string path, Func<Seen, Reply> reply) { _routes[m.Method + " " + path] = reply; return this; }
    public FakeIsapiHandler On(HttpMethod m, string path, string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
        => On(m, path, _ => new Reply(status, body, contentType));

    public static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public static string? Param(string header, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(header, name + "=\"?([^\",]*)\"?");
        return m.Success ? m.Groups[1].Value : null;
    }

    private bool DigestOk(Seen s)
    {
        var h = s.Authorization;
        if (h is null || !h.StartsWith("Digest ")) return false;
        var uri = Param(h, "uri"); var nc = Param(h, "nc"); var cn = Param(h, "cnonce"); var resp = Param(h, "response");
        var ha1 = Md5($"{User}:{Realm}:{Password}");
        var ha2 = Md5($"{s.Method.Method}:{uri}");
        var nonce = CurrentNonce;
        return Param(h, "username") == User && Param(h, "nonce") == nonce && resp == Md5($"{ha1}:{nonce}:{nc}:{cn}:auth:{ha2}");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        ct.ThrowIfCancellationRequested();
        lock (_lock) { _inFlight++; MaxInFlight = Math.Max(MaxInFlight, _inFlight); }
        try
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            return await AnswerAsync(request, ct);
        }
        finally { lock (_lock) _inFlight--; }
    }

    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var bytes = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct);
        var seen = new Seen(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), Encoding.UTF8.GetString(bytes), bytes,
            request.Content?.Headers.TryGetValues("Content-Type", out var ctv) == true ? string.Join(", ", ctv) : null);
        lock (_lock) Requests.Add(seen);
        if (RequireDigest && seen.Authorization is not null && DropNextAnswers > 0 && DigestOk(seen))
        {
            DropNextAnswers--;
            ExpireNonce();
            return Bare401(StaleBody);
        }
        if (RequireDigest && !DigestOk(seen))
        {
            var sentNonce = seen.Authorization is null ? null : Param(seen.Authorization, "nonce");
            bool expired; lock (_lock) expired = sentNonce is not null && _expired.Contains(sentNonce);
            if (expired) return Bare401(StaleBody);
            if (DeviceStyleWrongPassword && seen.Authorization is not null) return Bare401(WrongPasswordBody);
            var r401 = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
            r401.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Digest realm=\"{Realm}\",nonce=\"{CurrentNonce}\",qop=\"auth\"");
            return r401;
        }
        if (!_routes.TryGetValue(request.Method.Method + " " + request.RequestUri.AbsolutePath, out var f))
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        var rep = f(seen);
        if (rep.Raw is not null)
        {
            var c = new ByteArrayContent(rep.Raw);
            c.Headers.TryAddWithoutValidation("Content-Type", rep.ContentType);
            return new HttpResponseMessage(rep.Status) { Content = c };
        }
        return new HttpResponseMessage(rep.Status) { Content = new StringContent(rep.Body, Encoding.UTF8, rep.ContentType) };
    }
}
