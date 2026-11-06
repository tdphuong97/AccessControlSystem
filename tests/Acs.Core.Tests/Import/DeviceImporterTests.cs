using Acs.Core.Hik;
using Acs.Core.Import;
using Acs.Core.Model;
using Acs.Core.Sync;
using Acs.Core.Tests.Fakes;
using Acs.Core.Tests.Sync;

namespace Acs.Core.Tests.Import;

public sealed class DeviceImporterTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    private readonly DeviceImporter _imp;
    private readonly int _d1;
    private readonly FakeDevice _dev;
    private static readonly CancellationToken Ct = CancellationToken.None;

    public DeviceImporterTests()
    {
        _d1 = _h.AddDevice("Máy tầng trệt", "ModelA");
        _dev = _h.Factory.For(_d1);
        _imp = new DeviceImporter(_h.Db, _h.Factory, _h.Time);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private async Task Seed(string no, string name, bool enabled = true, bool face = false, params int[] fingers)
    {
        await _dev.UpsertUserAsync(new HikUserWrite(no, name, "2026-03-01T00:00:00", "2027-02-28T23:59:59", enabled, 1), Ct);
        if (face) await _dev.SetFaceAsync(no, [0xFF, 0xD8, 0xFF, 0xD9], Ct);
        foreach (var f in fingers) await _dev.SetFingerprintAsync(no, f, SyncHarness.Tpl("ModelA", "D" + f), 1, Ct);
    }

    [Fact]
    public async Task Import_creates_people_faces_fingers_and_locks_disabled_users()
    {
        await Seed("1001", "An", true, true, 1, 2);
        await Seed("1002", "Bình", false);
        var progress = new List<ImportProgress>();
        var s = await _imp.ImportAllAsync(_d1, new Collect(progress), Ct);

        Assert.Equal(new ImportSummary(2, 1, 0, 2), s);
        var an = _h.Db.People.Get("1001")!;
        Assert.Equal(PersonStatus.Active, an.Status);
        Assert.Equal(new DateOnly(2026, 3, 1), an.ValidFrom);
        Assert.Equal(new DateOnly(2027, 2, 28), an.ValidTo);
        Assert.Equal(PersonStatus.Locked, _h.Db.People.Get("1002")!.Status);
        Assert.Equal(BiometricSource.Imported, _h.Db.Biometrics.GetFace("1001")!.Source);
        var fp = _h.Db.Biometrics.GetFingers("1001");
        Assert.All(fp, f => { Assert.Equal("ModelA", f.FpGroup); Assert.Equal(BiometricSource.Imported, f.Source); });
        Assert.Equal(2, fp.Count);
        var st = _h.Db.Jobs.GetState("1001", _d1)!;
        Assert.True(st.UserPresent && st.UserEnabled && st.FacePresent);
        Assert.Equal(new HashSet<int> { 1, 2 }, st.Fingers);
        Assert.False(_h.Db.Jobs.GetState("1002", _d1)!.UserEnabled);
        Assert.Equal(2, progress.Last().Done);
        Assert.Empty(_h.Db.Jobs.ListOpen(_d1));
    }

    [Fact]
    public async Task Import_twice_adds_nothing()
    {
        await Seed("1001", "An", true, true, 1, 2);
        await _imp.ImportAllAsync(_d1, null, Ct);
        var face = _h.Db.Biometrics.GetFace("1001")!;
        var s2 = await _imp.ImportAllAsync(_d1, null, Ct);
        Assert.Equal(new ImportSummary(0, 0, 0, 0), s2);
        Assert.Equal(2, _h.Db.Biometrics.GetFingers("1001").Count);
        Assert.Equal(face.CapturedAt, _h.Db.Biometrics.GetFace("1001")!.CapturedAt);
        Assert.Single(_h.Db.People.List());
    }

    [Fact]
    public async Task Import_never_overwrites_existing_people_but_fills_gaps()
    {
        _h.AddPerson("1001", "Tên trong app");
        _h.AddFinger("1001", 1, "ModelB", "KEEP");
        await Seed("1001", "Tên trên máy", true, true, 1, 2);
        var s = await _imp.ImportAllAsync(_d1, null, Ct);
        Assert.Equal(new ImportSummary(0, 1, 0, 1), s);
        Assert.Equal("Tên trong app", _h.Db.People.Get("1001")!.FullName);
        var fp = _h.Db.Biometrics.GetFingers("1001");
        Assert.Equal(2, fp.Count);
        Assert.Equal("ModelB", fp.Single(f => f.FingerId == 1).FpGroup);
        Assert.Equal("ModelA", fp.Single(f => f.FingerId == 2).FpGroup);
    }

    [Fact]
    public async Task Import_counts_faces_device_will_not_give()
    {
        await Seed("1001", "An", true, true);
        var wrapped = new NoPhotoDevice(_dev);
        var imp = new DeviceImporter(_h.Db, new SingleDeviceFactory(wrapped), _h.Time);
        var s = await imp.ImportAllAsync(_d1, null, Ct);
        Assert.Equal(1, s.People);
        Assert.Equal(0, s.WithFace);
        Assert.Equal(1, s.FacesMissing);
        Assert.Null(_h.Db.Biometrics.GetFace("1001"));
    }

    [Fact]
    public async Task Import_rejects_non_jpeg_and_oversized_faces()
    {
        await Seed("1001", "An", true, true);
        await Seed("1002", "Bình", true, true);
        var bad = new FaceOverride(_dev, no => no == "1001" ? [1, 2, 3, 4] : new byte[200 * 1024 + 1].Select((_, i) => i < 3 ? (byte)(i == 0 ? 0xFF : i == 1 ? 0xD8 : 0xFF) : (byte)0).ToArray());
        var s = await new DeviceImporter(_h.Db, new SingleDeviceFactory(bad), _h.Time).ImportAllAsync(_d1, null, Ct);
        Assert.Equal(new ImportSummary(2, 0, 2, 0), s);
    }

    private sealed class Collect(List<ImportProgress> l) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress v) => l.Add(v);
    }

    private class FaceOverride(FakeDevice inner, Func<string, byte[]?> face) : IHikDevice
    {
        public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) => inner.GetDeviceInfoAsync(ct);
        public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => inner.ListUsersAsync(ct);
        public Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct) => inner.FindUserAsync(employeeNo, ct);
        public Task UpsertUserAsync(HikUserWrite user, CancellationToken ct) => inner.UpsertUserAsync(user, ct);
        public Task DeleteUserAsync(string employeeNo, CancellationToken ct) => inner.DeleteUserAsync(employeeNo, ct);
        public Task<byte[]> CaptureFaceAsync(CancellationToken ct) => inner.CaptureFaceAsync(ct);
        public Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct) => inner.SetFaceAsync(employeeNo, jpeg, ct);
        public Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct) => inner.HasFaceAsync(employeeNo, ct);
        public Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct) => Task.FromResult(face(employeeNo));
        public Task DeleteFaceAsync(string employeeNo, CancellationToken ct) => inner.DeleteFaceAsync(employeeNo, ct);
        public Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct) => inner.CaptureFingerprintAsync(fingerId, ct);
        public Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct) =>
            inner.SetFingerprintAsync(employeeNo, fingerId, templateBase64, readerNo, ct);
        public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) => inner.ReadFingerprintsAsync(employeeNo, ct);
        public Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct) => inner.DeleteFingerprintAsync(employeeNo, fingerId, ct);
    }

    private sealed class NoPhotoDevice(FakeDevice inner) : FaceOverride(inner, _ => null);
}
