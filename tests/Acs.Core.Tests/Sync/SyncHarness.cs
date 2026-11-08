using System.Text;
using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Storage;
using Acs.Core.Sync;
using Acs.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Sync;

/// <summary>Database + fake time + fake devices + engine, with helpers that wait for an observable condition.</summary>
public sealed class SyncHarness : IAsyncDisposable
{
    public static readonly DateTime T0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    public readonly Database Db = TestDb.Create();
    public readonly FakeTimeProvider Time = new(new DateTimeOffset(T0));
    public readonly FakeDeviceFactory Factory = new();
    public SyncEngine Engine { get; }
    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public SyncHarness(IHikDeviceFactory? factory = null) => Engine = new SyncEngine(Db, factory ?? Factory, Time);

    public int AddDevice(string name = "Máy tầng trệt", string fpGroup = "ModelA", bool authPaused = false) =>
        Db.Devices.Insert(new DeviceRecord(0, name, "192.168.1.201", "admin", "pw", 1, false, false, null, null, null, fpGroup, authPaused, null));

    public void AddPerson(string no = "1001", string name = "Nguyễn An", PersonStatus status = PersonStatus.Active) =>
        Db.People.Insert(new PersonRecord(no, name, null, new DateOnly(2026, 1, 1), null, status, T0, T0, null));

    public void AddFace(string no = "1001") =>
        Db.Biometrics.SetFace(new FaceRecord(no, [0xFF, 0xD8, 0xFF, 0xD9], BiometricSource.Webcam, T0));

    public static string Tpl(string group, string data = "AAAA") => Convert.ToBase64String(Encoding.ASCII.GetBytes(group + ":" + data));

    public void AddFinger(string no, int id, string group, string data = "AAAA") =>
        Db.Biometrics.SetFinger(new FingerRecord(no, id, group, Tpl(group, data), 80, BiometricSource.Device, T0));

    public void Enqueue(int dev, string no, JobKind kind, int finger = 0) => Db.Jobs.Enqueue(dev, no, kind, finger, Now);

    public JobRecord Job(int dev, string no, JobKind kind, int finger = 0) =>
        Db.Jobs.ListOpen(dev).Single(j => j.EmployeeNo == no && j.Kind == kind && j.FingerId == finger);

    /// <summary>Waits (real time, short polls) until the condition holds, optionally moving fake time on each poll.</summary>
    public async Task Until(Func<bool> cond, TimeSpan? advancePerPoll = null, int timeoutMs = 10000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (!cond())
        {
            if (Environment.TickCount64 > end) throw new TimeoutException("Condition not reached.");
            if (advancePerPoll is { } a) Time.Advance(a);
            await Task.Delay(5);
        }
    }

    public async Task WaitDone(int dev) => await Until(() => Db.Jobs.ListOpen(dev).Count == 0);

    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync();
        Db.Dispose();
    }
}

/// <summary>Wraps a FakeDevice and rejects templates from another sensor group. The fake's own group check needs a
/// "group:" prefix, which cannot survive the base64 storage of templates, so this test double decodes first.</summary>
public sealed class StrictGroupDevice(FakeDevice inner, string group) : IHikDevice
{
    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) => inner.GetDeviceInfoAsync(ct);
    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
    public IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => inner.ListUsersAsync(ct);
    public Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct) => inner.FindUserAsync(employeeNo, ct);
    public Task UpsertUserAsync(HikUserWrite user, CancellationToken ct) => inner.UpsertUserAsync(user, ct);
    public Task DeleteUserAsync(string employeeNo, CancellationToken ct) => inner.DeleteUserAsync(employeeNo, ct);
    public Task<byte[]> CaptureFaceAsync(CancellationToken ct) => inner.CaptureFaceAsync(ct);
    public Task<byte[]> SnapshotAsync(CancellationToken ct) => inner.SnapshotAsync(ct);
    public Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct) => inner.SetFaceAsync(employeeNo, jpeg, ct);
    public Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct) => inner.HasFaceAsync(employeeNo, ct);
    public Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct) => inner.DownloadFaceAsync(employeeNo, ct);
    public Task DeleteFaceAsync(string employeeNo, CancellationToken ct) => inner.DeleteFaceAsync(employeeNo, ct);
    public Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct) => inner.CaptureFingerprintAsync(fingerId, ct);
    public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) => inner.ReadFingerprintsAsync(employeeNo, ct);
    public Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct) => inner.DeleteFingerprintAsync(employeeNo, fingerId, ct);

    public Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct)
    {
        var text = Encoding.ASCII.GetString(Convert.FromBase64String(templateBase64));
        if (!text.StartsWith(group + ":", StringComparison.Ordinal))
            throw new HikRejectedException("fpMismatch", "Mẫu vân tay không khớp loại cảm biến của thiết bị này.");
        return inner.SetFingerprintAsync(employeeNo, fingerId, templateBase64, readerNo, ct);
    }
}

/// <summary>Passes every call to the inner device; test-local wrappers override only the calls they script.</summary>
public class ForwardingDevice(IHikDevice inner) : IHikDevice
{
    public virtual Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) => inner.GetDeviceInfoAsync(ct);
    public virtual Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
    public virtual IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => inner.ListUsersAsync(ct);
    public virtual Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct) => inner.FindUserAsync(employeeNo, ct);
    public virtual Task UpsertUserAsync(HikUserWrite user, CancellationToken ct) => inner.UpsertUserAsync(user, ct);
    public virtual Task DeleteUserAsync(string employeeNo, CancellationToken ct) => inner.DeleteUserAsync(employeeNo, ct);
    public virtual Task<byte[]> CaptureFaceAsync(CancellationToken ct) => inner.CaptureFaceAsync(ct);
    public virtual Task<byte[]> SnapshotAsync(CancellationToken ct) => inner.SnapshotAsync(ct);
    public virtual Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct) => inner.SetFaceAsync(employeeNo, jpeg, ct);
    public virtual Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct) => inner.HasFaceAsync(employeeNo, ct);
    public virtual Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct) => inner.DownloadFaceAsync(employeeNo, ct);
    public virtual Task DeleteFaceAsync(string employeeNo, CancellationToken ct) => inner.DeleteFaceAsync(employeeNo, ct);
    public virtual Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct) => inner.CaptureFingerprintAsync(fingerId, ct);
    public virtual Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct) =>
        inner.SetFingerprintAsync(employeeNo, fingerId, templateBase64, readerNo, ct);
    public virtual Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) => inner.ReadFingerprintsAsync(employeeNo, ct);
    public virtual Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct) => inner.DeleteFingerprintAsync(employeeNo, fingerId, ct);
}

/// <summary>A device whose finger list cannot be read for the given people (non-JSON answer).</summary>
public sealed class UnreadableFingersDevice(IHikDevice inner, params string[] employeeNos) : ForwardingDevice(inner)
{
    public override Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) =>
        employeeNos.Contains(employeeNo)
            ? throw new HikRejectedException("fpListUnreadable", HikClient.FpUnreadableMessage, "<html>")
            : base.ReadFingerprintsAsync(employeeNo, ct);
}

public sealed class SingleDeviceFactory(IHikDevice device) : IHikDeviceFactory
{
    public IHikDevice Create(DeviceRecord d) => device;
}

/// <summary>Wraps a FakeDevice with failure knobs the fake does not have.</summary>
public sealed class ScriptedDevice(FakeDevice inner) : IHikDevice
{
    public volatile bool TransientOnWrites;   // reachable device answering 5xx/busy to writes
    public volatile bool ThrowPlainOnPing;    // an unwrapped exception on GetDeviceInfo
    public volatile bool KeepOldEndTime;      // device ignores a changed valid-to date

    public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) =>
        ThrowPlainOnPing ? throw new InvalidOperationException("boom") : inner.GetDeviceInfoAsync(ct);
    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
    public IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => inner.ListUsersAsync(ct);
    public Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct) => inner.FindUserAsync(employeeNo, ct);
    public async Task UpsertUserAsync(HikUserWrite user, CancellationToken ct)
    {
        if (TransientOnWrites) throw new HikTransientException("Thiết bị đang bận. Thử lại sau.");
        if (KeepOldEndTime && await inner.FindUserAsync(user.EmployeeNo, ct) is { } old) user = user with { EndTime = old.EndTime };
        await inner.UpsertUserAsync(user, ct);
    }
    public Task DeleteUserAsync(string employeeNo, CancellationToken ct) => inner.DeleteUserAsync(employeeNo, ct);
    public Task<byte[]> CaptureFaceAsync(CancellationToken ct) => inner.CaptureFaceAsync(ct);
    public Task<byte[]> SnapshotAsync(CancellationToken ct) => inner.SnapshotAsync(ct);
    public Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct) => inner.SetFaceAsync(employeeNo, jpeg, ct);
    public Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct) => inner.HasFaceAsync(employeeNo, ct);
    public Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct) => inner.DownloadFaceAsync(employeeNo, ct);
    public Task DeleteFaceAsync(string employeeNo, CancellationToken ct) => inner.DeleteFaceAsync(employeeNo, ct);
    public Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct) => inner.CaptureFingerprintAsync(fingerId, ct);
    public Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct) =>
        inner.SetFingerprintAsync(employeeNo, fingerId, templateBase64, readerNo, ct);
    public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) => inner.ReadFingerprintsAsync(employeeNo, ct);
    public Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct) => inner.DeleteFingerprintAsync(employeeNo, fingerId, ct);
}

public sealed class ThrowingFactory : IHikDeviceFactory
{
    public volatile bool Fail;
    private readonly FakeDevice _device = new();
    public IHikDevice Create(DeviceRecord d) => Fail ? throw new InvalidOperationException("factory boom") : _device;
}
