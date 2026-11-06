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
            db.People.BumpHighWater(no);
            var now = Now;
            var r = new PersonRecord(no, name, GroupId(p.GroupName), DateOnly.FromDateTime(time.GetLocalNow().DateTime), p.ValidTo,
                PersonStatus.Active, now, now, null);
            db.People.Insert(r);
            EnqueueAll(no, JobKind.UpsertUser);
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
            if (cur.Status == PersonStatus.Active) EnqueueAll(r.EmployeeNo, JobKind.UpsertUser);
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
            EnqueueAll(employeeNo, JobKind.UpsertUser);
            if (db.Biometrics.GetFace(employeeNo) is not null) EnqueueAll(employeeNo, JobKind.SetFace);
            foreach (var id in db.Biometrics.GetFingers(employeeNo).Select(f => f.FingerId).Distinct())
                EnqueueAll(employeeNo, JobKind.SetFinger, id);
            return 0;
        });
        sync.Wake();
    }

    public void Delete(string employeeNo)
    {
        db.Tx(() =>
        {
            EnqueueAll(employeeNo, JobKind.DeleteUser);
            db.People.Delete(employeeNo);
            return 0;
        });
        sync.Wake();
    }
}
