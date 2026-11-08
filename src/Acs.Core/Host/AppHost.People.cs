using System.Globalization;
using System.Text;
using System.Text.Json;
using Acs.Core.Enroll;
using Acs.Core.Model;
using Acs.Core.Sync;

namespace Acs.Core.Host;

public sealed partial class AppHost
{
    private static readonly CompareInfo Vi = new CultureInfo("vi-VN").CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private static readonly string[] FingerNames = ["Cái", "Trỏ", "Giữa", "Áp út", "Út"];

    /// <summary>Finger ids 1-5 are the right hand thumb to little, 6-10 the left hand.</summary>
    public static string FingerLabel(int id) =>
        id is >= 1 and <= 10 ? $"{FingerNames[(id - 1) % 5]} {(id <= 5 ? "phải" : "trái")}" : $"Ngón số {id}";

    // vi-VN collation treats ê, ă, â, ơ, ư, đ as letters of their own, so IgnoreNonSpace alone would not match "nguyen"
    // to "Nguyễn". Fold to base letters first (decompose, drop combining marks, đ to d), then compare with vi-VN.
    private static string Fold(string s)
    {
        var d = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private static string Initials(string name)
    {
        var w = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return "?";
        if (w.Length == 1) return w[0][..Math.Min(2, w[0].Length)].ToUpperInvariant();
        return (w[0][..1] + w[^1][..1]).ToUpperInvariant();
    }

    private static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        throw new HostException("Ngày hết hạn không đúng. Hãy chọn lại ngày.");
    }

    private object PeopleList(JsonElement a)
    {
        var filter = OptStr(a, "filter") ?? "all";
        var query = (OptStr(a, "query") ?? "").Trim();
        var people = Db.People.List();
        if (query.Length > 0)
        {
            var q = Fold(query);
            people = people.Where(p => p.EmployeeNo.Contains(query, StringComparison.OrdinalIgnoreCase)
                || Vi.IndexOf(Fold(p.FullName), q, Loose) >= 0).ToList();
        }
        // Bulk reads only: one query each for jobs, device states, faces and finger ids (no per-person queries).
        var open = Db.Jobs.ListOpen().ToLookup(j => j.EmployeeNo);
        var states = Db.Jobs.ListStates().ToDictionary(st => (st.EmployeeNo, st.DeviceId));
        var faces = Db.Biometrics.FaceOwners();
        var fingerIds = Db.Biometrics.FingerIds();
        var devices = Db.Devices.List();
        var groups = Db.People.GroupNames();
        var now = Now;
        var rows = new List<PersonRow>();
        foreach (var p in people.OrderBy(p => p.FullName, Comparer<string>.Create((x, y) => Vi.Compare(x, y, CompareOptions.IgnoreCase)))
                     .ThenBy(p => p.EmployeeNo, StringComparer.Ordinal))
        {
            var mine = open[p.EmployeeNo].ToList();
            var hasFace = faces.Contains(p.EmployeeNo);
            var ids = fingerIds.GetValueOrDefault(p.EmployeeNo) ?? [];
            var perDevice = devices.Select(d => DeviceSyncOf(d, p, hasFace, ids, states.GetValueOrDefault((p.EmployeeNo, d.Id)),
                mine.Where(j => j.DeviceId == d.Id).ToList())).ToList();
            var sync = perDevice.Any(x => x.State == "error") ? "error" : perDevice.Any(x => x.State == "pending") ? "pending" : "ok";
            var waiting = perDevice.Where(x => x.State != "ok").Select(x => x.DeviceName).ToList();
            var fingers = ids.Count;
            var row = new PersonRow(p.EmployeeNo, p.FullName, Initials(p.FullName), p.GroupId is { } g ? groups.GetValueOrDefault(g) : null, hasFace, fingers,
                sync, waiting, now - p.CreatedAt < TimeSpan.FromDays(7), p.Status == PersonStatus.Locked);
            var keep = filter switch
            {
                "noFace" => !hasFace && !row.Locked,
                "noFinger" => fingers == 0 && !row.Locked,
                "notSynced" => sync != "ok" && !row.Locked,
                "locked" => row.Locked,
                _ => true,
            };
            if (keep) rows.Add(row);
        }
        return rows;
    }

    private PersonDetail PeopleCreate(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        Enroll.CreatePerson(new NewPerson(Str(a, "fullName"), no, OptStr(a, "groupName"), ParseDate(OptStr(a, "validTo"))));
        return PersonDetailOf(no.Trim());
    }

    private PersonDetail PeopleUpdate(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        Enroll.UpdatePerson(new PersonEdit(no, Str(a, "fullName"), OptStr(a, "groupName"), ParseDate(OptStr(a, "validTo"))));
        return PersonDetailOf(no);
    }

    private PersonDetail PeopleAction(JsonElement a, Action<string> act)
    {
        var no = Str(a, "employeeNo");
        act(no);
        return PersonDetailOf(no);
    }

    private object? PeopleDelete(JsonElement a)
    {
        Enroll.Delete(Str(a, "employeeNo"));
        return null;
    }

    private PersonDetail PersonDetailOf(string no)
    {
        var p = Db.People.Get(no) ?? throw new EnrollException("Không tìm thấy nhân viên, có thể đã bị xoá. Tải lại danh sách.");
        var face = Db.Biometrics.GetFace(no);
        var fingers = Db.Biometrics.GetFingers(no).GroupBy(f => f.FingerId).OrderBy(g => g.Key)
            .Select(g => new FingerView(g.Key, FingerLabel(g.Key), g.Max(f => f.Quality))).ToList();
        return new PersonDetail(p.EmployeeNo, p.FullName, Db.People.GroupName(p.GroupId), p.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.Status == PersonStatus.Locked,
            face is null ? null : DataUrl(face.Jpeg), fingers, SyncPerson(no));
    }

    private static string DataUrl(byte[] jpeg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);

    private async Task<FaceResult> FaceCapture(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        var jpeg = await Enroll.CaptureFaceAsync(no, Int(a, "deviceId"), _cts.Token);
        return new FaceResult(DataUrl(jpeg), SyncPerson(no));
    }

    private async Task<FacePreviewView> FacePreview(JsonElement a)
    {
        var jpeg = await Enroll.PreviewAsync(Int(a, "deviceId"), _cts.Token);
        return new FacePreviewView(jpeg is null ? null : DataUrl(jpeg), jpeg is null);
    }

    private FaceResult FaceSave(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        var url = Str(a, "imageDataUrl");
        var source = OptStr(a, "source") == "webcam" ? BiometricSource.Webcam : BiometricSource.File;
        const string bad = "Không đọc được ảnh. Hãy chọn lại ảnh JPEG.";
        var i = url.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || i < 0) throw new EnrollException(bad);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(url[(i + 7)..]); }
        catch (FormatException) { throw new EnrollException(bad); }
        Enroll.SaveFace(no, bytes, source);
        return new FaceResult(DataUrl(bytes), SyncPerson(no));
    }

    private async Task<FingerResult> FingerCapture(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        var id = Int(a, "fingerId");
        var cap = await Enroll.CaptureFingerAsync(no, Int(a, "deviceId"), id, _cts.Token);
        return new FingerResult(id, FingerLabel(id), cap.Quality, SyncPerson(no));
    }

    private PersonDetail FingerDelete(JsonElement a)
    {
        var no = Str(a, "employeeNo");
        Enroll.DeleteFinger(no, Int(a, "fingerId"));
        return PersonDetailOf(no);
    }
}
