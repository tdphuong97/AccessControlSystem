using Acs.Core.Hik;

namespace Acs.Core.Tests.Fakes;

public sealed class FakeDeviceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static HikUserWrite U(string no = "1001") => new(no, "An", "2026-01-01T00:00:00", "2030-01-01T00:00:00", true, 1);

    [Fact]
    public async Task Fake_round_trips_user_face_finger()
    {
        var d = new FakeDevice();
        await d.UpsertUserAsync(U(), Ct);
        await d.SetFaceAsync("1001", [1, 2, 3], Ct);
        await d.SetFingerprintAsync("1001", 2, "ModelA:XX", 1, Ct);
        var u = await d.FindUserAsync("1001", Ct);
        Assert.Equal(new HikUser("1001", "An", "2026-01-01T00:00:00", "2030-01-01T00:00:00", true, 1, 1), u);
        Assert.True(await d.HasFaceAsync("1001", Ct));
        Assert.Equal(new byte[] { 1, 2, 3 }, await d.DownloadFaceAsync("1001", Ct));
        var fp = Assert.Single(await d.ReadFingerprintsAsync("1001", Ct));
        Assert.Equal(new HikFinger(2, "ModelA:XX"), fp);
        var all = new List<HikUser>();
        await foreach (var x in d.ListUsersAsync(Ct)) all.Add(x);
        Assert.Single(all);
        await d.DeleteFingerprintAsync("1001", 2, Ct);
        await d.DeleteFaceAsync("1001", Ct);
        var after = (await d.FindUserAsync("1001", Ct))!;
        Assert.Equal(0, after.FaceCount + after.FingerCount);
        await d.DeleteUserAsync("1001", Ct);
        Assert.Null(await d.FindUserAsync("1001", Ct));
        Assert.Contains("UpsertUserAsync", d.Calls);
    }

    [Fact]
    public async Task Offline_throws_transient()
    {
        var d = new FakeDevice { Offline = true };
        await Assert.ThrowsAsync<HikTransientException>(() => d.GetDeviceInfoAsync(Ct));
        await Assert.ThrowsAsync<HikTransientException>(() => d.UpsertUserAsync(U(), Ct));
    }

    [Fact]
    public async Task WrongPassword_throws_auth()
    {
        var d = new FakeDevice { WrongPassword = true };
        await Assert.ThrowsAsync<HikAuthException>(() => d.FindUserAsync("1", Ct));
    }

    [Fact]
    public async Task RejectFace_throws_rejected_with_code()
    {
        var d = new FakeDevice { RejectFace = true };
        var ex = await Assert.ThrowsAsync<HikRejectedException>(() => d.SetFaceAsync("1", [1], Ct));
        Assert.Equal("faceModelingFailed", ex.Code);
    }

    [Fact]
    public async Task AcceptOnlyGroup_rejects_other_group()
    {
        var d = new FakeDevice { FpGroup = "ModelA", AcceptOnlyGroup = true };
        await d.SetFingerprintAsync("1", 1, "ModelA:AAAA", 1, Ct);
        var ex = await Assert.ThrowsAsync<HikRejectedException>(() => d.SetFingerprintAsync("1", 2, "ModelB:AAAA", 1, Ct));
        Assert.Equal("fpMismatch", ex.Code);
        var open = new FakeDevice { FpGroup = "ModelA" };
        await open.SetFingerprintAsync("1", 1, "ModelB:AAAA", 1, Ct);
    }

    [Fact]
    public async Task ForgetWrites_stores_nothing()
    {
        var d = new FakeDevice { ForgetWrites = true };
        await d.UpsertUserAsync(U(), Ct);
        await d.SetFaceAsync("1001", [1], Ct);
        await d.SetFingerprintAsync("1001", 1, "ModelA:A", 1, Ct);
        Assert.Null(await d.FindUserAsync("1001", Ct));
        Assert.False(await d.HasFaceAsync("1001", Ct));
        Assert.Empty(await d.ReadFingerprintsAsync("1001", Ct));
    }

    [Fact]
    public async Task Capture_returns_next_values_and_tracks_concurrency()
    {
        var d = new FakeDevice { NextFace = [9], NextFinger = new("ModelA:Z", 70), CaptureDelay = TimeSpan.FromMilliseconds(150) };
        Assert.Equal(new byte[] { 9 }, await d.CaptureFaceAsync(Ct));
        Assert.Equal(1, d.ConcurrentCaptures);
        await Task.WhenAll(d.CaptureFingerprintAsync(1, Ct), d.CaptureFingerprintAsync(2, Ct));
        Assert.Equal(2, d.ConcurrentCaptures);
        Assert.Equal(70, (await d.CaptureFingerprintAsync(1, Ct)).Quality);
    }

    [Fact]
    public async Task Factory_maps_device_id_and_testdb_works()
    {
        var f = new FakeDeviceFactory();
        var rec = new Acs.Core.Model.DeviceRecord(5, "n", "h", "u", "p", 1, false, false, null, null, null, "g", false, null);
        Assert.Same(f.Create(rec), f.For(5));
        await ((FakeDevice)f.Create(rec)).GetDeviceInfoAsync(Ct);
        using var db = TestDb.Create();
        Assert.NotNull(db.Devices);
    }
}
