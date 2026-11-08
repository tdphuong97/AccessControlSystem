using Acs.Core.Hik;
using Acs.Core.Probe;
using Acs.Core.Tests.Fakes;

namespace Acs.Core.Tests.Hik;

public class ProbeRunnerTests
{
    private static HikUserWrite U1001 => new("1001", "Nguyen Van A", "2026-01-01T00:00:00", "2030-01-01T00:00:00", true, 1);

    private static async Task<(FakeDevice main, FakeDevice peer, ProbeReport report, bool ok, List<string> prompts)> Run(
        Action<FakeDevice, FakeDevice>? setup = null, bool withPeer = true)
    {
        var main = new FakeDevice(); var peer = new FakeDevice();
        await main.UpsertUserAsync(U1001, default); await peer.UpsertUserAsync(U1001, default);
        setup?.Invoke(main, peer);
        var report = new ProbeReport(["s3cret"]);
        var prompts = new List<string>();
        var runner = new ProbeRunner(main, withPeer ? peer : null, p => { prompts.Add(p); return Task.CompletedTask; }, report);
        var ok = await runner.RunAsync(default);
        return (main, peer, report, ok, prompts);
    }

    [Fact]
    public async Task Probe_touches_only_99999()
    {
        var (main, peer, report, ok, prompts) = await Run();
        Assert.True(ok, report.Text);
        foreach (var d in new[] { main, peer })
        {
            Assert.Null(await d.FindUserAsync("99999", default));
            var u = await d.FindUserAsync("1001", default);
            Assert.NotNull(u);
            Assert.Equal("Nguyen Van A", u!.Name);
            Assert.Equal(0, u.FaceCount);
            Assert.Equal(0, u.FingerCount);
            Assert.DoesNotContain(d.Calls, c => c is nameof(IHikDevice.DeleteFaceAsync) or nameof(IHikDevice.DeleteFingerprintAsync));
        }
        Assert.DoesNotContain("1001", report.Text);
        Assert.Equal(2, prompts.Count);
        Assert.All(prompts, p => Assert.Contains("Enter", p));
        Assert.Contains("OK", report.Text);
    }

    [Fact]
    public async Task Probe_cleans_up_after_failure()
    {
        var (main, peer, report, ok, _) = await Run((m, _) => m.RejectFace = true);
        Assert.False(ok);
        Assert.Contains("LỖI", report.Text);
        Assert.Null(await main.FindUserAsync("99999", default));
        Assert.Null(await peer.FindUserAsync("99999", default));
        Assert.NotNull(await main.FindUserAsync("1001", default));
    }

    [Fact]
    public async Task Probe_attempts_and_reports_cleanup_when_device_goes_offline()
    {
        var main = new FakeDevice();
        var report = new ProbeReport();
        var runner = new ProbeRunner(main, null, _ => { main.Offline = true; return Task.CompletedTask; }, report);
        Assert.False(await runner.RunAsync(default));
        Assert.Contains("LỖI", report.Text);
        // the device is unreachable, so cleanup cannot succeed, but it must be attempted and reported (not thrown)
        Assert.Contains(nameof(IHikDevice.DeleteUserAsync), main.Calls);
        Assert.Contains("dọn dẹp", report.Text);
    }

    [Fact]
    public async Task Probe_refuses_when_99999_belongs_to_someone_else()
    {
        var main = new FakeDevice();
        await main.UpsertUserAsync(new("99999", "Real Person", "a", "b", true, 1), default);
        var report = new ProbeReport();
        Assert.False(await new ProbeRunner(main, null, _ => Task.CompletedTask, report).RunAsync(default));
        Assert.Equal("Real Person", (await main.FindUserAsync("99999", default))!.Name);
    }

    [Fact]
    public async Task Probe_fails_when_writes_are_silently_dropped_on_main()
    {
        var (main, _, report, ok, _) = await Run((m, _) => m.ForgetWrites = true, withPeer: false);
        Assert.False(ok);
        foreach (var step in new[] { "đọc lại người thử", "kiểm tra có khuôn mặt", "đọc lại vân tay" })
            Assert.Contains(report.Text.Split('\n'), l => l.Contains(step) && l.StartsWith("[LỖI]"));
        Assert.Null(await main.FindUserAsync("99999", default));
    }

    /// <summary>Test-local wrapper that can drop one kind of write or throw on cleanup.</summary>
    private sealed class FlakyDevice(FakeDevice inner) : IHikDevice
    {
        public bool DropFingerprint, DropFace, DeleteThrowsCancel;
        public int DeleteCalls;
        public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct) => inner.GetDeviceInfoAsync(ct);
        public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => inner.ListUsersAsync(ct);
        public Task<HikUser?> FindUserAsync(string no, CancellationToken ct) => inner.FindUserAsync(no, ct);
        public Task UpsertUserAsync(HikUserWrite u, CancellationToken ct) => inner.UpsertUserAsync(u, ct);
        public Task DeleteUserAsync(string no, CancellationToken ct)
        {
            DeleteCalls++;
            if (DeleteThrowsCancel) throw new OperationCanceledException("timeout");
            return inner.DeleteUserAsync(no, ct);
        }
        public Task<byte[]> CaptureFaceAsync(CancellationToken ct) => inner.CaptureFaceAsync(ct);
        public Task<byte[]> SnapshotAsync(CancellationToken ct) => inner.SnapshotAsync(ct);
        public Task SetFaceAsync(string no, byte[] j, CancellationToken ct) => DropFace ? Task.CompletedTask : inner.SetFaceAsync(no, j, ct);
        public Task<bool> HasFaceAsync(string no, CancellationToken ct) => inner.HasFaceAsync(no, ct);
        public Task<byte[]?> DownloadFaceAsync(string no, CancellationToken ct) => inner.DownloadFaceAsync(no, ct);
        public Task DeleteFaceAsync(string no, CancellationToken ct) => inner.DeleteFaceAsync(no, ct);
        public Task<CapturedFinger> CaptureFingerprintAsync(int id, CancellationToken ct) => inner.CaptureFingerprintAsync(id, ct);
        public Task SetFingerprintAsync(string no, int id, string t, int r, CancellationToken ct) => DropFingerprint ? Task.CompletedTask : inner.SetFingerprintAsync(no, id, t, r, ct);
        public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string no, CancellationToken ct) => inner.ReadFingerprintsAsync(no, ct);
        public Task DeleteFingerprintAsync(string no, int id, CancellationToken ct) => inner.DeleteFingerprintAsync(no, id, ct);
    }

    private static string[] PeerLine(ProbeReport r) => r.Text.Split('\n').Where(l => l.Contains("Máy phụ: kiểm tra lại")).ToArray();

    [Fact]
    public async Task Probe_fails_when_peer_drops_only_the_template()
    {
        var peerInner = new FakeDevice();
        var report = new ProbeReport();
        var ok = await new ProbeRunner(new FakeDevice(), new FlakyDevice(peerInner) { DropFingerprint = true }, _ => Task.CompletedTask, report).RunAsync(default);
        Assert.False(ok);
        var line = Assert.Single(PeerLine(report));
        Assert.StartsWith("[LỖI]", line);
        Assert.Contains("KHÔNG khớp mẫu", line);
        Assert.DoesNotContain("KHÔNG thấy", line);
    }

    [Fact]
    public async Task Probe_fails_when_peer_drops_only_the_face()
    {
        var report = new ProbeReport();
        var ok = await new ProbeRunner(new FakeDevice(), new FlakyDevice(new FakeDevice()) { DropFace = true }, _ => Task.CompletedTask, report).RunAsync(default);
        Assert.False(ok);
        var line = Assert.Single(PeerLine(report));
        Assert.StartsWith("[LỖI]", line);
        Assert.Contains("KHÔNG có khuôn mặt", line);
        Assert.DoesNotContain("KHÔNG thấy", line);
    }

    [Fact]
    public async Task Cleanup_on_peer_still_runs_when_main_cleanup_throws_cancellation()
    {
        var mainInner = new FakeDevice(); var peerInner = new FakeDevice();
        var main = new FlakyDevice(mainInner) { DeleteThrowsCancel = true };
        var report = new ProbeReport();
        var ok = await new ProbeRunner(main, peerInner, _ => Task.CompletedTask, report).RunAsync(default);
        Assert.False(ok);
        Assert.Equal(1, main.DeleteCalls);
        Assert.Contains(peerInner.Calls, c => c == nameof(IHikDevice.DeleteUserAsync));
        Assert.Null(await peerInner.FindUserAsync("99999", default));
        Assert.Contains("Máy chính: xoá người thử 99999 (dọn dẹp)", report.Text);
    }

    [Fact]
    public async Task Report_never_contains_a_person_name()
    {
        var main = new FakeDevice();
        await main.UpsertUserAsync(U1001, default);
        var report = new ProbeReport();
        await new ProbeRunner(main, null, _ => Task.CompletedTask, report).RunAsync(default);
        Assert.DoesNotContain("Nguyen", report.Text);
        Assert.DoesNotContain("name=", report.Text);
    }

    [Fact]
    public async Task Probe_cancelled_in_precheck_reports_and_writes_nothing()
    {
        var main = new FakeDevice();
        var report = new ProbeReport();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var ok = await new ProbeRunner(main, null, _ => Task.CompletedTask, report).RunAsync(cts.Token);
        Assert.False(ok);
        Assert.Contains("Huỷ", report.Text);
        Assert.DoesNotContain(main.Calls, c => c == nameof(IHikDevice.UpsertUserAsync));
    }

    [Fact]
    public async Task Probe_abort_line_does_not_leak_the_other_persons_name()
    {
        var main = new FakeDevice();
        await main.UpsertUserAsync(new("99999", "Real Person", "a", "b", true, 1), default);
        var report = new ProbeReport();
        await new ProbeRunner(main, null, _ => Task.CompletedTask, report).RunAsync(default);
        Assert.DoesNotContain("Real Person", report.Text);
        Assert.Contains("thuộc về một người khác", report.Text);
    }

    [Fact]
    public void Report_masks_passwords_and_limits_detail()
    {
        var r = new ProbeReport(["hunter2"]);
        r.Step("x hunter2", false, 5, "pw=hunter2 " + new string('a', 800));
        Assert.DoesNotContain("hunter2", r.Text);
        Assert.Contains("***", r.Text);
        Assert.True(r.Text.Length < 620);
        Assert.Contains("LỖI", r.Text);
    }

    [Fact]
    public async Task Peer_part_runs_only_with_peer()
    {
        var (_, peer, _, _, _) = await Run(withPeer: false);
        Assert.DoesNotContain(peer.Calls, c => c == nameof(IHikDevice.SetFaceAsync));
        Assert.DoesNotContain(peer.Calls, c => c == nameof(IHikDevice.DeleteUserAsync));
    }
}
