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
        main.Calls.ToList(); // snapshot not needed
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
