using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Storage;
using Acs.Core.Sync;

namespace Acs.Core.Enroll;

public sealed record NewPerson(string FullName, string EmployeeNo, string? GroupName, DateOnly? ValidTo);
public sealed record PersonEdit(string EmployeeNo, string FullName, string? GroupName, DateOnly? ValidTo);
public sealed class EnrollException(string message) : Exception(message);

public sealed partial class EnrollService(Database db, IHikDeviceFactory factory, SyncEngine sync, TimeProvider time)
{
    public const int MinFingerQuality = 40;
    public const int MaxNameLength = 32;
    public const int MaxFaceBytes = 200 * 1024;
    public const string NotJpegMessage = "Ảnh khuôn mặt phải là tệp JPEG. Chọn ảnh JPEG khác hoặc chụp bằng webcam.";
    public const string TooBigMessage = "Ảnh khuôn mặt quá lớn (tối đa 200 KB). Chụp lại hoặc chọn ảnh khác/webcam.";
    public const string NumberBusyMessage = "Mã này vừa xoá và đang chờ gỡ khỏi máy. Đợi máy gỡ xong hoặc dùng mã khác.";

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _busy = new();

    [GeneratedRegex("^[A-Za-z0-9]{1,32}$")] private static partial Regex NoRx();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public string NextEmployeeNo() => db.People.NextEmployeeNo();

    private static string CheckName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length is < 1 or > MaxNameLength)
            throw new EnrollException("Họ tên dài tối đa 32 ký tự (máy chấm công không nhận tên dài hơn).");
        return n;
    }

    private int? GroupId(string? name) => string.IsNullOrWhiteSpace(name) ? null : db.People.GetOrCreateGroup(name);

    private void EnqueueAll(string no, JobKind kind, int finger = 0)
    {
        foreach (var d in db.Devices.List()) db.Jobs.Enqueue(d.Id, no, kind, finger, Now);
    }

    /// <summary>
    /// A person goes to the devices once a device can recognise them (a face or a finger in the app) or once a device
    /// already holds them (imported, sent before). A new person whose capture failed or was skipped stays in the app only,
    /// so an unfinished registration never reaches a device. The first face or finger sends the person first.
    /// </summary>
    private bool Sendable(string no) =>
        db.Biometrics.HasFace(no) || db.Biometrics.FingerGroups(no).Count > 0
        || db.Devices.List().Any(d => db.Jobs.GetState(no, d.Id)?.UserPresent == true);

    /// <summary>Call before storing a face or finger: queues the person ahead of it when this is their first one.</summary>
    private void SendPersonIfFirst(string no)
    {
        if (!Sendable(no) && db.People.Get(no) is { Status: PersonStatus.Active }) EnqueueAll(no, JobKind.UpsertUser);
        db.People.SetDraft(no, false);
    }

    /// <summary>
    /// A new person still without face or finger, never sent (the flag alone is not trusted: the data must agree).
    /// Drafts are not kept: leaving the "Thêm người mới" steps drops them, and so does the next start.
    /// </summary>
    public bool IsDraft(string no) => db.People.DraftFlag(no) && !Sendable(no) && !db.Jobs.HasOpen(no);

    /// <summary>Drops a draft (nothing on any device, nothing worth keeping in Thùng rác). False when it is not one.</summary>
    public bool DiscardDraft(string no) => db.Tx(() =>
    {
        if (!IsDraft(no)) return false;
        db.People.Delete(no);
        return true;
    });

    /// <summary>At start: drafts left behind (the app was closed in the middle of "Thêm người mới").</summary>
    public int PurgeDrafts() => db.Tx(() => db.People.DraftNumbers().Count(DiscardDraft));

    /// <summary>A draft may still change its number (nothing was sent under the old one).</summary>
    public PersonRecord Renumber(string employeeNo, string newEmployeeNo)
    {
        var to = (newEmployeeNo ?? "").Trim();
        if (!NoRx().IsMatch(to)) throw new EnrollException("Mã nhân viên chỉ gồm chữ và số, tối đa 32 ký tự.");
        return db.Tx(() =>
        {
            var cur = db.People.Get(employeeNo) ?? throw new EnrollException("Không tìm thấy nhân viên.");
            if (to == cur.EmployeeNo) return cur;
            if (!IsDraft(employeeNo)) throw new EnrollException(NumberFixedMessage);
            if (db.People.Get(to) is not null) throw new EnrollException("Mã nhân viên đã có người dùng.");
            if (db.Jobs.HasOpen(to)) throw new EnrollException(NumberBusyMessage);
            var r = cur with { EmployeeNo = to, UpdatedAt = Now };
            db.People.Delete(employeeNo);
            db.People.Insert(r);
            db.People.SetDraft(to, true);
            return r;
        });
    }

    public const string NumberFixedMessage = "Mã không đổi được: người này đã được gửi xuống máy chấm công.";

    /// <summary>
    /// Brings a person back from Thùng rác with their face and fingers, under their old number or <paramref name="newNo"/>,
    /// and sends them to every device again (an active person with a face or finger).
    /// </summary>
    public PersonRecord Restore(long trashId, string? newNo)
    {
        var rec = db.Tx(() =>
        {
            var t = db.Trash.Get(trashId) ?? throw new EnrollException("Không thấy người này trong Thùng rác. Tải lại danh sách.");
            var no = string.IsNullOrWhiteSpace(newNo) ? t.Person.EmployeeNo : newNo.Trim();
            if (!NoRx().IsMatch(no)) throw new EnrollException("Mã nhân viên chỉ gồm chữ và số, tối đa 32 ký tự.");
            if (db.People.Get(no) is { } other)
                throw new EnrollException($"Mã {no} đang là của {other.FullName}. Nhập một mã khác để khôi phục.");
            if (db.Jobs.HasOpen(no)) throw new EnrollException(NumberBusyMessage);
            var now = Now;
            var p = t.Person with
            {
                EmployeeNo = no,
                GroupId = string.IsNullOrWhiteSpace(t.GroupName) ? null : db.People.GetOrCreateGroup(t.GroupName),
                UpdatedAt = now,
            };
            db.People.Insert(p);
            if (t.Face is { } f) db.Biometrics.SetFace(f with { EmployeeNo = no });
            foreach (var fr in t.Fingers) db.Biometrics.SetFinger(fr with { EmployeeNo = no });
            if (p.Status == PersonStatus.Active && (t.Face is not null || t.Fingers.Count > 0))
            {
                EnqueueAll(no, JobKind.UpsertUser);
                if (t.Face is not null) EnqueueAll(no, JobKind.SetFace);
                foreach (var id in t.Fingers.Select(x => x.FingerId).Distinct()) EnqueueAll(no, JobKind.SetFinger, id);
            }
            db.Trash.Remove(trashId);
            db.Log.Add(no, null, "Khôi phục", true, $"Khôi phục {p.FullName} từ Thùng rác" + (no != t.Person.EmployeeNo ? $" với mã mới (mã cũ {t.Person.EmployeeNo})" : "") + ".");
            return p;
        });
        sync.Wake();
        return rec;
    }

    private void RequirePerson(string no)
    {
        if (db.People.Get(no) is null) throw new EnrollException("Không tìm thấy nhân viên.");
    }

    public PersonRecord CreatePerson(NewPerson p)
    {
        var name = CheckName(p.FullName);
        var no = (p.EmployeeNo ?? "").Trim();
        if (!NoRx().IsMatch(no))
            throw new EnrollException("Mã nhân viên chỉ gồm chữ và số, tối đa 32 ký tự.");
        var rec = db.Tx(() =>
        {
            if (db.People.Get(no) is not null) throw new EnrollException("Mã nhân viên đã có người dùng.");
            // A queued DeleteUser for this number would be cancelled by the new UpsertUser and the old device user
            // (face, fingers) would be merged into the new person.
            if (db.Jobs.HasOpen(no)) throw new EnrollException(NumberBusyMessage);
            var now = Now;
            var r = new PersonRecord(no, name, GroupId(p.GroupName), DateOnly.FromDateTime(time.GetLocalNow().DateTime), p.ValidTo,
                PersonStatus.Active, now, now, null);
            db.People.Insert(r);   // sent with the first face or finger (Sendable)
            db.People.SetDraft(no, true);
            return r;
        });
        sync.Wake();
        return rec;
    }

    public PersonRecord UpdatePerson(PersonEdit e)
    {
        var name = CheckName(e.FullName);
        var rec = db.Tx(() =>
        {
            var cur = db.People.Get(e.EmployeeNo) ?? throw new EnrollException("Không tìm thấy nhân viên.");
            var r = cur with { FullName = name, GroupId = GroupId(e.GroupName), ValidTo = e.ValidTo, UpdatedAt = Now };
            db.People.Update(r);
            // A locked person is not pushed: an UpsertUser would supersede the pending DeleteUser/DisableUser.
            if (cur.Status == PersonStatus.Active && Sendable(r.EmployeeNo)) EnqueueAll(r.EmployeeNo, JobKind.UpsertUser);
            return r;
        });
        sync.Wake();
        return rec;
    }

    private async Task<T> Exclusive<T>(DeviceRecord dev, Func<IHikDevice, Task<T>> body)
    {
        AuthGuard.Require(dev);   // paused for a wrong password: never log in again from here
        var gate = _busy.GetOrAdd(dev.Id, _ => new SemaphoreSlim(1));
        if (!await gate.WaitAsync(0)) throw new EnrollException("Máy đang chụp/quét, đợi xong rồi bấm lại.");
        try { return await body(factory.Create(dev)); }
        catch (HikAuthException) { AuthGuard.Pause(db, dev.Id, sync); throw; }
        finally { gate.Release(); }
    }

    private static void CheckFingerId(int id)
    {
        if (id is < 1 or > 10) throw new EnrollException("Số ngón tay không hợp lệ.");
    }

    private DeviceRecord Device(int id) => db.Devices.Get(id) ?? throw new EnrollException("Không tìm thấy máy chấm công.");

    /// <summary>True while a face or finger capture runs on this device.</summary>
    public bool IsCapturing(int deviceId) => _busy.TryGetValue(deviceId, out var gate) && gate.CurrentCount == 0;

    /// <summary>
    /// One frame of the device camera for the live view, also while the device is capturing (on site the camera keeps
    /// answering, about 4 frames/s). A device paused for a wrong password is not asked (AuthGuard). A refused login here
    /// never pauses the device: the live view only loses that frame, and real wrong passwords are caught by the sync.
    /// </summary>
    public Task<byte[]> PreviewAsync(int deviceId, CancellationToken ct)
    {
        var dev = Device(deviceId);
        AuthGuard.Require(dev);
        return factory.Create(dev).SnapshotAsync(ct);
    }

    public async Task<byte[]> CaptureFaceAsync(string employeeNo, int deviceId, CancellationToken ct)
    {
        RequirePerson(employeeNo);
        var jpeg = await Exclusive(Device(deviceId), hik => hik.CaptureFaceAsync(ct));
        SaveFace(employeeNo, jpeg, BiometricSource.Device);
        return jpeg;
    }

    public void SaveFace(string employeeNo, byte[] jpeg, BiometricSource source)
    {
        if (jpeg is not { Length: >= 3 } || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[2] != 0xFF)
            throw new EnrollException(NotJpegMessage);
        if (jpeg.Length > MaxFaceBytes)
            throw new EnrollException(TooBigMessage);
        db.Tx(() =>
        {
            RequirePerson(employeeNo);
            SendPersonIfFirst(employeeNo);
            db.Biometrics.SetFace(new FaceRecord(employeeNo, jpeg, source, Now));
            EnqueueAll(employeeNo, JobKind.SetFace);
            return 0;
        });
        sync.Wake();
    }

    public async Task<CapturedFinger> CaptureFingerAsync(string employeeNo, int deviceId, int fingerId, CancellationToken ct)
    {
        CheckFingerId(fingerId);
        RequirePerson(employeeNo);
        var dev = Device(deviceId);
        var cap = await Exclusive(dev, hik => hik.CaptureFingerprintAsync(fingerId, ct));
        if (cap.Quality < MinFingerQuality)
            throw new EnrollException($"Vân tay chưa rõ (chất lượng {cap.Quality}). Đặt ngón ngay giữa, giữ yên rồi quét lại.");
        db.Tx(() =>
        {
            RequirePerson(employeeNo);
            SendPersonIfFirst(employeeNo);
            // A rescan replaces every copy of the finger, whatever the sensor group.
            db.Biometrics.DeleteFinger(employeeNo, fingerId);
            db.Biometrics.SetFinger(new FingerRecord(employeeNo, fingerId, dev.FpGroup, cap.TemplateBase64, cap.Quality,
                BiometricSource.Device, Now));
            EnqueueAll(employeeNo, JobKind.SetFinger, fingerId);
            return 0;
        });
        sync.Wake();
        return cap;
    }

    public void DeleteFinger(string employeeNo, int fingerId)
    {
        CheckFingerId(fingerId);
        db.Tx(() =>
        {
            db.Biometrics.DeleteFinger(employeeNo, fingerId);
            EnqueueAll(employeeNo, JobKind.DeleteFinger, fingerId);
            return 0;
        });
        sync.Wake();
    }

    public void Lock(string employeeNo)
    {
        db.Tx(() =>
        {
            var p = db.People.Get(employeeNo) ?? throw new EnrollException("Không tìm thấy nhân viên.");
            var now = Now;
            db.People.Update(p with { Status = PersonStatus.Locked, LockedAt = now, UpdatedAt = now });
            LockRules.EnqueueLock(db, employeeNo, now);
            return 0;
        });
        sync.Wake();
    }

    public void Unlock(string employeeNo)
    {
        db.Tx(() =>
        {
            var p = db.People.Get(employeeNo) ?? throw new EnrollException("Không tìm thấy nhân viên.");
            db.People.Update(p with { Status = PersonStatus.Active, LockedAt = null, UpdatedAt = Now });
            if (!Sendable(employeeNo)) return 0;   // never sent: stays in the app until a face or finger
            EnqueueAll(employeeNo, JobKind.UpsertUser);
            if (db.Biometrics.GetFace(employeeNo) is not null) EnqueueAll(employeeNo, JobKind.SetFace);
            foreach (var id in db.Biometrics.GetFingers(employeeNo).Select(f => f.FingerId).Distinct())
                EnqueueAll(employeeNo, JobKind.SetFinger, id);
            return 0;
        });
        sync.Wake();
    }

    /// <summary>
    /// "Xoá hẳn": off every device, and into Thùng rác with face and fingers so it can be restored. A draft (never sent)
    /// is simply dropped.
    /// </summary>
    public void Delete(string employeeNo)
    {
        db.Tx(() =>
        {
            if (DiscardDraft(employeeNo)) return 0;
            if (db.People.Get(employeeNo) is { } p)
            {
                db.Trash.Add(new TrashedPerson(p, db.People.GroupName(p.GroupId), db.Biometrics.GetFace(employeeNo),
                    db.Biometrics.GetFingers(employeeNo)), Now);
                db.Log.Add(employeeNo, null, "Chuyển vào Thùng rác", true, $"Xoá hẳn {p.FullName}: gỡ khỏi máy, giữ trong Thùng rác.");
            }
            EnqueueAll(employeeNo, JobKind.DeleteUser);
            db.People.Delete(employeeNo);
            return 0;
        });
        sync.Wake();
    }
}
