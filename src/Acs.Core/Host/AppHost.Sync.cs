using System.Text.Json;
using Acs.Core.Enroll;
using Acs.Core.Model;
using Acs.Core.Sync;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    private const string FailedMessage = "Có việc gửi sang máy bị lỗi. Mở Đồng bộ và bấm Thử lại.";

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

    private static SyncItem ItemOf(string label, JobRecord j) => j.Status == JobStatus.Failed
        ? new SyncItem(label, "error", j.ErrorMessage, j.ErrorDetail)
        : new SyncItem(label, "pending", j.Attempts > 0 ? j.ErrorMessage : null, j.Attempts > 0 ? j.ErrorDetail : null);

    private List<DeviceSync> SyncPerson(string no)
    {
        var jobs = Db.Jobs.ListOpen().Where(j => j.EmployeeNo == no).ToList();
        var hasFace = Db.Biometrics.GetFace(no) is not null;
        var fingers = Db.Biometrics.GetFingers(no).Select(f => f.FingerId).Distinct().OrderBy(x => x).ToList();
        var result = new List<DeviceSync>();
        foreach (var d in Db.Devices.List())
        {
            var mine = jobs.Where(j => j.DeviceId == d.Id).ToList();
            var state = Db.Jobs.GetState(no, d.Id);
            var items = new List<SyncItem>();
            var used = new HashSet<long>();

            void Add(string label, Func<JobRecord, bool> match, Func<SyncItem> whenClean)
            {
                var found = mine.Where(match).ToList();
                foreach (var j in found) used.Add(j.Id);
                // a failed job is the most useful thing to show
                var j0 = found.OrderByDescending(j => j.Status == JobStatus.Failed).FirstOrDefault();
                items.Add(j0 is null ? whenClean() : ItemOf(label, j0));
            }

            Add("Thông tin", j => j.Kind is JobKind.UpsertUser or JobKind.DisableUser or JobKind.DeleteUser,
                () => new SyncItem("Thông tin", "ok", null, null));
            if (hasFace || mine.Any(j => j.Kind == JobKind.SetFace))
                Add("Khuôn mặt", j => j.Kind == JobKind.SetFace, () => new SyncItem("Khuôn mặt", "ok", null, null));
            foreach (var f in fingers)
            {
                var label = "Vân tay " + FingerLabel(f);
                Add(label, j => j.Kind == JobKind.SetFinger && j.FingerId == f, () =>
                    state?.NeedsScan.Contains(f) == true
                        ? new SyncItem(label, "error", $"Cần quét {FingerLabel(f)} trực tiếp ở {d.Name}.", null)
                        : new SyncItem(label, "ok", null, null));
            }
            foreach (var j in mine.Where(j => !used.Contains(j.Id))) items.Add(ItemOf(JobLabel(j), j));

            var agg = items.Any(i => i.State == "error") ? "error" : items.Any(i => i.State == "pending") ? "pending" : "ok";
            result.Add(new DeviceSync(d.Id, d.Name, agg, items));
        }
        return result;
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
