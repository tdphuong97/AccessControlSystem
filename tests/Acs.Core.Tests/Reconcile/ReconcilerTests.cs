using Acs.Core.Hik;
using Acs.Core.Import;
using Acs.Core.Model;
using Acs.Core.Reconcile;
using Acs.Core.Sync;
using Acs.Core.Tests.Fakes;
using Acs.Core.Tests.Sync;

namespace Acs.Core.Tests.Reconcile;

public sealed class ReconcilerTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    private readonly Reconciler _rec;
    private readonly int _d1;
    private readonly FakeDevice _dev1;

    public ReconcilerTests()
    {
        _d1 = _h.AddDevice("Máy tầng trệt", "ModelA");
        _dev1 = _h.Factory.For(_d1);
        _rec = new Reconciler(_h.Db, _h.Factory, _h.Engine, new DeviceImporter(_h.Db, _h.Factory, _h.Time), _h.Time);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static HikUserWrite W(string no, string name, bool enabled = true) =>
        new(no, name, "2026-01-01T00:00:00", JobRules.NoEndDate, enabled, 1);

    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Plan_lists_missing_person_face_fingers()
    {
        _h.AddPerson("1001", "An");                      // absent on device
        _h.AddFinger("1001", 1, "ModelA");
        _h.AddFace("1001");
        _h.AddPerson("1002", "Bình"); _h.AddFace("1002"); // present, no face
        _h.AddFinger("1002", 1, "ModelA"); _h.AddFinger("1002", 2, "ModelA");
        await _dev1.UpsertUserAsync(W("1002", "Bình"), Ct);
        await _dev1.SetFingerprintAsync("1002", 1, SyncHarness.Tpl("ModelA"), 1, Ct);
        await _dev1.SetFingerprintAsync("1002", 9, SyncHarness.Tpl("ModelA"), 1, Ct); // device-only fingers
        await _dev1.SetFingerprintAsync("1002", 10, SyncHarness.Tpl("ModelA"), 1, Ct);

        var plan = await _rec.BuildAsync(null, Ct);
        var items = plan.Devices.Single().Items;
        var a = items.Single(i => i.EmployeeNo == "1001");
        Assert.Equal(DiffKind.MissingPerson, a.Kind);
        Assert.Equal([1], a.Fingers);
        Assert.Contains(items, i => i.EmployeeNo == "1002" && i.Kind == DiffKind.MissingFace);
        var mf = items.Single(i => i.EmployeeNo == "1002" && i.Kind == DiffKind.MissingFingers);
        Assert.Equal([2], mf.Fingers);
        var ef = items.Single(i => i.EmployeeNo == "1002" && i.Kind == DiffKind.ExtraFingers);
        Assert.Equal([9, 10], ef.Fingers);

        var st = _h.Db.Jobs.GetState("1002", _d1)!;
        Assert.True(st.UserPresent && st.UserEnabled && !st.FacePresent);
        Assert.Equal(new HashSet<int> { 1, 9, 10 }, st.Fingers);
        Assert.False(_h.Db.Jobs.GetState("1001", _d1)!.UserPresent);
    }

    [Fact]
    public async Task Info_mismatch_and_locked_but_enabled()
    {
        _h.AddPerson("1001", "Nguyễn An");
        await _dev1.UpsertUserAsync(W("1001", "Nguyen An"), Ct);
        _h.AddPerson("1002", "Bình", PersonStatus.Locked);
        await _dev1.UpsertUserAsync(W("1002", "Bình"), Ct);
        _h.AddPerson("1003", "Cường", PersonStatus.Locked);       // absent: fine
        _h.AddPerson("1004", "Dũng", PersonStatus.Locked);
        await _dev1.UpsertUserAsync(W("1004", "Dũng", false), Ct); // disabled: fine

        var items = (await _rec.BuildAsync(null, Ct)).Devices.Single().Items;
        Assert.Contains(items, i => i.EmployeeNo == "1001" && i.Kind == DiffKind.InfoMismatch);
        Assert.Contains(items, i => i.EmployeeNo == "1002" && i.Kind == DiffKind.LockedButEnabled);
        Assert.DoesNotContain(items, i => i.EmployeeNo is "1003" or "1004");

        await _rec.ApplyAsync(await _rec.BuildAsync(null, Ct), new Dictionary<string, StrangerChoice>(), Ct);
        var jobs = _h.Db.Jobs.ListOpen(_d1);
        Assert.Contains(jobs, j => j.EmployeeNo == "1001" && j.Kind == JobKind.UpsertUser);
        Assert.Contains(jobs, j => j.EmployeeNo == "1002" && j.Kind == JobKind.DeleteUser); // app holds all the device has
    }

    [Fact]
    public async Task Apply_missing_person_enqueues_upsert_face_and_every_finger()
    {
        _h.AddPerson("1001", "An"); _h.AddFace("1001");
        _h.AddFinger("1001", 1, "ModelA"); _h.AddFinger("1001", 6, "ModelA");
        var plan = await _rec.BuildAsync(null, Ct);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice>(), Ct);
        var jobs = _h.Db.Jobs.ListOpen(_d1).Select(j => (j.Kind, j.FingerId)).ToHashSet();
        Assert.Contains((JobKind.UpsertUser, 0), jobs);
        Assert.Contains((JobKind.SetFace, 0), jobs);
        Assert.Contains((JobKind.SetFinger, 1), jobs);
        Assert.Contains((JobKind.SetFinger, 6), jobs);
    }

    [Fact]
    public async Task Stranger_is_skipped_by_default_and_never_deleted()
    {
        await _dev1.UpsertUserAsync(W("9001", "Người lạ"), Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        var s = Assert.Single(plan.Devices.Single().Strangers);
        Assert.Equal("9001", s.EmployeeNo);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice>(), Ct);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice> { [Reconciler.StrangerKey(_d1, "9001")] = StrangerChoice.Skip }, Ct);
        Assert.Empty(_h.Db.Jobs.ListOpen(_d1));
        Assert.Null(_h.Db.People.Get("9001"));
        Assert.DoesNotContain("DeleteUserAsync", _dev1.Calls);
        Assert.NotNull(await _dev1.FindUserAsync("9001", Ct));
    }

    [Fact]
    public async Task Stranger_numbers_are_never_suggested_for_new_people()
    {
        _h.AddPerson("1001", "An");
        await _dev1.UpsertUserAsync(W("5000", "Người lạ"), Ct);
        await _rec.BuildAsync(null, Ct);
        Assert.Equal("5001", _h.Db.People.NextEmployeeNo());
    }

    [Fact]
    public async Task Remove_choice_enqueues_DeleteUser()
    {
        await _dev1.UpsertUserAsync(W("9001", "Người lạ"), Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice> { [Reconciler.StrangerKey(_d1, "9001")] = StrangerChoice.Remove }, Ct);
        var j = Assert.Single(_h.Db.Jobs.ListOpen(_d1));
        Assert.Equal(JobKind.DeleteUser, j.Kind);
        Assert.Equal("9001", j.EmployeeNo);
    }

    [Fact]
    public async Task Import_choice_brings_stranger_into_app()
    {
        await _dev1.UpsertUserAsync(W("9001", "Người lạ"), Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice> { [Reconciler.StrangerKey(_d1, "9001")] = StrangerChoice.Import }, Ct);
        Assert.Equal("Người lạ", _h.Db.People.Get("9001")!.FullName);
        Assert.Empty(_h.Db.Jobs.ListOpen(_d1));
    }

    [Fact]
    public async Task Progress_is_reported_in_vietnamese()
    {
        _h.AddPerson("1001", "An");
        await _dev1.UpsertUserAsync(W("1001", "An"), Ct);
        var msgs = new List<string>();
        await _rec.BuildAsync(new SyncProgress(msgs), Ct);
        Assert.Contains(msgs, m => m.StartsWith("Đang so khớp Máy tầng trệt (1/1)"));
    }

    [Fact]
    public async Task Imported_stranger_is_sent_to_the_other_device()
    {
        var d2 = _h.AddDevice("Máy tầng 2", "ModelA");
        await _dev1.UpsertUserAsync(W("9001", "Người lạ"), Ct);
        await _dev1.SetFaceAsync("9001", [0xFF, 0xD8, 0xFF, 0xD9], Ct);
        await _dev1.SetFingerprintAsync("9001", 3, SyncHarness.Tpl("ModelA"), 1, Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice> { [Reconciler.StrangerKey(_d1, "9001")] = StrangerChoice.Import }, Ct);

        Assert.NotNull(_h.Db.People.Get("9001"));
        var jobs = _h.Db.Jobs.ListOpen(d2).Select(j => (j.Kind, j.FingerId)).ToHashSet();
        Assert.Contains((JobKind.UpsertUser, 0), jobs);
        Assert.Contains((JobKind.SetFace, 0), jobs);
        Assert.Contains((JobKind.SetFinger, 3), jobs);
        Assert.Empty(_h.Db.Jobs.ListOpen(_d1));
    }

    [Fact]
    public async Task Unreadable_device_aborts_the_plan_with_a_clear_message()
    {
        _dev1.Offline = true;
        var ex = await Record.ExceptionAsync(() => _rec.BuildAsync(null, Ct));
        var hik = Assert.IsAssignableFrom<HikException>(ex);
        Assert.StartsWith("Không đọc được Máy tầng trệt: ", hik.Message);
        Assert.EndsWith("Kiểm tra máy có điện/mạng rồi so khớp lại.", hik.Message);
    }

    [Fact]
    public async Task Paused_device_is_not_read_and_a_wrong_password_pauses_it()
    {
        _h.Db.Devices.SetAuthPaused(_d1, true);
        var e = await Assert.ThrowsAsync<HikAuthException>(() => _rec.BuildAsync(null, Ct));
        Assert.Equal("Không đọc được Máy tầng trệt: " + JobRules.AuthMessage, e.Message);
        Assert.Empty(_dev1.Calls);

        _h.Db.Devices.SetAuthPaused(_d1, false);   // "Thử lại"
        _dev1.WrongPassword = true;
        await Assert.ThrowsAsync<HikAuthException>(() => _rec.BuildAsync(null, Ct));
        Assert.True(_h.Db.Devices.Get(_d1)!.AuthPaused);
        Assert.Single(_dev1.Calls);
    }

    [Fact]
    public async Task Device_2037_no_end_date_is_not_a_mismatch()
    {
        _h.AddPerson("1001", "An");   // no end date in the app
        await _dev1.UpsertUserAsync(new HikUserWrite("1001", "An", "2026-01-01T00:00:00", "2037-12-31T23:59:59", true, 1), Ct);
        var items = (await _rec.BuildAsync(null, Ct)).Devices.Single().Items;
        Assert.DoesNotContain(items, i => i.Kind == DiffKind.InfoMismatch);
    }

    [Fact]
    public async Task Apply_leaves_people_locked_or_unlocked_since_the_plan_alone()
    {
        _h.AddPerson("1001", "An");                           // missing on the device...
        _h.AddPerson("1002", "Bình", PersonStatus.Locked);    // ...and locked but still enabled there
        await _dev1.UpsertUserAsync(W("1002", "Bình"), Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        Assert.Contains(plan.Devices.Single().Items, i => i.EmployeeNo == "1001" && i.Kind == DiffKind.MissingPerson);
        Assert.Contains(plan.Devices.Single().Items, i => i.EmployeeNo == "1002" && i.Kind == DiffKind.LockedButEnabled);

        // Before the admin presses "Bổ sung": 1001 is locked, 1002 unlocked.
        var p1 = _h.Db.People.Get("1001")!;
        _h.Db.People.Update(p1 with { Status = PersonStatus.Locked });
        _h.Enqueue(_d1, "1001", JobKind.DisableUser);
        var p2 = _h.Db.People.Get("1002")!;
        _h.Db.People.Update(p2 with { Status = PersonStatus.Active });

        await _rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice>(), Ct);
        var jobs = _h.Db.Jobs.ListOpen(_d1);
        Assert.Contains(jobs, j => j.EmployeeNo == "1001" && j.Kind == JobKind.DisableUser);   // not cancelled by an upsert
        Assert.DoesNotContain(jobs, j => j.EmployeeNo == "1001" && j.Kind == JobKind.UpsertUser);
        Assert.DoesNotContain(jobs, j => j.EmployeeNo == "1002");   // no lock jobs for someone active again
    }

    [Fact]
    public async Task Name_whitespace_is_not_a_mismatch()
    {
        _h.AddPerson("1001", "An");
        await _dev1.UpsertUserAsync(W("1001", "  An "), Ct);
        var items = (await _rec.BuildAsync(null, Ct)).Devices.Single().Items;
        Assert.DoesNotContain(items, i => i.Kind == DiffKind.InfoMismatch);
    }

    [Fact]
    public async Task One_failed_stranger_import_does_not_stop_the_others()
    {
        var d2 = _h.AddDevice("Máy tầng 2", "ModelA");
        await _dev1.UpsertUserAsync(W("9001", "Một"), Ct);
        await _dev1.UpsertUserAsync(W("9002", "Hai"), Ct);
        var plan = await _rec.BuildAsync(null, Ct);
        await _dev1.DeleteUserAsync("9002", Ct); // vanishes before apply
        var choice = new Dictionary<string, StrangerChoice>
        {
            [Reconciler.StrangerKey(_d1, "9001")] = StrangerChoice.Import,
            [Reconciler.StrangerKey(_d1, "9002")] = StrangerChoice.Import,
        };
        var ex = await Assert.ThrowsAsync<HikException>(() => _rec.ApplyAsync(plan, choice, Ct));
        Assert.Contains("9002", ex.Message);
        Assert.DoesNotContain("9001", ex.Message);
        Assert.NotNull(_h.Db.People.Get("9001"));
        Assert.Null(_h.Db.People.Get("9002"));
        Assert.Contains(_h.Db.Jobs.ListOpen(d2), j => j.EmployeeNo == "9001" && j.Kind == JobKind.UpsertUser);
    }

    [Fact]
    public async Task First_reconcile_reads_fingers_even_when_counts_match()
    {
        _h.AddPerson("1001", "An");
        _h.AddFinger("1001", 1, "ModelA");
        await _dev1.UpsertUserAsync(W("1001", "An"), Ct);
        await _dev1.SetFingerprintAsync("1001", 5, SyncHarness.Tpl("ModelA"), 1, Ct); // same count, different id
        var items = (await _rec.BuildAsync(null, Ct)).Devices.Single().Items;
        Assert.Equal(new HashSet<int> { 5 }, _h.Db.Jobs.GetState("1001", _d1)!.Fingers);   // read, not invented
        Assert.Equal([1], items.Single(i => i.Kind == DiffKind.MissingFingers).Fingers);
        Assert.Equal([5], items.Single(i => i.Kind == DiffKind.ExtraFingers).Fingers);
    }

    [Fact]
    public async Task Unreadable_finger_list_is_unknown_not_empty_and_the_plan_goes_on()
    {
        var dev = new FakeDevice();
        var factory = new SingleDeviceFactory(new UnreadableFingersDevice(dev, "1001"));
        await using var h = new SyncHarness(factory);
        var d = h.AddDevice("Máy tầng trệt", "ModelA");
        var rec = new Reconciler(h.Db, factory, h.Engine, new DeviceImporter(h.Db, factory, h.Time), h.Time);
        h.AddPerson("1001", "An", PersonStatus.Locked);
        h.AddPerson("1002", "Bình");
        await dev.UpsertUserAsync(W("1001", "An"), Ct);
        await dev.SetFingerprintAsync("1001", 2, SyncHarness.Tpl("ModelA"), 1, Ct);
        await dev.UpsertUserAsync(W("1002", "Bình"), Ct);

        var plan = await rec.BuildAsync(null, Ct);   // one unreadable person does not abort the plan
        var st = h.Db.Jobs.GetState("1001", d)!;
        Assert.False(st.FingersKnown);
        Assert.Empty(st.Fingers);
        Assert.True(h.Db.Jobs.GetState("1002", d)!.FingersKnown);
        Assert.Contains(plan.Devices.Single().Items, i => i.EmployeeNo == "1001" && i.Kind == DiffKind.LockedButEnabled);

        await rec.ApplyAsync(plan, new Dictionary<string, StrangerChoice>(), Ct);   // the Lock rule cannot prove a full copy
        Assert.Contains(h.Db.Jobs.ListOpen(d), j => j.EmployeeNo == "1001" && j.Kind == JobKind.DisableUser);
        Assert.DoesNotContain(h.Db.Jobs.ListOpen(d), j => j.EmployeeNo == "1001" && j.Kind == JobKind.DeleteUser);
    }

    [Fact]
    public async Task Known_state_with_matching_count_is_not_read_again()
    {
        _h.AddPerson("1001", "An");
        _h.AddFinger("1001", 1, "ModelA");
        await _dev1.UpsertUserAsync(W("1001", "An"), Ct);
        await _dev1.SetFingerprintAsync("1001", 1, SyncHarness.Tpl("ModelA"), 1, Ct);
        await _rec.BuildAsync(null, Ct);
        var reads = _dev1.Calls.Count(c => c == nameof(IHikDevice.ReadFingerprintsAsync));
        await _rec.BuildAsync(null, Ct);
        Assert.Equal(reads, _dev1.Calls.Count(c => c == nameof(IHikDevice.ReadFingerprintsAsync)));
        Assert.Equal(new HashSet<int> { 1 }, _h.Db.Jobs.GetState("1001", _d1)!.Fingers);
    }

    private sealed class SyncProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }
}
