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
/// Never overwrites what the app already has; only fills missing people, faces and finger rows.</summary>
public sealed class DeviceImporter(Database db, IHikDeviceFactory factory, TimeProvider time)
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public async Task<ImportSummary> ImportAllAsync(int deviceId, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var dev = db.Devices.Get(deviceId) ?? throw new InvalidOperationException("Không tìm thấy thiết bị.");
        var hik = factory.Create(dev);
        var users = new List<HikUser>();
        await foreach (var u in hik.ListUsersAsync(ct)) users.Add(u);

        int people = 0, withFace = 0, missing = 0, fingers = 0, done = 0;
        foreach (var u in users)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress(done, users.Count, u.Name));
            var r = await ImportOneAsync(dev, hik, u, ct);
            if (r.NewPerson) people++;
            if (r.FaceStored) withFace++;
            if (r.FaceMissing) missing++;
            fingers += r.Fingers;
            progress?.Report(new ImportProgress(++done, users.Count, u.Name));
        }
        return new ImportSummary(people, withFace, missing, fingers);
    }

    public async Task ImportPersonAsync(int deviceId, string employeeNo, CancellationToken ct)
    {
        var dev = db.Devices.Get(deviceId) ?? throw new InvalidOperationException("Không tìm thấy thiết bị.");
        var hik = factory.Create(dev);
        if (await hik.FindUserAsync(employeeNo, ct) is not { } u) return;
        await ImportOneAsync(dev, hik, u, ct);
    }

    private readonly record struct One(bool NewPerson, bool FaceStored, bool FaceMissing, int Fingers);

    private async Task<One> ImportOneAsync(DeviceRecord dev, IHikDevice hik, HikUser u, CancellationToken ct)
    {
        var no = u.EmployeeNo;
        var needFace = u.FaceCount > 0 && db.Biometrics.GetFace(no) is null;

        // Device reads happen outside any transaction.
        byte[]? face = null;
        var faceMissing = false;
        if (needFace)
        {
            var bytes = await hik.DownloadFaceAsync(no, ct);
            if (bytes is not null && bytes.Length <= EnrollService.MaxFaceBytes && bytes.AsSpan().StartsWith(JpegMagic)) face = bytes;
            else faceMissing = true;
        }
        IReadOnlyList<HikFinger> devFingers = u.FingerCount > 0 ? await hik.ReadFingerprintsAsync(no, ct) : [];

        var now = time.GetUtcNow().UtcDateTime;
        return db.Tx(() =>
        {
            var added = 0;
            var isNew = db.People.Get(no) is null;
            if (isNew)
            {
                var from = ParseDate(u.BeginTime) ?? DateOnly.FromDateTime(now);
                var to = JobRules.NormTime(u.EndTime) == JobRules.NormTime(JobRules.NoEndDate) ? null : ParseDate(u.EndTime);
                var locked = !u.Enabled;
                db.People.Insert(new PersonRecord(no, string.IsNullOrWhiteSpace(u.Name) ? no : u.Name.Trim(), null, from, to,
                    locked ? PersonStatus.Locked : PersonStatus.Active, now, now, locked ? now : null));
            }
            var stored = false;
            if (face is not null && db.Biometrics.GetFace(no) is null)
            {
                db.Biometrics.SetFace(new FaceRecord(no, face, BiometricSource.Imported, now));
                stored = true;
            }
            var have = db.Biometrics.GetFingers(no).Select(f => f.FingerId).ToHashSet();
            foreach (var f in devFingers)
            {
                if (!have.Add(f.FingerId)) continue;
                db.Biometrics.SetFinger(new FingerRecord(no, f.FingerId, dev.FpGroup, f.TemplateBase64, null, BiometricSource.Imported, now));
                added++;
            }
            var prev = db.Jobs.GetState(no, dev.Id);
            var ids = devFingers.Select(f => f.FingerId).ToHashSet();
            db.Jobs.SetState(new DeviceStateRecord(no, dev.Id, true, u.Enabled, u.FaceCount > 0, ids,
                prev?.NeedsScan.Where(n => !ids.Contains(n)).ToHashSet() ?? new HashSet<int>(), now));
            return new One(isNew, stored, faceMissing, added);
        });
    }

    private static DateOnly? ParseDate(string s) =>
        s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
