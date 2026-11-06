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
public sealed class Reconciler(Database db, IHikDeviceFactory factory, SyncEngine sync, DeviceImporter importer, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>Key used in the strangers dictionary: "deviceId:employeeNo".</summary>
    public static string StrangerKey(int deviceId, string employeeNo) => $"{deviceId}:{employeeNo}";

    public async Task<ReconcilePlan> BuildAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var diffs = new List<DeviceDiff>();
        var people = db.People.List();
        foreach (var dev in db.Devices.List())
        {
            ct.ThrowIfCancellationRequested();
            if (dev.AuthPaused) throw new HikAuthException($"Không đọc được {dev.Name}: {JobRules.AuthMessage}");
            try { diffs.Add(await BuildDeviceAsync(dev, people, progress, ct)); }
            catch (OperationCanceledException) { throw; }
            catch (HikAuthException)
            {
                AuthGuard.Pause(db, dev.Id, sync);
                throw new HikAuthException($"Không đọc được {dev.Name}: {JobRules.AuthMessage}");
            }
            catch (HikException ex)
            {
                throw new HikException($"Không đọc được {dev.Name}: {ex.Message.TrimEnd('.', ' ')}. Kiểm tra máy có điện/mạng rồi so khớp lại.", ex.Detail);
            }
            catch (Exception ex)
            {
                throw new HikException($"Không đọc được {dev.Name}: lỗi không xác định. Kiểm tra máy có điện/mạng rồi so khớp lại.", $"{ex.GetType().Name}: {ex.Message}");
            }
        }
        return new ReconcilePlan(diffs);
    }

    private async Task<DeviceDiff> BuildDeviceAsync(DeviceRecord dev, IReadOnlyList<PersonRecord> people, IProgress<string>? progress, CancellationToken ct)
    {
        var byNo = people.ToDictionary(p => p.EmployeeNo);
        var hik = factory.Create(dev);
        var users = new Dictionary<string, HikUser>();
        await foreach (var u in hik.ListUsersAsync(ct)) users[u.EmployeeNo] = u;

        var items = new List<DiffItem>();
        var strangers = new List<Stranger>();
        var states = new List<DeviceStateRecord>();
        var seen = 0;
        foreach (var u in users.Values)
        {
            progress?.Report($"Đang đọc {dev.Name} ({++seen}/{users.Count})…");
            if (!byNo.ContainsKey(u.EmployeeNo)) strangers.Add(new Stranger(dev.Id, u.EmployeeNo, u.Name, u.FaceCount, u.FingerCount));
        }

        var present = people.Count(p => users.ContainsKey(p.EmployeeNo));
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
                states.Add(new DeviceStateRecord(p.EmployeeNo, dev.Id, false, false, false, new HashSet<int>(), new HashSet<int>(), Now));
                continue;
            }
            progress?.Report($"Đang so khớp {dev.Name} ({++done}/{present})…");

            if (p.Status == PersonStatus.Active)
            {
                var w = JobRules.ToWrite(p, dev.DoorNo, true);
                if (du.Name.Trim() != p.FullName.Trim() || !du.Enabled
                    || JobRules.NormTime(du.BeginTime) != JobRules.NormTime(w.BeginTime)
                    || JobRules.NormTime(du.EndTime) != JobRules.NormTime(w.EndTime))
                    items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.InfoMismatch, []));
                if (appFace && du.FaceCount == 0)
                    items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.MissingFace, []));
            }
            else if (du.Enabled)
                items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.LockedButEnabled, []));

            // Fingers are read when the counts differ or the complete list was never confirmed for this device (no
            // state row, or an earlier read failed); otherwise keep what was stored before.
            IReadOnlySet<int> fingers = prev?.Fingers ?? new HashSet<int>();
            IReadOnlySet<int> needScan = prev?.NeedsScan ?? new HashSet<int>();
            var known = prev?.FingersKnown ?? false;
            if (prev is null || !prev.FingersKnown || du.FingerCount != appFingers.Count)
            {
                HashSet<int>? devIds = null;
                try { devIds = (await hik.ReadFingerprintsAsync(p.EmployeeNo, ct)).Select(f => f.FingerId).ToHashSet(); }
                catch (Exception e) when (e is HikRejectedException or HikNotSupportedException)
                {
                    // This person's list is unreadable: keep it unknown (never "no fingers") and carry on with the plan.
                    known = false;
                }
                if (devIds is not null)
                {
                    if (p.Status == PersonStatus.Active)
                    {
                        var missing = appFingers.Where(f => !devIds.Contains(f) && !needScan.Contains(f)).ToList();
                        var extra = devIds.Where(f => !appFingers.Contains(f)).OrderBy(x => x).ToList();
                        if (missing.Count > 0) items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.MissingFingers, missing));
                        if (extra.Count > 0) items.Add(new DiffItem(p.EmployeeNo, p.FullName, DiffKind.ExtraFingers, extra));
                    }
                    fingers = devIds;
                    known = true;
                    needScan = needScan.Where(n => !devIds.Contains(n)).ToHashSet();
                }
            }
            states.Add(new DeviceStateRecord(p.EmployeeNo, dev.Id, true, du.Enabled, du.FaceCount > 0, fingers, needScan, Now) { FingersKnown = known });
        }
        db.Tx(() =>
        {
            foreach (var s in states) db.Jobs.SetState(s);
            foreach (var no in users.Keys) db.People.BumpHighWater(no);   // strangers' numbers are taken too
            return 0;
        });
        return new DeviceDiff(dev.Id, dev.Name, items, strangers);
    }

    public async Task ApplyAsync(ReconcilePlan plan, IReadOnlyDictionary<string, StrangerChoice> strangers, CancellationToken ct)
    {
        // Imports talk to devices, so they run before (and outside) the transaction. One failure never stops the others.
        var imported = new List<(int DeviceId, string No)>();
        var failed = new List<string>();
        foreach (var d in plan.Devices)
            foreach (var s in d.Strangers)
            {
                if (!strangers.TryGetValue(StrangerKey(d.DeviceId, s.EmployeeNo), out var c) || c != StrangerChoice.Import) continue;
                ct.ThrowIfCancellationRequested();
                try
                {
                    await importer.ImportPersonAsync(d.DeviceId, s.EmployeeNo, ct);
                    if (db.People.Get(s.EmployeeNo) is null) failed.Add(s.EmployeeNo); else imported.Add((d.DeviceId, s.EmployeeNo));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { failed.Add(s.EmployeeNo); }
            }

        var now = Now;
        db.Tx(() =>
        {
            foreach (var d in plan.Devices)
            {
                foreach (var it in d.Items)
                {
                    var no = it.EmployeeNo;
                    if (db.People.Get(no) is null) continue;
                    switch (it.Kind)
                    {
                        case DiffKind.MissingPerson: EnqueueFull(d.DeviceId, no, now); break;
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
            }
            // An imported stranger goes to every other device too (the app is the master copy).
            foreach (var (srcId, no) in imported)
                foreach (var other in db.Devices.List())
                {
                    if (other.Id == srcId) continue;
                    if (plan.Devices.Any(x => x.DeviceId == other.Id && x.Strangers.Any(s => s.EmployeeNo == no))) continue;
                    EnqueueFull(other.Id, no, now);
                }
            return 0;
        });
        sync.Wake();
        if (failed.Count > 0)
            throw new HikException($"Chưa lấy được người dùng từ máy: {string.Join(", ", failed.Distinct())}. Kiểm tra máy có điện/mạng rồi so khớp lại.");
    }

    private void EnqueueFull(int deviceId, string no, DateTime now)
    {
        db.Jobs.Enqueue(deviceId, no, JobKind.UpsertUser, 0, now);
        if (db.Biometrics.GetFace(no) is not null) db.Jobs.Enqueue(deviceId, no, JobKind.SetFace, 0, now);
        foreach (var f in db.Biometrics.GetFingers(no).Select(f => f.FingerId).Distinct())
            db.Jobs.Enqueue(deviceId, no, JobKind.SetFinger, f, now);
    }
}
