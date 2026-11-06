using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Acs.Core.Hik;

// Hikvision's names are reversed: FingerPrintUpload READS from the device, FingerPrintDownload WRITES to it.
public sealed partial class HikClient
{
    private const string FpBase = "/ISAPI/AccessControl";
    private const int FpPageSize = 30;
    private const int FpMaxPages = 20;
    private const string FpCaptureFailedMessage = "Máy chưa quét được vân tay. Đặt ngón tay lên cảm biến, giữ yên rồi bấm lại.";
    private const string FpUnconfirmedMessage = "Máy chưa xác nhận đã lưu vân tay. Bấm Thử lại; nếu vẫn không được, kiểm tra máy rồi gửi lại.";
    public const string FpUnreadableMessage = "Máy trả về danh sách vân tay không đọc được. Bấm Thử lại; nếu vẫn lỗi, xem Chi tiết và báo lại cho người làm phần mềm.";

    /// <summary>Wait between FingerPrintProgress / DeleteProcess polls. Tests set it small.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to wait for the device to confirm a write or delete. Tests set it small.</summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly Regex FingerDataXml = new(@"<fingerData>(.*?)</fingerData>", RegexOptions.Singleline);
    private static readonly Regex FingerQualityXml = new(@"<fingerPrintQuality>\s*(\d+)\s*</fingerPrintQuality>");
    private static readonly Regex StatusCodeOk = new("\"statusCode\"\\s*:\\s*\"?1\"?\\s*[,}]|<statusCode>\\s*1\\s*</statusCode>");

    // ---- capture ----

    public async Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct)
    {
        var attempts = new List<string>();
        var notSupported = 0;
        for (var i = 0; i < 2; i++)
        {
            try
            {
                var text = i == 0
                    ? await SendAsync(HttpMethod.Post, $"{FpBase}/CaptureFingerPrint?format=json",
                        JsonBody(new { CaptureFingerPrintCond = new { fingerNo = fingerId } }), CaptureTimeout, ct)
                    : await SendAsync(HttpMethod.Post, $"{FpBase}/CaptureFingerPrint",
                        new StringContent($"<CaptureFingerPrintCond><fingerNo>{fingerId}</fingerNo></CaptureFingerPrintCond>", Encoding.UTF8, "application/xml"),
                        CaptureTimeout, ct);
                var got = ParseCapturedFinger(text);
                if (got is not null) return got;
                attempts.Add("không có fingerData: " + HikErrorMap.Trim(text));
            }
            catch (HikNotSupportedException e) { notSupported++; attempts.Add(e.Detail ?? "notSupport"); }
            catch (HikRejectedException e) { attempts.Add(e.Detail ?? e.Code); }
        }
        var detail = HikErrorMap.Trim(string.Join(" || ", attempts));
        if (notSupported == 2)
            throw new HikNotSupportedException("Máy này không hỗ trợ quét vân tay. Kiểm tra model máy hoặc cập nhật firmware.", detail);
        throw new HikRejectedException("captureFailed", FpCaptureFailedMessage, detail);
    }

    private static CapturedFinger? ParseCapturedFinger(string text)
    {
        var t = StripBom(text);
        if (t.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var info = root.TryGetProperty("CaptureFingerPrint", out var c) && c.ValueKind == JsonValueKind.Object ? c : root;
                    var data = Pick(info, "fingerData", "data");
                    if (!string.IsNullOrWhiteSpace(data))
                        return new CapturedFinger(Regex.Replace(data, @"\s+", ""), PickInt(info, "fingerPrintQuality", "quality"));
                }
            }
            catch (JsonException) { }
        }
        var m = FingerDataXml.Match(t);
        if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
        {
            var q = FingerQualityXml.Match(t);
            return new CapturedFinger(Regex.Replace(m.Groups[1].Value, @"\s+", ""), q.Success ? int.Parse(q.Groups[1].Value) : 0);
        }
        return null;
    }

    private static string? Pick(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined)
            {
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        return null;
    }

    private static int PickInt(JsonElement e, params string[] names)
        => int.TryParse(Pick(e, names), out var i) ? i : 0;

    // ---- write ----

    public async Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct)
    {
        var body = new
        {
            FingerPrintCfg = new
            {
                employeeNo,
                enableCardReader = new[] { readerNo },
                fingerPrintID = fingerId,
                fingerType = "normalFP",
                fingerData = templateBase64,
            },
        };

        try
        {
            var text = await SendAsync(HttpMethod.Put, $"{FpBase}/FingerPrint/SetUp?format=json", JsonBody(body), CaptureTimeout, ct);
            RequireConfirmed(text);
            return;
        }
        catch (HikNotSupportedException) { }

        // Older firmware: no SetUp. Download, then wait for the device to say it is done. A timeout is a FAILURE.
        await SendAsync(HttpMethod.Post, $"{FpBase}/FingerPrintDownload?format=json", JsonBody(body), CaptureTimeout, ct);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var text = await GetAsync($"{FpBase}/FingerPrintProgress?format=json", ct);
            using (var doc = TryParseJson(text))
            {
                if (doc is not null)
                {
                    var root = doc.RootElement;
                    var prog = root.TryGetProperty("FingerPrintStatus", out var p) && p.ValueKind == JsonValueKind.Object ? p : root;
                    if (prog.TryGetProperty("totalStatus", out var ts) && ts.ToString() == "1")
                    {
                        RequireConfirmed(text);
                        return;
                    }
                }
            }
            if (sw.Elapsed >= PollTimeout)
                throw new HikTransientException(FpUnconfirmedMessage, "FingerPrintProgress timeout: " + HikErrorMap.Trim(text));
            await Task.Delay(PollInterval, ct);
        }
    }

    private static JsonDocument? TryParseJson(string text)
    {
        var t = StripBom(text);
        if (!t.StartsWith('{')) return null;
        try
        {
            var d = JsonDocument.Parse(t);
            if (d.RootElement.ValueKind == JsonValueKind.Object) return d;
            d.Dispose();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Success only when the device positively confirmed: every cardReaderRecvStatus is 1 (or an explicit statusCode 1).</summary>
    private static void RequireConfirmed(string text)
    {
        using var doc = TryParseJson(text);
        if (doc is not null)
        {
            var root = doc.RootElement;
            JsonElement list = default;
            var found = (root.TryGetProperty("StatusList", out list) && list.ValueKind == JsonValueKind.Array)
                || (root.TryGetProperty("FingerPrintStatus", out var fs) && fs.ValueKind == JsonValueKind.Object
                    && fs.TryGetProperty("StatusList", out list) && list.ValueKind == JsonValueKind.Array);
            if (found && list.GetArrayLength() > 0)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var raw = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("cardReaderRecvStatus", out var s) ? s.ToString() : "";
                    if (raw != "1") throw FingerStatusError(raw, text);
                }
                return;
            }
        }
        if (StatusCodeOk.IsMatch(text)) return;
        throw new HikTransientException(FpUnconfirmedMessage, "no confirmation: " + HikErrorMap.Trim(text));
    }

    private static HikException FingerStatusError(string status, string text)
    {
        var detail = HikErrorMap.Trim(text);
        return status switch
        {
            "2" => new HikRejectedException("readerNotReady", "Máy chưa sẵn sàng nhận vân tay. Chờ một lát rồi bấm Thử lại.", detail),
            "3" => new HikRejectedException("fpMismatch", "Vân tay này không hợp với cảm biến của máy. Quét lại ngón này trực tiếp ở máy đó.", detail),
            "4" => new HikRejectedException("deviceFull", "Máy đã đầy bộ nhớ vân tay. Xoá bớt vân tay không còn dùng trên máy rồi bấm Thử lại.", detail),
            _ => new HikRejectedException("fpStatus" + status,
                $"Máy không nhận vân tay (mã {(status.Length == 0 ? "không rõ" : status)}). {HikErrorMap.UnknownHint}", detail),
        };
    }

    // ---- read ----

    public async Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct)
    {
        try { return await ReadFingerprintPagesAsync(employeeNo, nested: true, ct); }
        catch (HikRejectedException) { }
        // Older firmware wants the flat form.
        return await ReadFingerprintPagesAsync(employeeNo, nested: false, ct);
    }

    private async Task<IReadOnlyList<HikFinger>> ReadFingerprintPagesAsync(string employeeNo, bool nested, CancellationToken ct)
    {
        var result = new List<HikFinger>();
        var seen = new HashSet<int>();
        var pos = 0;
        for (var page = 0; page < FpMaxPages; page++)
        {
            object payload = nested
                ? new { FingerPrintCond = new { searchID = "acs", employeeNo, cardReaderNo = 1 } }
                : new { searchID = "acs", searchResultPosition = pos, maxResults = FpPageSize, employeeNo };
            var text = await SendAsync(HttpMethod.Post, $"{FpBase}/FingerPrintUpload?format=json", JsonBody(payload), null, ct);
            using var doc = TryParseJson(text);
            // Not JSON: the list is unknown, which must never read as "this person has no fingers".
            if (doc is null) throw new HikRejectedException("fpListUnreadable", FpUnreadableMessage, HikErrorMap.Trim(text));
            var root = doc.RootElement;
            var info = root.TryGetProperty("FingerPrintInfo", out var fi) && fi.ValueKind == JsonValueKind.Object ? fi : root;
            var status = Pick(info, "status", "responseStatusStrg");
            var added = 0;
            var count = 0;
            if (info.TryGetProperty("FingerPrintList", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var f in list.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object) continue;
                    count++;
                    // Defensive: a loosely-filtering device may list another person's fingers.
                    if (Pick(f, "employeeNo") is { } owner && !string.Equals(owner, employeeNo, StringComparison.Ordinal)) continue;
                    var data = Pick(f, "fingerData");
                    var id = PickInt(f, "fingerPrintID", "fingerNo");
                    if (data is null || id <= 0) continue;
                    if (seen.Add(id)) { result.Add(new HikFinger(id, Regex.Replace(data, @"\s+", ""))); added++; }
                }
            if (string.Equals(status, "NoFP", StringComparison.OrdinalIgnoreCase) || count == 0 || added == 0) break;
            pos += count;
        }
        return result;
    }

    // ---- delete ----

    public async Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct)
    {
        await SendJsonAsync(HttpMethod.Put, $"{FpBase}/FingerPrint/Delete?format=json", new
        {
            FingerPrintDelete = new { mode = "byEmployeeNo", EmployeeNoDetail = new { employeeNo, fingerPrintID = new[] { fingerId } } },
        }, ct);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var text = await GetAsync($"{FpBase}/FingerPrint/DeleteProcess?format=json", ct);
            using (var doc = TryParseJson(text))
            {
                if (doc is not null)
                {
                    var root = doc.RootElement;
                    var info = root.TryGetProperty("FingerPrintDeleteProcess", out var p) && p.ValueKind == JsonValueKind.Object ? p : root;
                    var status = Pick(info, "status", "processStatus");
                    if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) return;
                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                        throw new HikRejectedException("fpDeleteFailed", "Máy không xoá được vân tay. Bấm Thử lại; nếu vẫn lỗi, xoá trực tiếp trên máy.", HikErrorMap.Trim(text));
                }
            }
            if (sw.Elapsed >= PollTimeout)
                throw new HikTransientException("Máy chưa xác nhận đã xoá vân tay. Bấm Thử lại.", "DeleteProcess timeout: " + HikErrorMap.Trim(text));
            await Task.Delay(PollInterval, ct);
        }
    }
}
