using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Acs.Core.Tests.Hik;

/// <summary>In-process fake of a Hikvision ISAPI endpoint. Routes by method + path (query ignored); can require Digest.</summary>
public sealed class FakeIsapiHandler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, string PathAndQuery, string? Authorization, string Body, byte[]? BodyBytes = null);
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
        return Param(h, "username") == User && Param(h, "nonce") == Nonce && resp == Md5($"{ha1}:{Nonce}:{nc}:{cn}:auth:{ha2}");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        ct.ThrowIfCancellationRequested();
        var bytes = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct);
        var seen = new Seen(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), Encoding.UTF8.GetString(bytes), bytes);
        Requests.Add(seen);
        if (RequireDigest && !DigestOk(seen))
        {
            var r401 = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") };
            r401.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Digest realm=\"{Realm}\",nonce=\"{Nonce}\",qop=\"auth\"");
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
