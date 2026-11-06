using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Acs.Core.Hik;

/// <summary>
/// RFC 2617 Digest (MD5, qop=auth). The nonce from the first challenge is cached and reused with an increasing nc;
/// a 401 on a request that used the cached nonce (stale) is answered once with the fresh challenge. A 401 on the
/// answered request is returned to the caller as-is (HikClient turns it into HikAuthException), so there is never a loop.
/// After that final 401 the cached nonce is dropped: the next call then starts without credentials (a plain
/// challenge, not a failed login) and costs at most one failed login instead of two, so a wrong password does not
/// walk the device towards its IP lock-out twice as fast.
/// </summary>
public sealed class DigestHandler : DelegatingHandler
{
    private readonly string _user, _password;
    private readonly object _lock = new();
    private string? _realm, _nonce, _opaque;
    private int _nc;

    public DigestHandler(string user, string password, HttpMessageHandler inner) : base(inner)
    {
        _user = user; _password = password;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        byte[]? body = null;
        if (request.Content is not null) body = await request.Content.ReadAsByteArrayAsync(ct);

        string? cached = Authorize(request);
        var first = Clone(request, body, cached);
        var resp = await base.SendAsync(first, ct);
        if (resp.StatusCode != HttpStatusCode.Unauthorized) return resp;

        var challenge = resp.Headers.WwwAuthenticate.FirstOrDefault(h => h.Scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase));
        if (challenge?.Parameter is null) { Forget(); return resp; }
        lock (_lock)
        {
            _realm = Field(challenge.Parameter, "realm") ?? "";
            _nonce = Field(challenge.Parameter, "nonce") ?? "";
            _opaque = Field(challenge.Parameter, "opaque");
            _nc = 0;
        }
        resp.Dispose();
        var answered = await base.SendAsync(Clone(request, body, Authorize(request)), ct);
        if (answered.StatusCode == HttpStatusCode.Unauthorized) Forget();
        return answered;
    }

    private void Forget()
    {
        lock (_lock) { _realm = null; _nonce = null; _opaque = null; _nc = 0; }
    }

    private string? Authorize(HttpRequestMessage req)
    {
        lock (_lock)
        {
            if (_nonce is null) return null;
            var nc = (++_nc).ToString("x8");
            var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var uri = req.RequestUri!.PathAndQuery;
            var ha1 = Md5($"{_user}:{_realm}:{_password}");
            var ha2 = Md5($"{req.Method.Method}:{uri}");
            var response = Md5($"{ha1}:{_nonce}:{nc}:{cnonce}:auth:{ha2}");
            var sb = new StringBuilder();
            sb.Append($"username=\"{_user}\", realm=\"{_realm}\", nonce=\"{_nonce}\", uri=\"{uri}\", algorithm=MD5, ");
            sb.Append($"qop=auth, nc={nc}, cnonce=\"{cnonce}\", response=\"{response}\"");
            if (!string.IsNullOrEmpty(_opaque)) sb.Append($", opaque=\"{_opaque}\"");
            return sb.ToString();
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage src, byte[]? body, string? digest)
    {
        var c = new HttpRequestMessage(src.Method, src.RequestUri) { Version = src.Version };
        foreach (var h in src.Headers) c.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (body is not null)
        {
            c.Content = new ByteArrayContent(body);
            foreach (var h in src.Content!.Headers) c.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        if (digest is not null) c.Headers.Authorization = new AuthenticationHeaderValue("Digest", digest);
        return c;
    }

    private static string? Field(string s, string name)
    {
        var m = Regex.Match(s, "(?:^|[\\s,])" + name + "=(?:\"([^\"]*)\"|([^,\\s]*))", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    }

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
