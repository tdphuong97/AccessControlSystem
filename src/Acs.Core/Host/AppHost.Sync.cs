using System.Text.Json;
using Acs.Core.Enroll;
using Acs.Core.Model;
using Acs.Core.Sync;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    private const string FailedMessage = "Có việc gửi sang máy bị lỗi. Ở tab \"Người dùng\", mở người có dấu \"Lỗi gửi\" để xem cách xử lý và bấm Thử lại.";

    private static string KindLabel(JobKind kind, int? finger)
    {
        var f = finger is > 0 ? " " + FingerLabel(finger.Value) : "";
        return kind switch
        {
            JobKind.UpsertUser or JobKind.DisableUser => "Thông tin",
            JobKind.DeleteUser => "Xoá khỏi máy",
            JobKind.SetFace => "Khuôn mặt",
            JobKind.DeleteFace => "Xoá khuôn mặt",
            JobKind.SetFinger => "Vân tay" + f,
            _ => "Xoá vân tay" + f,
        };
    }

    private static string JobLabel(JobRecord j) => KindLabel(j.Kind, j.FingerId);

    /// <summary>The worker logs job kinds and a few fixed codes; the log screen shows them in Vietnamese. Unknown codes stay as they are.</summary>
    private static string LogAction(string code) => code switch
    {
        "Ping" => "Kiểm tra kết nối",
        "Auth" => "Đăng nhập máy",
        "Worker" => "Luồng gửi dữ liệu",
        _ => Enum.TryParse<JobKind>(code, out var k) ? KindLabel(k, null) : code,
    };

    /// <summary>Shown when nothing is queued but the device never confirmed this part (first import, added device, restore).</summary>
    public const string NotOnDeviceMessage = "Chưa có trên máy này — chạy So khớp 2 máy.";
    public const string DisabledOnDeviceMessage = "Đang bị tắt trên máy này — chạy So khớp 2 máy.";
    public const string LockedUnknownMessage = "Chưa kiểm tra trên máy này — chạy So khớp 2 máy.";
    public const string LockedStillEnabledMessage = "Máy này vẫn cho người đã khoá ra vào — chạy So khớp 2 máy.";

    private static SyncItem ItemOf(string label, JobRecord j) => j.Status == JobStatus.Failed
        ? new SyncItem(label, "error", j.ErrorMessage, j.ErrorDetail)
        : new SyncItem(label, "pending", j.Attempts > 0 ? j.ErrorMessage : null, j.Attempts > 0 ? j.ErrorDetail : null);

    /// <summary>
    /// One person on one device. Open jobs show their own state; without a job a part is "ok" only when device_state
    /// (written after a device read-back) confirms it: an active person needs the user present and enabled, the face
    /// when the app has one, and every app finger. A locked person is "ok" when confirmed absent or disabled (R20).
    /// Anything unconfirmed is "pending" with a hint to run So khớp, never a silent ✓.
    /// </summary>
    internal static DeviceSync DeviceSyncOf(DeviceRecord d, PersonRecord p, bool hasFace, IReadOnlyList<int> fingers,
        DeviceStateRecord? state, IReadOnlyList<JobRecord> mine)
    {
        var items = new List<SyncItem>();
        var used = new HashSet<long>();
        var locked = p.Status == PersonStatus.Locked;
        static SyncItem Ok(string label) => new(label, "ok", null, null);
        static SyncItem Unconfirmed(string label) => new(label, "pending", NotOnDeviceMessage, null);

        void Add(string label, Func<JobRecord, bool> match, Func<SyncItem> whenClean)
        {
            var found = mine.Where(match).ToList();
            foreach (var j in found) used.Add(j.Id);
            // a failed job is the most useful thing to show
            var j0 = found.OrderByDescending(j => j.Status == JobStatus.Failed).FirstOrDefault();
            items.Add(j0 is null ? whenClean() : ItemOf(label, j0));
        }

        Add("Thông tin", j => j.Kind is JobKind.UpsertUser or JobKind.DisableUser or JobKind.DeleteUser, () =>
        {
            if (locked)
                return state is null ? new SyncItem("Thông tin", "pending", LockedUnknownMessage, null)
                    : state.UserPresent && state.UserEnabled ? new SyncItem("Thông tin", "pending", LockedStillEnabledMessage, null)
                    : Ok("Thông tin");
            if (state is { UserPresent: true, UserEnabled: true }) return Ok("Thông tin");
            return state is { UserPresent: true } ? new SyncItem("Thông tin", "pending", DisabledOnDeviceMessage, null) : Unconfirmed("Thông tin");
        });
        if (hasFace || mine.Any(j => j.Kind == JobKind.SetFace))
            Add("Khuôn mặt", j => j.Kind == JobKind.SetFace,
                () => locked || state?.FacePresent == true ? Ok("Khuôn mặt") : Unconfirmed("Khuôn mặt"));
        foreach (var f in fingers)
        {
            var label = "Vân tay " + FingerLabel(f);
            Add(label, j => j.Kind == JobKind.SetFinger && j.FingerId == f, () =>
                locked ? Ok(label)
                : state?.NeedsScan.Contains(f) == true ? new SyncItem(label, "error", $"Cần quét {FingerLabel(f)} trực tiếp ở {d.Name}.", null)
                : state?.Fingers.Contains(f) == true ? Ok(label)
                : Unconfirmed(label));
        }
        foreach (var j in mine.Where(j => !used.Contains(j.Id))) items.Add(ItemOf(JobLabel(j), j));

        var agg = items.Any(i => i.State == "error") ? "error" : items.Any(i => i.State == "pending") ? "pending" : "ok";
        return new DeviceSync(d.Id, d.Name, agg, items);
    }

    private List<DeviceSync> SyncPerson(string no)
    {
        var p = Db.People.Get(no);
        if (p is null) return [];
        var jobs = Db.Jobs.ListOpen().Where(j => j.EmployeeNo == no).ToList();
        var hasFace = Db.Biometrics.HasFace(no);
        var fingers = Db.Biometrics.FingerGroups(no).Select(f => f.FingerId).Distinct().OrderBy(x => x).ToList();
        return Db.Devices.List()
            .Select(d => DeviceSyncOf(d, p, hasFace, fingers, Db.Jobs.GetState(no, d.Id), jobs.Where(j => j.DeviceId == d.Id).ToList()))
            .ToList();
    }

    private object? SyncRetry(JsonElement a)
    {
        var id = OptInt(a, "deviceId");
        foreach (var d in Db.Devices.List().Where(d => id is null || d.Id == id).Where(d => d.AuthPaused))
            Db.Devices.SetAuthPaused(d.Id, false);
        Db.Jobs.ResetFailed(id, Now);
        Sync.Wake(id);
        Emit("devices.changed", DeviceViews());
        return null;
    }
}
