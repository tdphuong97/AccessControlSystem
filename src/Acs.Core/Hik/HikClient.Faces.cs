using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    public const int MaxFaceBytes = 200 * 1024;
    private const string CapturePath = "/ISAPI/AccessControl/CaptureFaceData";
    private const string FdSearchPath = "/ISAPI/Intelligent/FDLib/FDSearch?format=json";
    private const string CaptureFailedMessage = "Máy chưa chụp được. Mời đứng thẳng trước camera rồi bấm lại.";

    private static readonly string[] CaptureBodies =
    {
        "<CaptureFaceDataCond><dataType>binary</dataType></CaptureFaceDataCond>",
        "<CaptureFaceDataCond version=\"2.0\"></CaptureFaceDataCond>",
        "",
    };

    private static readonly Regex[] FaceDataPatterns =
    {
        new(@"<faceData>(.*?)</faceData>", RegexOptions.Singleline),
        new("\"faceData\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline),
        new(@"<picData>(.*?)</picData>", RegexOptions.Singleline),
    };

    private static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    public async Task<byte[]> CaptureFaceAsync(CancellationToken ct)
    {
        var attempts = new List<string>();
        var notSupported = 0;
        foreach (var xml in CaptureBodies)
        {
            try
            {
                var body = new StringContent(xml, Encoding.UTF8, "application/xml");
                var bytes = await SendRawAsync(HttpMethod.Post, CapturePath, body, CaptureTimeout, ct);
                var jpeg = ExtractJpeg(bytes);
                if (jpeg is not null) return jpeg;
                attempts.Add($"{bytes.Length}B không có ảnh: {Decode(bytes)[..Math.Min(120, Decode(bytes).Length)]}");
            }
            catch (HikNotSupportedException e) { notSupported++; attempts.Add(e.Detail ?? "notSupport"); }
            catch (HikRejectedException e) { attempts.Add(e.Detail ?? e.Code); }
        }
        if (notSupported == CaptureBodies.Length) throw new HikNotSupportedException("Máy này không hỗ trợ chụp khuôn mặt. Kiểm tra model máy hoặc cập nhật firmware.", HikErrorMap.Trim(string.Join(" || ", attempts)));
        throw new HikRejectedException("captureFailed", CaptureFailedMessage, HikErrorMap.Trim(string.Join(" || ", attempts)));
    }

    /// <summary>Raw JPEG, base64 in faceData/picData, or a JPEG embedded in a multipart body.</summary>
    private static byte[]? ExtractJpeg(byte[] bytes)
    {
        if (IsJpeg(bytes)) return bytes;
        var text = Decode(bytes);
        foreach (var re in FaceDataPatterns)
        {
            var m = re.Match(text);
            if (!m.Success) continue;
            var b64 = Regex.Replace(m.Groups[1].Value, @"\s+", "");
            if (b64.Length == 0) continue;
            try
            {
                var raw = Convert.FromBase64String(b64);
                if (IsJpeg(raw)) return raw;
            }
            catch (FormatException) { }
        }
        ReadOnlySpan<byte> magic = [0xFF, 0xD8, 0xFF];
        var idx = bytes.AsSpan().IndexOf(magic);
        if (idx < 0) return null;
        var tail = bytes.AsSpan(idx);
        var end = tail.LastIndexOf<byte>([0xFF, 0xD9]);
        return end >= 0 ? tail[..(end + 2)].ToArray() : tail.ToArray();
    }

    private static MultipartFormDataContent FaceMultipart(string employeeNo, byte[] jpeg)
    {
        var meta = JsonSerializer.Serialize(new { faceLibType = "blackFD", FDID = "1", FPID = employeeNo }, HikJson.Options);
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(meta, Encoding.UTF8, "application/json"), "FaceDataRecord");
        var img = new ByteArrayContent(jpeg);
        img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "img", "face.jpg");
        return form;
    }

    public async Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct)
    {
        if (!IsJpeg(jpeg) || jpeg.Length > MaxFaceBytes)
            throw new HikRejectedException("faceImageInvalid",
                "Ảnh khuôn mặt không hợp lệ (phải là JPEG, tối đa 200 KB). Chụp hoặc chọn lại ảnh khác.",
                $"length={jpeg.Length}");
        try
        {
            using var form = FaceMultipart(employeeNo, jpeg);
            await SendAsync(HttpMethod.Put, "/ISAPI/Intelligent/FDLib/FDSetUp?format=json", form, CaptureTimeout, ct);
            return;
        }
        catch (HikNotSupportedException) { }

        // Older firmware: no FDSetUp. Delete any existing face (it may not exist), then add as a new record.
        try { await DeleteFaceAsync(employeeNo, ct); }
        catch (Exception e) when (e is HikRejectedException or HikNotSupportedException) { }
        using var form2 = FaceMultipart(employeeNo, jpeg);
        await SendAsync(HttpMethod.Post, "/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json", form2, CaptureTimeout, ct);
    }

    /// <summary>FDSearch for one FPID. Returns the MatchList item whose FPID equals employeeNo exactly (the device may filter loosely), or null.</summary>
    private async Task<JsonElement?> FaceSearchAsync(string employeeNo, CancellationToken ct)
    {
        var text = await SendUtf8JsonAsync(HttpMethod.Post, FdSearchPath, new
        {
            searchResultPosition = 0, maxResults = 30, faceLibType = "blackFD", FDID = "1", FPID = employeeNo,
        }, ct);
        var t = StripBom(text);
        if (!t.StartsWith('{')) return null;
        using var doc = JsonDocument.Parse(t);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("MatchList", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var m in list.EnumerateArray())
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("FPID", out var f)
                    && string.Equals(f.ToString(), employeeNo, StringComparison.Ordinal))
                    return m.Clone();
        return null;
    }

    public async Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct)
        => await FaceSearchAsync(employeeNo, ct) is not null;

    public async Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct)
    {
        string? url = null;
        var r = await FaceSearchAsync(employeeNo, ct);
        if (r is { } m && m.TryGetProperty("faceURL", out var f) && !string.IsNullOrWhiteSpace(f.ToString())) url = f.ToString();

        if (url is null)
        {
            // Some firmware keeps the photo link on the user record instead.
            try
            {
                var text = await SendUtf8JsonAsync(HttpMethod.Post, UserSearchPath, new
                {
                    UserInfoSearchCond = new { searchID = "acs", searchResultPosition = 0, maxResults = 1, EmployeeNoList = new[] { new { employeeNo } } },
                }, ct);
                using var doc = JsonDocument.Parse(StripBom(text));
                // Only the entry for exactly this employeeNo: loosely-filtering firmware may list other users
                // first, and taking their photo would store (and later send) the wrong person's face.
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("UserInfoSearch", out var s) && s.ValueKind == JsonValueKind.Object
                    && s.TryGetProperty("UserInfo", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var u in arr.EnumerateArray())
                    {
                        if (u.ValueKind != JsonValueKind.Object || !u.TryGetProperty("employeeNo", out var no)
                            || !string.Equals(no.ToString(), employeeNo, StringComparison.Ordinal)) continue;
                        foreach (var name in new[] { "faceURL", "picURL" })
                            if (url is null && u.TryGetProperty(name, out var v) && !string.IsNullOrWhiteSpace(v.ToString())) url = v.ToString();
                    }
            }
            catch (Exception ex) when (ex is HikRejectedException or HikNotSupportedException or JsonException) { }
        }
        if (url is null) return null;

        // Credentials must only ever go to the configured device: keep path+query, reject scheme-relative URLs,
        // and verify the final URI host before sending.
        var path = SafeDevicePath(url);
        if (path is null) return null;
        var bytes = await SendRawAsync(HttpMethod.Get, path, null, null, ct);
        return bytes.Length == 0 ? null : bytes;
    }

    public async Task DeleteFaceAsync(string employeeNo, CancellationToken ct)
        => await SendUtf8JsonAsync(HttpMethod.Put, "/ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=1&faceLibType=blackFD",
            new { FPID = new[] { new { value = employeeNo } } }, ct);

    private string? SafeDevicePath(string url)
    {
        url = url.Trim();
        if (url.StartsWith("//") || url.StartsWith("\\") || url.StartsWith("/\\")) return null;
        string path;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) return null;
            path = abs.PathAndQuery;
        }
        else path = url.StartsWith('/') ? url : "/" + url;
        if (path.StartsWith("//")) return null;
        var baseUri = _http.BaseAddress!;
        if (!Uri.TryCreate(baseUri, path, out var final)
            || !string.Equals(final.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || final.Port != baseUri.Port) return null;
        return path;
    }
}
