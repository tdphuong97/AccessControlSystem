using Acs.Core.Enroll;
using Acs.Core.Hik;
using Acs.Core.Import;
using Acs.Core.Model;
using Acs.Core.Storage;
using Acs.Core.Sync;

namespace Acs.Core.Reconcile;

public sealed record ReconcilePlan(IReadOnlyList<DeviceDiff> Devices);
public sealed record DeviceDiff(int DeviceId, string DeviceName, IReadOnlyList<DiffItem> Items, IReadOnlyList<Stranger> Strangers);
public sealed record DiffItem(string EmployeeNo, string FullName, DiffKind Kind, IReadOnlyList<int> Fingers);
public enum DiffKind { MissingPerson, InfoMismatch, MissingFace, MissingFingers, ExtraFingers, LockedButEnabled }
public sealed record Stranger(int DeviceId, string EmployeeNo, string Name, int FaceCount, int FingerCount);
public enum StrangerChoice { Skip, Import, Remove }

/// <summary>"So khớp 2 máy": compares each device with the app and adds what is missing.
/// Nothing is deleted from a device unless the user explicitly chose Remove for a stranger.</summary>
public sealed class Reconciler(Database db, IHikDeviceFactory factory, SyncEngine sync, DeviceImporter importer)
{
    /// <summary>Key used in the strangers dictionary: "deviceId:employeeNo".</summary>
    public static string StrangerKey(int deviceId, string employeeNo) => $"{deviceId}:{employeeNo}";

    public async Task<ReconcilePlan> BuildAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var diffs = new List<DeviceDiff>();
        var people = db.People.List();
        var byNo = people.ToDictionary(p => p.EmployeeNo);
        foreach (var dev in db.Devices.List())
        {
            ct.ThrowIfCancellationRequested();
            var hik = factory.Create(dev);
            var users = new Dictionary<string, HikUser>();
            await foreach (var u in hik.ListUsersAsync(ct)) users[u.EmployeeNo] = u;

            var items = new List<DiffItem>();
            var strangers = new List<Stranger>();
            var seen = 0;
            foreach (var u in users.Values)
            {
                progress?.Report($"Đang đọc {dev.Name} ({++seen}/{users.Count})…");
                if (!byNo.ContainsKey(u.EmployeeNo)) strangers.Add(new Stranger(dev.Id, u.EmployeeNo, u.Name, u.FaceCount, u.FingerCount));
            }

            var done = 0;
            foreach (var p in people)
            {
                ct.ThrowIfCancellationRequested();
                var appFace = db.Biometrics.GetFace(p.EmployeeNo) is not null;
                var appFingers = db.Biometrics.GetFingers(p.EmployeeNo).Select(f => f.FingerId).Distinct().OrderBy(x => x).ToList();
                var prev = db.Jobs.GetState(p.EmployeeNo, dev.Id);

                if (!users.TryGetValue(p.EmployeeNo, out var du))
                {
                    if (p.Status == PersonStatus.Active) items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.MissingPerson, appFingers));
                    db.Jobs.SetState(new DeviceStateRecord(p.EmployeeNo, dev.Id, false, false, false, new HashSet<int>(), new HashSet<int>(), DateTime.UtcNow));
                    continue;
                }
                progress?.Report($"Đang so khớp {dev.Name} ({++done}/{people.Count})…");

                var devFingerIds = appFingers.ToHashSet();
                if (p.Status == PersonStatus.Active)
                {
                    var w = JobRules.ToWrite(p, dev.DoorNo, true);
                    if (du.Name != p.FullName || !du.Enabled
                        || JobRules.NormTime(du.BeginTime) != JobRules.NormTime(w.BeginTime)
                        || JobRules.NormTime(du.EndTime) != JobRules.NormTime(w.EndTime))
                        items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.InfoMismatch, []));
                    if (appFace && du.FaceCount == 0)
                        items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.MissingFace, []));
                }
                else if (du.Enabled)
                    items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.LockedButEnabled, []));

                if (du.FingerCount != appFingers.Count)
                {
                    devFingerIds = (await hik.ReadFingerprintsAsync(p.EmployeeNo, ct)).Select(f => f.FingerId).ToHashSet();
                    if (p.Status == PersonStatus.Active)
                    {
                        var needScan = prev?.NeedsScan ?? new HashSet<int>();
                        var missing = appFingers.Where(f => !devFingerIds.Contains(f) && !needScan.Contains(f)).ToList();
                        var extra = devFingerIds.Where(f => !appFingers.Contains(f)).OrderBy(x => x).ToList();
                        if (missing.Count > 0) items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.MissingFingers, missing));
                        if (extra.Count > 0) items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.ExtraFingers, extra));
                    }
                }
                db.Jobs.SetState(new DeviceStateRecord(p.EmployeeNo, dev.Id, true, du.Enabled, du.FaceCount > 0, devFingerIds,
                    prev?.NeedsScan.Where(n => !devFingerIds.Contains(n)).ToHashSet() ?? new HashSet<int>(), DateTime.UtcNow));
            }
            diffs.Add(new DeviceDiff(dev.Id, dev.Name, items, strangers));
        }
        return new ReconcilePlan(diffs);
    }

    public async Task ApplyAsync(ReconcilePlan plan, IReadOnlyDictionary<string, StrangerChoice> strangers, CancellationToken ct)
    {
        // Imports talk to devices, so they run before (and outside) the transactions.
        foreach (var d in plan.Devices)
            foreach (var s in d.Strangers)
                if (strangers.TryGetValue(StrangerKey(d.DeviceId, s.EmployeeNo), out var c) && c == StrangerChoice.Import)
                    await importer.ImportPersonAsync(d.DeviceId, s.EmployeeNo, ct);

        var now = DateTime.UtcNow;
        foreach (var d in plan.Devices)
        {
            ct.ThrowIfCancellationRequested();
            db.Tx(() =>
            {
                foreach (var it in d.Items)
                {
                    var no = it.EmployeeNo;
                    if (db.People.Get(no) is null) continue;
                    switch (it.Kind)
                    {
                        case DiffKind.MissingPerson:
                            db.Jobs.Enqueue(d.DeviceId, no, JobKind.UpsertUser, 0, now);
                            if (db.Biometrics.GetFace(no) is not null) db.Jobs.Enqueue(d.DeviceId, no, JobKind.SetFace, 0, now);
                            foreach (var f in db.Biometrics.GetFingers(no).Select(f => f.FingerId).Distinct())
                                db.Jobs.Enqueue(d.DeviceId, no, JobKind.SetFinger, f, now);
                            break;
                        case DiffKind.InfoMismatch: db.Jobs.Enqueue(d.DeviceId, no, JobKind.UpsertUser, 0, now); break;
                        case DiffKind.MissingFace: db.Jobs.Enqueue(d.DeviceId, no, JobKind.SetFace, 0, now); break;
                        case DiffKind.MissingFingers:
                            foreach (var f in it.Fingers) db.Jobs.Enqueue(d.DeviceId, no, JobKind.SetFinger, f, now);
                            break;
                        case DiffKind.LockedButEnabled: LockRules.EnqueueLock(db, no, now); break;
                        case DiffKind.ExtraFingers: break; // information only
                    }
                }
                foreach (var s in d.Strangers)
                    if (strangers.TryGetValue(StrangerKey(d.DeviceId, s.EmployeeNo), out var c) && c == StrangerChoice.Remove)
                        db.Jobs.Enqueue(d.DeviceId, s.EmployeeNo, JobKind.DeleteUser, 0, now);
                return 0;
            });
        }
        sync.Wake();
    }
}
