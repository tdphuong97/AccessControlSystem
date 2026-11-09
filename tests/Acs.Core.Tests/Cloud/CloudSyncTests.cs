using System.Text;
using Acs.Core.Cloud;
using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Cloud;

public sealed class CloudSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acs-cs-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCloud _cloud = new();
    private readonly byte[] _k = CloudCrypto.NewKey();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 7, 0, 0, TimeSpan.Zero));
    private readonly Database _nhien, _huong;
    private readonly CloudSync _a, _b;
    private static DateTime Now => new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);

    public CloudSyncTests()
    {
        _nhien = new Database(Path.Combine(_dir, "n.db"), new DataProtector(CloudCrypto.NewKey()));
        _huong = new Database(Path.Combine(_dir, "h.db"), new DataProtector(CloudCrypto.NewKey()));
        var code = CloudCode.Parse("acs1:acs.test:" + FakeCloud.Key);
        _a = new CloudSync(_nhien, new CloudClient(_cloud.Client(), code), _k, "pcA", "NHIEN-PC", _time);
        _b = new CloudSync(_huong, new CloudClient(_cloud.Client(), code), _k, "pcB", "HUONG-PC", _time);
    }

    public void Dispose() { _nhien.Dispose(); _huong.Dispose(); SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static PersonRecord Person(string no, string name = "Nguyễn Văn A") =>
        new(no, name, null, new DateOnly(2026, 1, 1), null, PersonStatus.Active, Now, Now, null);
    private static DeviceRecord Dev(string group) => new(0, "Trệt", "10.9.0.84", "admin", "Secret#1", 1, true, false, null, null, null, group, false, null);

    private void Seed(Database db)
    {
        var dev = db.Devices.Insert(Dev("device-1"));
        foreach (var no in new[] { "101", "102", "103" })
        {
            db.People.Insert(Person(no, "Người " + no));
            db.Biometrics.SetFace(new FaceRecord(no, [0xFF, 0xD8, 0xFF, (byte)no[2], 0xFF, 0xD9], BiometricSource.Device, Now));
            db.Biometrics.SetFinger(new FingerRecord(no, 1, "device-1", Convert.ToBase64String([1, 2, (byte)no[2]]), null, BiometricSource.Device, Now));
        }
        db.Jobs.Enqueue(dev, "101", JobKind.SetFace, 0, Now);
    }

    private async Task PublishAndJoin()
    {
        Seed(_nhien);
        await _a.PublishAllAsync(null, default);
        _huong.People.Insert(Person("999", "Cũ trên máy Hương"));
        Assert.Equal(3, await _b.ReplaceLocalFromCloudAsync(null, default));
    }

    [Fact]
    public async Task publish_then_join_gives_identical_people_faces_fingers()
    {
        await PublishAndJoin();
        Assert.Equal(_nhien.People.List().Select(p => (p.EmployeeNo, p.FullName)), _huong.People.List().Select(p => (p.EmployeeNo, p.FullName)));
        Assert.Null(_huong.People.Get("999"));
        foreach (var no in new[] { "101", "102", "103" })
        {
            Assert.Equal(_nhien.Biometrics.GetFace(no)!.Jpeg, _huong.Biometrics.GetFace(no)!.Jpeg);
            Assert.Equal(_nhien.Biometrics.GetFingers(no).Single().TemplateBase64, _huong.Biometrics.GetFingers(no).Single().TemplateBase64);
        }
        Assert.Equal(1L, _huong.Scalar("SELECT COUNT(*) FROM jobs"));
        Assert.True(_huong.Outbox.Capture);
        Assert.Equal(0, _huong.Outbox.Count());
        Assert.Equal(0, _nhien.Outbox.Count());
    }

    [Fact]
    public async Task edit_on_one_pc_reaches_the_other_after_push_and_pull()
    {
        await PublishAndJoin();
        var applied = new List<string>();
        _a.Applied += (t, k) => applied.Add(t + ":" + k);
        _huong.People.Update(_huong.People.Get("102")! with { FullName = "Đổi tên" });
        await _b.PushAsync(default);
        await _a.PullAsync(default);
        Assert.Equal("Đổi tên", _nhien.People.Get("102")!.FullName);
        Assert.Contains("people:102", applied);
        Assert.Equal(0, _nhien.Outbox.Count());   // applying a pulled row does not queue it again
    }

    [Fact]
    public async Task own_rows_are_not_applied_again()
    {
        await PublishAndJoin();
        var applied = 0;
        _a.Applied += (_, _) => applied++;
        await _a.PullAsync(default);
        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task later_edit_wins_and_overwrite_is_logged()
    {
        await PublishAndJoin();
        _nhien.People.Update(_nhien.People.Get("101")! with { FullName = "Nhiên sửa" });
        await _a.PushAsync(default);
        _huong.People.Update(_huong.People.Get("101")! with { FullName = "Hương sửa sau" });   // Hương has not pulled Nhiên's edit
        await _b.PushAsync(default);
        await _a.PullAsync(default);
        Assert.Equal("Hương sửa sau", _nhien.People.Get("101")!.FullName);
        Assert.Contains(_huong.Log.List(null, null, 20), e => e.Action == "Dữ liệu chung" && e.Message!.Contains("NHIEN-PC") && e.Message.Contains("101"));
    }

    [Fact]
    public async Task pending_local_change_survives_a_pull_and_wins_on_push()
    {
        await PublishAndJoin();
        _nhien.People.Update(_nhien.People.Get("103")! with { FullName = "Nhiên" });
        await _a.PushAsync(default);
        _huong.People.Update(_huong.People.Get("103")! with { FullName = "Hương" });
        await _b.PullAsync(default);   // Nhiên's row arrives while Hương's own change is still queued
        Assert.Equal("Hương", _huong.People.Get("103")!.FullName);
        await _b.PushAsync(default);
        await _a.PullAsync(default);
        Assert.Equal("Hương", _nhien.People.Get("103")!.FullName);
        Assert.Contains(_huong.Log.List(null, null, 20), e => e.Action == "Dữ liệu chung" && e.Message!.Contains("103"));
    }

    [Fact]
    public async Task delete_reaches_the_other_pc()
    {
        await PublishAndJoin();
        _huong.Biometrics.DeleteFace("102");
        _huong.People.Delete("102");
        await _b.PushAsync(default);
        await _a.PullAsync(default);
        Assert.Null(_nhien.People.Get("102"));
        Assert.Null(_nhien.Biometrics.GetFace("102"));
    }

    [Fact]
    public async Task renumber_leaves_no_stale_row()
    {
        await PublishAndJoin();
        _nhien.People.Insert(Person("120"));   // the app only renumbers people without face or finger yet
        await _a.PushAsync(default);
        await _b.PullAsync(default);
        _nhien.Exec("UPDATE people SET employee_no='121' WHERE employee_no='120'");
        await _a.PushAsync(default);
        await _b.PullAsync(default);
        Assert.Null(_huong.People.Get("120"));
        Assert.NotNull(_huong.People.Get("121"));
    }

    [Fact]
    public async Task concurrent_pushes_on_one_pc_do_not_log_an_overwrite()
    {
        Seed(_nhien);
        _nhien.Outbox.Capture = true;
        _nhien.Outbox.MarkAll();
        _cloud.Latency = TimeSpan.FromMilliseconds(30);
        await Task.WhenAll(_a.PushAsync(default), _a.PushAsync(default), _a.PublishAllAsync(null, default));
        Assert.Equal(0, _nhien.Outbox.Count());
        Assert.DoesNotContain(_nhien.Log.List(null, null, 50), e => e.Action == "Dữ liệu chung");
    }

    [Fact]
    public async Task change_made_during_a_push_stays_queued()
    {
        await PublishAndJoin();
        _nhien.People.Update(_nhien.People.Get("101")! with { FullName = "Một" });
        var taken = _nhien.Outbox.Take(10).Single();
        _nhien.People.Update(_nhien.People.Get("101")! with { FullName = "Hai" });   // recorded again: new seq
        _nhien.Outbox.RemoveIf(taken.Tbl, taken.Key, taken.Seq);
        Assert.Equal(1, _nhien.Outbox.Count());
    }

    [Fact]
    public async Task deferred_row_applies_on_next_pull()
    {
        await PublishAndJoin();
        // A device and its job written by Nhiên; Hương pulls the job page before the device exists here.
        var dev = _nhien.Devices.Insert(Dev("device-xy"));
        _nhien.Outbox.Clear();
        _nhien.Jobs.Enqueue(dev, "102", JobKind.UpsertUser, 0, Now);
        await _a.PushAsync(default);                                   // job only
        await _b.PullAsync(default);
        Assert.Equal(1, _huong.Outbox.Deferred().Count);
        _nhien.Devices.Update(_nhien.Devices.Get(dev)! with { Name = "Tầng 2" });
        await _a.PushAsync(default);                                   // now the device
        await _b.PullAsync(default);
        Assert.Empty(_huong.Outbox.Deferred());
        Assert.Equal(2L, _huong.Scalar("SELECT COUNT(*) FROM jobs"));
    }

    [Fact]
    public async Task push_splits_large_rows_into_small_batches()
    {
        for (var i = 0; i < 30; i++)
        {
            var no = (300 + i).ToString();
            _nhien.People.Insert(Person(no));
            _nhien.Biometrics.SetFace(new FaceRecord(no, [0xFF, 0xD8, 0xFF, .. new byte[100_000], 0xFF, 0xD9], BiometricSource.Device, Now));
        }
        await _a.PublishAllAsync(null, default);
        Assert.Equal(0, _nhien.Outbox.Count());
        Assert.All(_cloud.PushSizes, size => Assert.True(size < 1_500_000, "push body " + size));
        Assert.True(_cloud.PushSizes.Count >= 5);
    }

    [Fact]
    public async Task publish_rides_out_a_failing_push()
    {
        Seed(_nhien);
        _cloud.FailNextPushes = 2;
        await _a.PublishAllAsync(null, default);
        Assert.Equal(0, _nhien.Outbox.Count());
    }

    [Fact]
    public async Task offline_push_keeps_outbox_and_retries()
    {
        await PublishAndJoin();
        _nhien.People.Update(_nhien.People.Get("101")! with { FullName = "Offline" });
        _cloud.Offline = true;
        await Assert.ThrowsAsync<CloudOfflineException>(() => _a.PushAsync(default));
        Assert.Equal(1, _nhien.Outbox.Count());
        Assert.False(_a.Status.Online);
        _cloud.Offline = false;
        await _a.PushAsync(default);
        Assert.Equal(0, _nhien.Outbox.Count());
        Assert.True(_a.Status.Online);
    }

    [Fact]
    public async Task republish_makes_cloud_equal_local_including_deletes()
    {
        await PublishAndJoin();
        _huong.People.Insert(Person("200", "Mới sau sao lưu"));
        await _b.PushAsync(default);
        // Nhiên "restores" a backup without 200 and with 101 renamed: capture off, like a swapped database.
        _nhien.Outbox.Applying(() => { _nhien.People.Update(_nhien.People.Get("101")! with { FullName = "Bản sao lưu" }); return 0; });
        await _a.RepublishAsync(default);
        await _b.PullAsync(default);
        Assert.Null(_huong.People.Get("200"));
        Assert.Equal("Bản sao lưu", _huong.People.Get("101")!.FullName);
    }

    [Fact]
    public async Task server_never_sees_plaintext()
    {
        await PublishAndJoin();
        var needles = new[] { "101", "102", "Người", "NHIEN", "10.9.0.84", "Secret#1", "device-1" }.Select(Encoding.UTF8.GetBytes).ToList();
        needles.Add([0xFF, 0xD8, 0xFF]);
        foreach (var blob in _cloud.StoredBytes())
            foreach (var n in needles)
                Assert.True(blob.AsSpan().IndexOf(n) < 0, "found plaintext " + Encoding.UTF8.GetString(n));
    }
}
