using System.Net;
using System.Text;
using System.Text.Json;

namespace Acs.Core.Hik;

/// <summary>Real Hikvision ISAPI client. Partial: users, faces and fingerprints live in HikClient.*.cs.</summary>
public sealed partial class HikClient : IHikDevice, IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);   // never disposed: no wait handle is ever created

    public HikClient(string host, string user, string password, HttpMessageHandler? inner = null, TimeProvider? time = null)
    {
        // The devices sit on the local network: never route their traffic (and the digest login) through a system proxy.
        inner ??= new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        _http = new HttpClient(new DigestHandler(user, password, inner, time), disposeHandler: true)
        {
            BaseAddress = new Uri(host.Contains("://") ? host.TrimEnd('/') : "http://" + host.TrimEnd('/')),
            Timeout = Timeout.InfiniteTimeSpan,   // per-call timeouts are applied in SendRawAsync
        };
    }

    /// <summary>UTF-8 decode that drops a leading BOM (some firmwares send one).</summary>
    internal static string Decode(byte[] b)
        => (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? Encoding.UTF8.GetString(b, 3, b.Length - 3) : Encoding.UTF8.GetString(b));

    /// <summary>Drops a leading BOM and whitespace so { and < sniffing works.</summary>
    internal static string StripBom(string s) => s.TrimStart((char)0xFEFF).TrimStart();

    public void Dispose() => _http.Dispose();

    /// <summary>Sends a request and returns the body as text. Every response goes through HikErrorMap.</summary>
    internal async Task<string> SendAsync(HttpMethod method, string path, HttpContent? body, TimeSpan? timeout, CancellationToken ct)
        => Decode(await SendRawAsync(method, path, body, timeout, ct));

    internal Task<string> GetAsync(string path, CancellationToken ct, TimeSpan? timeout = null)
        => SendAsync(HttpMethod.Get, path, null, timeout, ct);

    internal Task<string> SendJsonAsync(HttpMethod method, string path, object payload, CancellationToken ct, TimeSpan? timeout = null)
        => SendAsync(method, path, JsonBody(payload), timeout, ct);

    internal static StringContent JsonBody(object payload)
        => new(JsonSerializer.Serialize(payload, HikJson.Options), Encoding.UTF8, "application/json");

    /// <summary>
    /// Core call. Network errors and timeouts become HikTransientException; a cancelled caller token propagates as
    /// OperationCanceledException; 401 (after the Digest handler's single re-challenge) becomes HikAuthException, never
    /// retried; other failures (including HTTP 200 with statusCode != 1) are mapped by HikErrorMap.
    /// </summary>
    internal async Task<byte[]> SendRawAsync(HttpMethod method, string path, HttpContent? body, TimeSpan? timeout, CancellationToken ct)
    {
        // One call at a time per device: on site, logins in flight together made the device refuse one of them (401
        // invalidOperation), which read as a wrong password and paused the device. The wait for the gate is not part
        // of the call's timeout, so a ping queued behind a 60 s capture does not report the device as offline.
        await _gate.WaitAsync(ct);
        try { return await SendOneAsync(method, path, body, timeout, ct); }
        finally { _gate.Release(); }
    }

    /// <summary>For a caller that already holds the gate: a multi-page read that nothing may land in between.</summary>
    private async Task<string> SendHeldAsync(HttpMethod method, string path, HttpContent? body, CancellationToken ct)
        => Decode(await SendOneAsync(method, path, body, null, ct));

    private async Task<byte[]> SendOneAsync(HttpMethod method, string path, HttpContent? body, TimeSpan? timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        using var req = new HttpRequestMessage(method, path) { Content = body };
        HttpResponseMessage resp;
        byte[] bytes;
        try
        {
            resp = await _http.SendAsync(req, cts.Token);
            using (resp) bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HikTransientException("Máy không trả lời kịp. Kiểm tra máy đã bật và cùng mạng, app sẽ tự thử lại.", "timeout");
        }
        catch (HttpRequestException e)
        {
            throw new HikTransientException("Không kết nối được máy. Kiểm tra dây mạng, địa chỉ IP và nguồn của máy, app sẽ tự thử lại.", HikErrorMap.Trim(e.Message));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The connection dropped while the body was being read (HttpIOException is an IOException), or this client
            // was replaced after a login change while a call was still running: both are worth a retry.
            throw new HikTransientException("Mất kết nối với máy giữa chừng. Kiểm tra dây mạng và nguồn của máy, app sẽ tự thử lại.", HikErrorMap.Trim(e.Message));
        }

        var code = (int)resp.StatusCode;
        var looksText = bytes.Length > 0 && (bytes[0] == 0xEF || bytes[0] == (byte)'{' || bytes[0] == (byte)'<' || bytes[0] <= (byte)' ');
        if (code >= 400 || looksText)
        {
            var err = HikErrorMap.FromResponse(code, Decode(bytes));
            if (err is not null) throw err;
        }
        return bytes;
    }
}
