using System.Globalization;
using Acs.Core.Enroll;
using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Storage;
using Acs.Core.Sync;

namespace Acs.Core.Import;

public sealed record ImportProgress(int Done, int Total, string CurrentName);
public sealed record ImportSummary(int People, int WithFace, int FacesMissing, int Fingers);

/// <summary>First-run migration: pulls people, face photos and fingerprints from a device into the app.
/// Never overwrites what the app already has; only fills missing people, faces and finger rows.
/// One person's unreadable photo or finger list never stops the others: the photo counts as missing, the fingers
/// stay unknown, and people that could not be read are counted (log + the last progress text). Only a device that
/// is gone (timeout, offline) or refuses the login stops the run; running it again continues where it stopped.</summary>
public sealed class DeviceImporter(Database db, IHikDeviceFactory factory, TimeProvider time)
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    public const string LogAction = "Lấy dữ liệu";

    /// <summary>Last import.progress text when some people could not be read completely (C4 has no field for it).</summary>
    public static string NotReadText(int n) => $"Không đọc được đủ dữ liệu của {n} người. Xem Nhật ký; chạy lại Lấy dữ liệu sau nếu cần.";

    /// <summary>Per-run memory: after the device refused a photo download with an auth error, no more photo downloads.</summary>
    private sealed class Run { public bool PhotosOff; }

    public async Task<ImportSummary> ImportAllAsync(int deviceId, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var dev = db.Devices.Get(deviceId) ?? throw new InvalidOperationException("Không tìm thấy thiết bị.");
        AuthGuard.Require(dev);
        try { return await ImportAllCoreAsync(dev, progress, ct); }
        catch (HikAuthException) { AuthGuard.Pause(db, dev.Id, null); throw; }   // the host wakes the worker afterwards
    }

    private async Task<ImportSummary> ImportAllCoreAsync(DeviceRecord dev, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var hik = factory.Create(dev);
        var users = new List<HikUser>();
        await foreach (var u in hik.ListUsersAsync(ct)) users.Add(u);

        int people = 0, withFace = 0, missing = 0, fingers = 0, done = 0, notRead = 0;
        var run = new Run();
        foreach (var u in users)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress(done, users.Count, u.Name));
            try
            {
                var r = await ImportOneAsync(dev, hik, u, run, ct);
                if (r.NewPerson) people++;
                if (r.FaceStored) withFace++;
                if (r.FaceMissing) missing++;
                fingers += r.Fingers;
                if (!r.FingersRead) notRead++;
            }
            catch (Exception e) when (e is not (OperationCanceledException or HikTransientException or HikAuthException))
            {
                // Anything else is about this one person (odd data, a refused read): skip them, keep going.
                notRead++;
                // The message stays Vietnamese without raw device text (R13); the error type helps the developer.
                Log(u.EmployeeNo, dev.Id, $"Không đọc được người này từ máy nên chưa lấy về app. Chạy lại Lấy dữ liệu; nếu vẫn lỗi, báo lại cho người làm phần mềm (mã lỗi {e.GetType().Name}).");
            }
            progress?.Report(new ImportProgress(++done, users.Count, u.Name));
        }
        if (notRead > 0)
        {
            Log(null, dev.Id, NotReadText(notRead));
            progress?.Report(new ImportProgress(done, users.Count, NotReadText(notRead)));
        }
        return new ImportSummary(people, withFace, missing, fingers);
    }

    public async Task ImportPersonAsync(int deviceId, string employeeNo, CancellationToken ct)
    {
        var dev = db.Devices.Get(deviceId) ?? throw new InvalidOperationException("Không tìm thấy thiết bị.");
        AuthGuard.Require(dev);
        var hik = factory.Create(dev);
        try
        {
            if (await hik.FindUserAsync(employeeNo, ct) is not { } u) return;
            await ImportOneAsync(dev, hik, u, new Run(), ct);
        }
        catch (HikAuthException) { AuthGuard.Pause(db, dev.Id, null); throw; }
    }

    private readonly record struct One(bool NewPerson, bool FaceStored, bool FaceMissing, int Fingers, bool FingersRead);

    private void Log(string? employeeNo, int deviceId, string message)
    {
        try { db.Log.Add(employeeNo, deviceId, LogAction, false, message); } catch { /* the log must not stop the import */ }
    }

    /// <summary>A read for this person failed with a timeout or 5xx: is the device itself still there? If not, the
    /// probe throws and the run stops (it would only time out person after person).</summary>
    private static async Task ProbeAsync(IHikDevice hik, string no, CancellationToken ct) => await hik.FindUserAsync(no, ct);

    private async Task<One> ImportOneAsync(DeviceRecord dev, IHikDevice hik, HikUser u, Run run, CancellationToken ct)
    {
        var no = u.EmployeeNo;
        var needFace = u.FaceCount > 0 && !db.Biometrics.HasFace(no);

        // Device reads happen outside any transaction.
        byte[]? face = null;
        var faceMissing = false;
        if (needFace && run.PhotosOff) faceMissing = true;
        else if (needFace)
        {
            try
            {
                var bytes = await hik.DownloadFaceAsync(no, ct);
                if (bytes is not null && bytes.Length <= EnrollService.MaxFaceBytes && bytes.AsSpan().StartsWith(JpegMagic)) face = bytes;
                else faceMissing = true;
            }
            catch (Exception e) when (e is HikRejectedException or HikNotSupportedException)
            {
                faceMissing = true;   // 403 / 404 / refused: "cần chụp lại 1 lần", like a device that keeps no photo
            }
            catch (HikAuthException)
            {
                // The photo link refused our login. Retrying it for everyone would cost a failed login per person
                // (IP lock-out), so skip photos for the rest of this run.
                faceMissing = true;
                run.PhotosOff = true;
                Log(no, dev.Id, "Máy không cho tải ảnh khuôn mặt (từ chối đăng nhập khi tải ảnh). App bỏ qua ảnh của những người còn lại; " +
                    "người đó vẫn vào được máy này, cần chụp lại 1 lần nếu muốn gửi sang máy khác.");
            }
            catch (HikTransientException)
            {
                await ProbeAsync(hik, no, ct);
                faceMissing = true;
            }
        }

        IReadOnlyList<HikFinger> devFingers = [];
        var fingersKnown = true;
        var fingersRead = true;
        if (u.FingerCount > 0)
        {
            try { devFingers = await hik.ReadFingerprintsAsync(no, ct); }
            catch (Exception e) when (e is HikRejectedException or HikNotSupportedException or HikTransientException)
            {
                if (e is HikTransientException) await ProbeAsync(hik, no, ct);
                fingersKnown = false;   // unknown, never "no fingers": the Lock rule must not delete on the strength of it
                fingersRead = false;
                Log(no, dev.Id, "Không đọc được vân tay của người này trên máy, app chưa lấy vân tay về. Người đó vẫn vào được máy này; " +
                    "chạy So khớp 2 máy sau.");
            }
        }
        // A template that is not valid base64 cannot be stored or sent: the device still has that finger.
        var usable = devFingers.Where(f => IsBase64(f.TemplateBase64)).ToList();
        if (usable.Count < devFingers.Count)
        {
            fingersRead = false;
            Log(no, dev.Id, $"Có {devFingers.Count - usable.Count} vân tay trên máy có dữ liệu không đọc được, app chưa lấy về. Người đó vẫn vào được máy này.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        return db.Tx(() =>
        {
            var added = 0;
            var isNew = db.People.Get(no) is null;
            if (isNew)
            {
                var from = ParseDate(u.BeginTime) ?? DateOnly.FromDateTime(now);
                var to = JobRules.IsNoEnd(u.EndTime) ? null : ParseDate(u.EndTime);   // 2037-12-31 = "không thời hạn" too
                var locked = !u.Enabled;
                var name = string.IsNullOrWhiteSpace(u.Name) ? no : u.Name.Trim();
                int? group = GroupRules.FromName(name) is { } code ? db.People.GetOrCreateGroup(code) : null;
                db.People.Insert(new PersonRecord(no, name, group, from, to,
                    locked ? PersonStatus.Locked : PersonStatus.Active, now, now, locked ? now : null));
            }
            var stored = false;
            if (face is not null && !db.Biometrics.HasFace(no))
            {
                db.Biometrics.SetFace(new FaceRecord(no, face, BiometricSource.Imported, now));
                stored = true;
            }
            var have = db.Biometrics.FingerGroups(no).Select(f => f.FingerId).ToHashSet();
            foreach (var f in usable)
            {
                if (!have.Add(f.FingerId)) continue;
                db.Biometrics.SetFinger(new FingerRecord(no, f.FingerId, dev.FpGroup, f.TemplateBase64, null, BiometricSource.Imported, now));
                added++;
            }
            var prev = db.Jobs.GetState(no, dev.Id);
            var ids = devFingers.Select(f => f.FingerId).ToHashSet();
            db.Jobs.SetState(new DeviceStateRecord(no, dev.Id, true, u.Enabled, u.FaceCount > 0, ids,
                prev?.NeedsScan.Where(n => !ids.Contains(n)).ToHashSet() ?? new HashSet<int>(), now) { FingersKnown = fingersKnown });
            return new One(isNew, stored, faceMissing, added, fingersRead);
        });
    }

    private static bool IsBase64(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var buf = new byte[(s.Length * 3 + 3) / 4];
        return Convert.TryFromBase64String(s, buf, out _);
    }

    private static DateOnly? ParseDate(string s) =>
        s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
