using Acs.Core.Hik;
using Acs.Core.Model;

namespace Acs.Core.Sync;

/// <summary>Pure rules shared by the job queue and the worker.</summary>
public static class JobRules
{
    public const int VerifyReads = 3;
    public static readonly TimeSpan VerifyGap = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(30);
    public const string NotSavedMessage = "Máy báo đã nhận nhưng đọc lại không thấy. Bấm Thử lại; nếu vẫn lỗi, xem Chi tiết.";
    public const string AuthMessage = "Sai mật khẩu hoặc máy đang khoá tạm (khoảng 30 phút). Kiểm tra mật khẩu trong Thiết lập.";
    public const string OfflineMessage = "Đang chờ — tự gửi khi máy có mạng.";
    public const string NoEndDate = "2035-12-31T23:59:59";

    /// <summary>0 delete user, 1 upsert/disable user, 2 face, 3 fingers.</summary>
    public static int Rank(JobKind k) => k switch
    {
        JobKind.DeleteUser => 0,
        JobKind.UpsertUser or JobKind.DisableUser => 1,
        JobKind.SetFace or JobKind.DeleteFace => 2,
        _ => 3,
    };

    public static HikUserWrite ToWrite(PersonRecord p, int doorNo, bool enabled) => new(
        p.EmployeeNo, p.FullName,
        p.ValidFrom.ToString("yyyy-MM-dd") + "T00:00:00",
        p.ValidTo is { } to ? to.ToString("yyyy-MM-dd") + "T23:59:59" : NoEndDate,
        enabled, doorNo);

    private static readonly string[] Names = ["cái", "trỏ", "giữa", "áp út", "út"];

    /// <summary>Finger ids 1-5 right hand (thumb to little), 6-10 left hand.</summary>
    public static string FingerLabel(int id) =>
        id is >= 1 and <= 10 ? $"ngón {Names[(id - 1) % 5]} {(id <= 5 ? "phải" : "trái")}" : $"ngón số {id}";
}
