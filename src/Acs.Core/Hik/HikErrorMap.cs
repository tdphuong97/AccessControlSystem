using System.Text.Json;
using System.Xml.Linq;

namespace Acs.Core.Hik;

/// <summary>Turns an ISAPI response (HTTP status + body) into a typed exception, or null when it is a success.</summary>
public static class HikErrorMap
{
    public const string UnknownHint = "Xem Chi tiết và báo lại cho người làm phần mềm.";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["employeeNoAlreadyExist"] = "Mã nhân viên này đã có trên máy. Dùng mã khác hoặc kiểm tra lại người đó trên máy.",
        ["employeeNoNotExist"] = "Máy chưa có người này. Bấm Thử lại để gửi người trước rồi mới gửi sinh trắc học.",
        ["userNotExist"] = "Máy chưa có người này. Bấm Thử lại để gửi người trước rồi mới gửi sinh trắc học.",
        ["badJsonContent"] = "Máy không hiểu dữ liệu gửi đi. Xem Chi tiết và báo lại cho người làm phần mềm.",
        ["MessageParametersLack"] = "Dữ liệu gửi máy bị thiếu thông tin. Xem Chi tiết và báo lại cho người làm phần mềm.",
        ["maxUserNum"] = "Máy đã đầy số người tối đa. Xoá bớt người không còn dùng trên máy rồi thử lại.",
        ["faceLibFull"] = "Thư viện khuôn mặt trên máy đã đầy. Xoá bớt khuôn mặt không còn dùng rồi thử lại.",
        ["checkFingerPrintFailed"] = "Máy không nhận vân tay này. Quét lại vân tay cho rõ rồi thử lại.",
        ["faceImageQualityBad"] = "Ảnh khuôn mặt chưa đạt chất lượng. Chụp lại ảnh rõ mặt, đủ sáng, nhìn thẳng.",
        ["modelingFailed"] = "Máy không nhận ra khuôn mặt trong ảnh. Chụp lại ảnh rõ mặt, đủ sáng, nhìn thẳng.",
    };

    public static HikException? FromResponse(int http, string body)
    {
        body ??= "";
        var detail = Trim(body);
        if (http == 401)
            return new HikAuthException("Máy báo sai tài khoản hoặc mật khẩu, hoặc đã khoá tạm thời. Kiểm tra lại trong Cài đặt máy, hoặc chờ khoảng 30 phút rồi bấm Thử lại.", detail);

        Parse(body, out var status, out var sub, out var msg);

        if (http == 404 || string.Equals(sub, "notSupport", StringComparison.OrdinalIgnoreCase))
            return new HikNotSupportedException("Máy này không hỗ trợ chức năng đó. Kiểm tra model máy hoặc cập nhật firmware.", detail);
        if (http >= 500 || string.Equals(sub, "deviceBusy", StringComparison.OrdinalIgnoreCase))
            return new HikTransientException("Máy đang bận hoặc gặp lỗi tạm thời. Chờ một lúc, app sẽ tự thử lại.", detail);

        var failed = status is not null ? status != 1 : http >= 400;
        if (!failed) return null;

        var code = sub ?? status?.ToString() ?? http.ToString();
        if (sub is not null && Known.TryGetValue(sub, out var vi))
            return new HikRejectedException(code, vi, detail);
        return new HikRejectedException(code, $"Máy từ chối: {(string.IsNullOrWhiteSpace(msg) ? code : msg)}. {UnknownHint}", detail);
    }

    public static string Trim(string s) => s.Length <= 500 ? s : s[..500];

    private static void Parse(string body, out int? status, out string? sub, out string? msg)
    {
        status = null; sub = null; msg = null;
        var t = body.TrimStart();
        try
        {
            if (t.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return;
                if (r.TryGetProperty("statusCode", out var sc))
                    status = sc.ValueKind == JsonValueKind.Number ? sc.GetInt32()
                        : int.TryParse(sc.GetString(), out var n) ? n : null;
                if (r.TryGetProperty("subStatusCode", out var ss)) sub = ss.ToString();
                if (r.TryGetProperty("errorMsg", out var em)) msg = em.ToString();
                else if (r.TryGetProperty("statusString", out var st)) msg = st.ToString();
            }
            else if (t.StartsWith('<'))
            {
                var root = XDocument.Parse(body).Root;
                if (root is null || root.Name.LocalName != "ResponseStatus") return;
                string? Get(string n) => root.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value;
                status = int.TryParse(Get("statusCode"), out var n) ? n : null;
                sub = Get("subStatusCode");
                msg = Get("errorMsg") ?? Get("statusString");
            }
        }
        catch (Exception e) when (e is JsonException or System.Xml.XmlException or InvalidOperationException or FormatException) { }
    }
}
