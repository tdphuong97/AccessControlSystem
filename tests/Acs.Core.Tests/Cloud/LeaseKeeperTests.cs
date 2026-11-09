using Acs.Core.Cloud;
using Acs.Core.Model;
using Acs.Core.Sync;
using Acs.Core.Tests.Sync;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Cloud;

public sealed class LeaseKeeperTests
{
    private readonly FakeCloud _cloud = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero));
    private readonly byte[] _k = CloudCrypto.NewKey();

    public LeaseKeeperTests() => _cloud.NowMs = () => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private LeaseKeeper Keeper(string pc, string name) =>
        new(new CloudClient(_cloud.Client(), CloudCode.Parse("acs1:acs.test:" + FakeCloud.Key)), _k, pc, name, _time);

    [Fact]
    public async Task first_pc_gets_the_lease()
    {
        var a = Keeper("pcA", "NHIEN-PC");
        Assert.True(await a.TickAsync(false, default));
        Assert.Equal("NHIEN-PC", a.HolderName);
    }

    [Fact]
    public async Task tray_pc_yields_to_pc_with_open_window()
    {
        var a = Keeper("pcA", "NHIEN-PC");
        var b = Keeper("pcB", "HUONG-PC");
        Assert.True(await a.TickAsync(false, default));
        Assert.False(await b.TickAsync(false, default));
        Assert.Equal("NHIEN-PC", b.HolderName);
        Assert.True(await b.TickAsync(true, default));
        Assert.False(await a.TickAsync(false, default));
        Assert.Equal("HUONG-PC", a.HolderName);
    }

    [Fact]
    public async Task lease_moves_when_holder_stops_renewing_for_90s()
    {
        var a = Keeper("pcA", "NHIEN-PC");
        var b = Keeper("pcB", "HUONG-PC");
        Assert.True(await a.TickAsync(true, default));
        Assert.False(await b.TickAsync(false, default));
        _time.Advance(TimeSpan.FromSeconds(91));
        Assert.True(await b.TickAsync(false, default));
    }

    [Fact]
    public async Task offline_more_than_2_minutes_runs_locally()
    {
        var b = Keeper("pcB", "HUONG-PC");
        Assert.True(await Keeper("pcA", "NHIEN-PC").TickAsync(true, default));
        Assert.False(await b.TickAsync(false, default));
        _cloud.Offline = true;
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.False(await b.TickAsync(false, default));   // keeps the last answer for a while
        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(await b.TickAsync(false, default));    // > 2 min without the cloud: send on its own
    }

    [Fact]
    public async Task label_is_sealed_on_the_server()
    {
        await Keeper("pcA", "NHIEN-PC").TickAsync(false, default);
        Assert.DoesNotContain("NHIEN", _cloud.Lease!.Value.Label);
    }

    [Fact]
    public async Task release_frees_the_lease()
    {
        var a = Keeper("pcA", "NHIEN-PC");
        await a.TickAsync(true, default);
        await a.ReleaseAsync(default);
        Assert.True(await Keeper("pcB", "HUONG-PC").TickAsync(false, default));
    }
}

public sealed class SyncEngineSenderTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();

    [Fact]
    public async Task SetSenderAsync_false_stops_workers_and_reports_message()
    {
        var d = _h.AddDevice();
        _h.Engine.Start();
        await _h.Engine.SetSenderAsync(false);
        var s = _h.Engine.Status().Single(x => x.DeviceId == d);
        Assert.Equal(SyncEngine.OtherSenderMessage, s.Message);
        Assert.False(s.Online);
        Assert.False(_h.Engine.IsSender);
    }

    [Fact]
    public async Task SetSenderAsync_true_resets_running_jobs()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Db.Exec("UPDATE jobs SET status='running'");
        await _h.Engine.SetSenderAsync(false);
        await _h.Engine.SetSenderAsync(true);
        Assert.True(_h.Engine.IsSender);
        Assert.NotEqual(JobStatus.Running, _h.Job(d, "1001", JobKind.UpsertUser).Status);
        Assert.NotEqual(SyncEngine.OtherSenderMessage, _h.Engine.Status().Single().Message);
    }

    public async ValueTask DisposeAsync() => await _h.DisposeAsync();
}
