using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    private const int PageSize = 30;
    private const string UserSearchPath = "/ISAPI/AccessControl/UserInfo/Search?format=json";
    public const string NoEndTime = "2035-12-31T23:59:59";
    private const string NoBeginTime = "2000-01-01T00:00:00";

    /// <summary>Pause between UserInfo/Search pages (the device locks the IP if hammered). Tests set it to zero.</summary>
    public TimeSpan PagePause { get; set; } = TimeSpan.FromMilliseconds(150);

    private Task<string> SendUtf8JsonAsync(HttpMethod method, string path, object payload, CancellationToken ct)
        => SendAsync(method, path, new StringContent(JsonSerializer.Serialize(payload, HikJson.Options), Encoding.UTF8, "application/json"), null, ct);

    public async IAsyncEnumerable<HikUser> ListUsersAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var pos = 0;
        while (true)
        {
            var (users, status, total) = await SearchPageAsync(pos, null, ct);
            foreach (var u in users) yield return u;
            pos += users.Count;
            if (users.Count == 0 || status != "MORE" || (total > 0 && pos >= total)) yield break;
            if (PagePause > TimeSpan.Zero) await Task.Delay(PagePause, ct);
        }
    }

    public async Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct)
    {
        var (users, _, _) = await SearchPageAsync(0, employeeNo, ct);
        return users.FirstOrDefault(u => u.EmployeeNo == employeeNo) ?? users.FirstOrDefault();
    }

    private async Task<(List<HikUser> Users, string? Status, int Total)> SearchPageAsync(int pos, string? employeeNo, CancellationToken ct)
    {
        var cond = new Dictionary<string, object>
        {
            ["searchID"] = "acs", ["searchResultPosition"] = pos, ["maxResults"] = PageSize,
        };
        if (employeeNo is not null) cond["EmployeeNoList"] = new[] { new { employeeNo } };
        var text = await SendUtf8JsonAsync(HttpMethod.Post, UserSearchPath, new { UserInfoSearchCond = cond }, ct);

        var list = new List<HikUser>();
        using var doc = JsonDocument.Parse(StripBom(text));
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("UserInfoSearch", out var s))
            return (list, null, 0);
        if (s.TryGetProperty("UserInfo", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var u in arr.EnumerateArray()) list.Add(ParseUser(u));
        var status = s.TryGetProperty("responseStatusStrg", out var st) ? st.ToString() : null;
        var total = s.TryGetProperty("totalMatches", out var tm) && tm.ValueKind == JsonValueKind.Number ? tm.GetInt32() : 0;
        return (list, status, total);
    }

    private static HikUser ParseUser(JsonElement u)
    {
        static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v.ToString() : "";
        static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v)
            ? (v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.TryParse(v.ToString(), out var i) ? i : 0) : 0;
        string begin = "", end = "";
        var enabled = true;
        if (u.TryGetProperty("Valid", out var valid) && valid.ValueKind == JsonValueKind.Object)
        {
            begin = Str(valid, "beginTime"); end = Str(valid, "endTime");
            if (valid.TryGetProperty("enable", out var en))
                enabled = en.ValueKind == JsonValueKind.False ? false
                    : en.ValueKind == JsonValueKind.String ? !string.Equals(en.GetString(), "false", StringComparison.OrdinalIgnoreCase) : true;
        }
        return new HikUser(Str(u, "employeeNo"), Str(u, "name"), begin, end, enabled, Int(u, "numOfFace"), Int(u, "numOfFP"));
    }

    /// <summary>Accepts "yyyy-MM-dd" (adds the time of day), a full timestamp, or empty (default; end = 2035-12-31T23:59:59).</summary>
    private static string NormalizeTime(string? t, bool isEnd)
    {
        if (string.IsNullOrWhiteSpace(t)) return isEnd ? NoEndTime : NoBeginTime;
        t = t.Trim();
        return t.Length == 10 ? t + (isEnd ? "T23:59:59" : "T00:00:00") : t;
    }

    public async Task UpsertUserAsync(HikUserWrite user, CancellationToken ct)
    {
        var body = new
        {
            UserInfo = new
            {
                employeeNo = user.EmployeeNo,
                name = user.Name,
                userType = "normal",
                Valid = new
                {
                    enable = user.Enabled,
                    beginTime = NormalizeTime(user.BeginTime, false),
                    endTime = NormalizeTime(user.EndTime, true),
                    timeType = "local",
                },
                doorRight = user.DoorNo.ToString(),
                RightPlan = new[] { new { doorNo = user.DoorNo, planTemplateNo = "1" } },
            },
        };
        try
        {
            await SendUtf8JsonAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/SetUp?format=json", body, ct);
            return;
        }
        catch (HikNotSupportedException) { }

        try
        {
            await SendUtf8JsonAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Modify?format=json", body, ct);
            return;
        }
        catch (HikRejectedException e) when (IsNotExist(e)) { }

        await SendUtf8JsonAsync(HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Record?format=json", body, ct);
    }

    /// <summary>"User does not exist": subStatusCode employeeNoNotExist / userNotExist, or "not exist" text in code or detail.</summary>
    private static bool IsNotExist(HikRejectedException e)
    {
        static bool Has(string? s) => s is not null
            && (s.Contains("NotExist", StringComparison.OrdinalIgnoreCase) || s.Contains("not exist", StringComparison.OrdinalIgnoreCase));
        return Has(e.Code) || Has(e.Detail);
    }

    public async Task DeleteUserAsync(string employeeNo, CancellationToken ct)
        => await SendUtf8JsonAsync(HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Delete?format=json",
            new { UserInfoDelCond = new { EmployeeNoList = new[] { new { employeeNo } } } }, ct);
}
