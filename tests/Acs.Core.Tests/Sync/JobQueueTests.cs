using Acs.Core.Model;
using Acs.Core.Sync;

namespace Acs.Core.Tests.Sync;

public sealed class JobQueueTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public void Backoff_sequence_is_capped_at_900()
    {
        int[] want = [30, 60, 120, 300, 600, 900, 900, 900];
        for (var i = 0; i < want.Length; i++) Assert.Equal(TimeSpan.FromSeconds(want[i]), Backoff.After(i));
    }

    [Fact]
    public void Enqueue_upserts_and_resets_the_open_job()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.SetFace);
        var j = _h.Job(d, "1001", JobKind.SetFace);
        _h.Db.Jobs.MarkRunning(j.Id, _h.Now);
        _h.Db.Jobs.Fail(j.Id, "rejected", "x", "y", _h.Now);
        _h.Time.Advance(TimeSpan.FromMinutes(5));
        _h.Enqueue(d, "1001", JobKind.SetFace);
        var again = Assert.Single(_h.Db.Jobs.ListOpen(d));
        Assert.Equal(j.Id, again.Id);
        Assert.Equal(JobStatus.Pending, again.Status);
        Assert.Equal(0, again.Attempts);
        Assert.Equal(_h.Now, again.NextAt);
        Assert.Null(again.ErrorKind);
        Assert.Null(again.ErrorMessage);
    }

    [Fact]
    public void DeleteUser_supersedes_open_jobs()
    {
        var d = _h.AddDevice();
        var other = _h.AddDevice("Máy tầng 1");
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Enqueue(d, "1001", JobKind.SetFace);
        _h.Enqueue(d, "1001", JobKind.SetFinger, 2);
        _h.Enqueue(d, "1002", JobKind.SetFace);
        _h.Enqueue(other, "1001", JobKind.SetFace);
        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        var open = _h.Db.Jobs.ListOpen(d);
        Assert.Equal(2, open.Count);
        Assert.Contains(open, j => j.EmployeeNo == "1001" && j.Kind == JobKind.DeleteUser);
        Assert.Contains(open, j => j.EmployeeNo == "1002" && j.Kind == JobKind.SetFace);
        Assert.Single(_h.Db.Jobs.ListOpen(other));
    }

    [Fact]
    public void UpsertUser_removes_open_delete_and_disable()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        _h.Enqueue(d, "1001", JobKind.DisableUser);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        Assert.Equal(JobKind.UpsertUser, Assert.Single(_h.Db.Jobs.ListOpen(d)).Kind);
    }

    [Fact]
    public void NextDue_orders_by_rank_then_created_and_blocks_behind_open_upsert()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.SetFinger, 3);
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        _h.Enqueue(d, "1001", JobKind.SetFace);
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        _h.Enqueue(d, "1002", JobKind.DeleteFace);
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        _h.Enqueue(d, "1003", JobKind.DeleteUser);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);   // blocks the person's face and finger jobs

        var order = new List<(string, JobKind)>();
        while (_h.Db.Jobs.NextDue(d, _h.Now) is { } j)
        {
            order.Add((j.EmployeeNo, j.Kind));
            _h.Db.Jobs.MarkRunning(j.Id, _h.Now);
            _h.Db.Jobs.MarkDone(j.Id, _h.Now);
        }
        Assert.Equal([("1003", JobKind.DeleteUser), ("1001", JobKind.UpsertUser), ("1001", JobKind.SetFace), ("1002", JobKind.DeleteFace), ("1001", JobKind.SetFinger)], order);
    }

    [Fact]
    public void NextDue_waits_for_next_at_and_failed_upsert_keeps_blocking()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Enqueue(d, "1001", JobKind.SetFace);
        var up = _h.Job(d, "1001", JobKind.UpsertUser);
        _h.Db.Jobs.MarkRunning(up.Id, _h.Now);
        _h.Db.Jobs.Retry(up.Id, _h.Now.AddSeconds(30), "transient", "m", null, _h.Now);
        Assert.Null(_h.Db.Jobs.NextDue(d, _h.Now));
        Assert.Equal(_h.Now.AddSeconds(30), _h.Db.Jobs.NextFuture(d, _h.Now));
        Assert.Equal(1, _h.Job(d, "1001", JobKind.UpsertUser).Attempts);
        _h.Db.Jobs.MarkRunning(up.Id, _h.Now);
        _h.Db.Jobs.Fail(up.Id, "rejected", "m", null, _h.Now);
        _h.Time.Advance(TimeSpan.FromHours(1));
        Assert.Null(_h.Db.Jobs.NextDue(d, _h.Now));
        Assert.Null(_h.Db.Jobs.NextFuture(d, _h.Now));
        Assert.Equal((1, 1), _h.Db.Jobs.Counts(d));
    }

    [Fact]
    public void MarkDone_ignored_when_job_was_reenqueued_while_running()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.SetFace);
        var j = _h.Job(d, "1001", JobKind.SetFace);
        _h.Db.Jobs.MarkRunning(j.Id, _h.Now);
        _h.Enqueue(d, "1001", JobKind.SetFace);
        Assert.False(_h.Db.Jobs.MarkDone(j.Id, _h.Now));
        Assert.Equal(JobStatus.Pending, _h.Db.Jobs.Get(j.Id)!.Status);
    }
}
