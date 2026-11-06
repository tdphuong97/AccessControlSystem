using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Sync;
using Acs.Core.Tests.Fakes;

namespace Acs.Core.Tests.Sync;

public sealed class SyncEngineTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public void Running_jobs_reset_to_pending_on_start()
    {
        var d = _h.AddDevice(authPaused: true);   // paused so the worker leaves the job alone
        _h.Db.Jobs.Insert(new JobRecord(0, d, "1001", JobKind.SetFace, 0, JobStatus.Running, 2, _h.Now, null, null, null));
        _h.Engine.Start();
        Assert.Equal(JobStatus.Pending, Assert.Single(_h.Db.Jobs.ListOpen(d)).Status);
    }

    [Fact]
    public async Task Offline_device_retries_with_backoff_30_60_120()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.Factory.For(d).Offline = true;
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();

        await _h.Until(() => _h.Job(d, "1001", JobKind.UpsertUser).Attempts == 1);
        Assert.Equal(SyncHarness.T0.AddSeconds(30), _h.Job(d, "1001", JobKind.UpsertUser).NextAt);
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        await _h.Until(() => _h.Job(d, "1001", JobKind.UpsertUser).Attempts == 2);
        Assert.Equal(SyncHarness.T0.AddSeconds(30 + 60), _h.Job(d, "1001", JobKind.UpsertUser).NextAt);
        _h.Time.Advance(TimeSpan.FromSeconds(60));
        await _h.Until(() => _h.Job(d, "1001", JobKind.UpsertUser).Attempts == 3);
        Assert.Equal(SyncHarness.T0.AddSeconds(30 + 60 + 120), _h.Job(d, "1001", JobKind.UpsertUser).NextAt);

        await _h.Until(() => _h.Engine.Status().Single().Message != null);
        var st = Assert.Single(_h.Engine.Status());
        Assert.False(st.Online);
        Assert.Equal(1, st.Pending);
        Assert.NotNull(st.Message);
    }

    [Fact]
    public async Task Device_coming_back_online_runs_waiting_retries_at_once()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        var fake = _h.Factory.For(d);
        fake.Offline = true;
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Job(d, "1001", JobKind.UpsertUser).Attempts == 1);
        fake.Offline = false;
        _h.Time.Advance(TimeSpan.FromSeconds(30));   // the 30 s ping finds the device (job was due too)
        await _h.WaitDone(d);
        Assert.NotNull(await fake.FindUserAsync("1001", default));
        await _h.Until(() => _h.Engine.Status().Single().Online);
        Assert.NotNull(_h.Db.Devices.Get(d)!.LastSeen);
    }

    [Fact]
    public async Task Auth_error_pauses_device_without_retry()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        var fake = _h.Factory.For(d);
        fake.WrongPassword = true;
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Devices.Get(d)!.AuthPaused);
        _h.Time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(50);
        Assert.Single(fake.Calls);
        var j = Assert.Single(_h.Db.Jobs.ListOpen(d));
        Assert.Equal(JobStatus.Pending, j.Status);
        Assert.Equal(0, j.Attempts);
        Assert.True(_h.Engine.Status().Single().AuthPaused);

        // user fixes the password, clears the pause and wakes the worker
        fake.WrongPassword = false;
        _h.Db.Devices.SetAuthPaused(d, false);
        _h.Engine.Wake(d);
        await _h.WaitDone(d);
        Assert.NotNull(await fake.FindUserAsync("1001", default));
    }

    [Fact]
    public async Task Verify_failure_marks_not_saved()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.Factory.For(d).ForgetWrites = true;
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed), TimeSpan.FromSeconds(1));
        var j = Assert.Single(_h.Db.Jobs.ListOpen(d));
        Assert.Equal("not_saved", j.ErrorKind);
        Assert.Equal(JobRules.NotSavedMessage, j.ErrorMessage);
        // one look-up before the first write (no state row yet: is the number free?) + 3 verify reads
        Assert.Equal(1 + JobRules.VerifyReads, _h.Factory.For(d).Calls.Count(c => c == nameof(IHikDevice.FindUserAsync)));
        Assert.Null(_h.Db.Jobs.GetState("1001", d)?.VerifiedAt);
        Assert.Contains(_h.Db.Log.List("1001", d, 10), e => !e.Ok);
    }

    [Fact]
    public async Task Upsert_runs_before_face_and_fingers()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.AddFace();
        _h.AddFinger("1001", 2, "ModelA");
        _h.Enqueue(d, "1001", JobKind.SetFinger, 2);
        _h.Enqueue(d, "1001", JobKind.SetFace);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.WaitDone(d);
        var calls = _h.Factory.For(d).Calls.ToList();
        var up = calls.IndexOf(nameof(IHikDevice.UpsertUserAsync));
        var face = calls.IndexOf(nameof(IHikDevice.SetFaceAsync));
        var fp = calls.IndexOf(nameof(IHikDevice.SetFingerprintAsync));
        Assert.True(up >= 0 && up < face && face < fp, string.Join(",", calls));
        var s = _h.Db.Jobs.GetState("1001", d)!;
        Assert.True(s.UserPresent && s.UserEnabled && s.FacePresent);
        Assert.Equal(new[] { 2 }, s.Fingers);
        Assert.NotNull(s.VerifiedAt);
    }

    [Fact]
    public async Task Cross_group_template_rejected_sets_needs_scan()
    {
        var fake = new FakeDevice();
        await using var h = new SyncHarness(new SingleDeviceFactory(new StrictGroupDevice(fake, "ModelA")));
        var d = h.AddDevice("Máy tầng 1", "ModelA");
        h.AddPerson();
        h.AddFinger("1001", 6, "ModelB");
        h.Enqueue(d, "1001", JobKind.SetFinger, 6);
        h.Engine.Start();
        await h.Until(() => h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed));
        var j = Assert.Single(h.Db.Jobs.ListOpen(d));
        Assert.Equal("needs_scan", j.ErrorKind);
        Assert.Equal("Cần quét ngón cái trái trực tiếp ở Máy tầng 1.", j.ErrorMessage);
        Assert.Contains(6, h.Db.Jobs.GetState("1001", d)!.NeedsScan);
        Assert.Empty(await fake.ReadFingerprintsAsync("1001", default));
        Assert.Equal("ModelB", Assert.Single(h.Db.Biometrics.GetFingers("1001")).FpGroup);   // no copy stored
    }

    private sealed class RefusesFingers(IHikDevice inner, HikRejectedException refusal) : ForwardingDevice(inner)
    {
        public override Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct) => throw refusal;
    }

    [Theory]
    [InlineData("deviceFull", "Máy đã đầy bộ nhớ vân tay. Xoá bớt vân tay không còn dùng trên máy rồi bấm Thử lại.")]
    [InlineData("readerNotReady", "Máy chưa sẵn sàng nhận vân tay. Chờ một lát rồi bấm Thử lại.")]
    [InlineData("employeeNoNotExist", "Máy chưa có người này. Bấm Thử lại để gửi người trước rồi mới gửi sinh trắc học.")]
    public async Task Cross_group_refusal_other_than_a_sensor_mismatch_fails_with_its_own_message(string code, string message)
    {
        await using var h = new SyncHarness(new SingleDeviceFactory(new RefusesFingers(new FakeDevice(), new HikRejectedException(code, message))));
        var d = h.AddDevice("Máy tầng 1", "ModelA");
        h.AddPerson();
        h.AddFinger("1001", 6, "ModelB");
        h.Enqueue(d, "1001", JobKind.SetFinger, 6);
        h.Engine.Start();
        await h.Until(() => h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed));
        var j = Assert.Single(h.Db.Jobs.ListOpen(d));
        Assert.Equal("rejected", j.ErrorKind);
        Assert.Equal(message, j.ErrorMessage);
        Assert.DoesNotContain(6, h.Db.Jobs.GetState("1001", d)?.NeedsScan ?? new HashSet<int>());
    }

    [Fact]
    public async Task Cross_group_checkFingerPrintFailed_also_means_needs_scan()
    {
        await using var h = new SyncHarness(new SingleDeviceFactory(new RefusesFingers(new FakeDevice(),
            new HikRejectedException("checkFingerPrintFailed", "Máy không nhận vân tay này. Quét lại vân tay cho rõ rồi thử lại."))));
        var d = h.AddDevice("Máy tầng 1", "ModelA");
        h.AddPerson();
        h.AddFinger("1001", 6, "ModelB");
        h.Enqueue(d, "1001", JobKind.SetFinger, 6);
        h.Engine.Start();
        await h.Until(() => h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed));
        Assert.Equal("needs_scan", Assert.Single(h.Db.Jobs.ListOpen(d)).ErrorKind);
        Assert.Contains(6, h.Db.Jobs.GetState("1001", d)!.NeedsScan);
    }

    private sealed class AlreadyGone(IHikDevice inner) : ForwardingDevice(inner)
    {
        public override Task DeleteUserAsync(string employeeNo, CancellationToken ct) =>
            throw new HikRejectedException("employeeNoNotExist", "Máy chưa có người này.", "{\"subStatusCode\":\"employeeNoNotExist\"}");
    }

    [Fact]
    public async Task DeleteUser_answered_not_exist_is_done_once_absence_is_verified()
    {
        await using var h = new SyncHarness(new SingleDeviceFactory(new AlreadyGone(new FakeDevice())));
        var d = h.AddDevice();
        h.Enqueue(d, "1001", JobKind.DeleteUser);   // explicit delete: the person is no longer in the app
        h.Engine.Start();
        await h.WaitDone(d);
        Assert.Contains(h.Db.Log.List("1001", d, 10), l => l.Ok && l.Action == nameof(JobKind.DeleteUser));
    }

    [Fact]
    public async Task Cross_group_copy_is_not_stored_when_verify_fails()
    {
        var d = _h.AddDevice("Máy tầng 1", "ModelA");
        _h.AddPerson();
        _h.AddFinger("1001", 1, "ModelB");
        _h.Factory.For(d).ForgetWrites = true;
        _h.Enqueue(d, "1001", JobKind.SetFinger, 1);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed), TimeSpan.FromSeconds(1));
        Assert.Equal("not_saved", Assert.Single(_h.Db.Jobs.ListOpen(d)).ErrorKind);
        Assert.Equal("ModelB", Assert.Single(_h.Db.Biometrics.GetFingers("1001")).FpGroup);
    }

    [Fact]
    public async Task Transient_write_errors_on_a_reachable_device_follow_backoff_and_stay_online()
    {
        var scripted = new ScriptedDevice(new FakeDevice()) { TransientOnWrites = true };
        await using var h = new SyncHarness(new SingleDeviceFactory(scripted));
        var d = h.AddDevice();
        h.AddPerson();
        h.Enqueue(d, "1001", JobKind.UpsertUser);
        h.Engine.Start();
        JobRecord J() => h.Job(d, "1001", JobKind.UpsertUser);

        await h.Until(() => J().Attempts == 1 && h.Engine.Status().Single().Online);
        Assert.Equal(SyncHarness.T0.AddSeconds(30), J().NextAt);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Until(() => J().Attempts == 2);
        Assert.Equal(SyncHarness.T0.AddSeconds(90), J().NextAt);
        h.Time.Advance(TimeSpan.FromSeconds(30));   // pings succeed, but the job must keep its backoff
        await Task.Delay(50);
        Assert.Equal(2, J().Attempts);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Until(() => J().Attempts == 3);
        Assert.Equal(SyncHarness.T0.AddSeconds(90 + 120), J().NextAt);
        Assert.True(h.Engine.Status().Single().Online);
        Assert.DoesNotContain(h.Db.Log.List(null, d, 100), e => e.Action == "Ping");
    }

    [Fact]
    public async Task Plain_exception_on_ping_does_not_kill_the_worker()
    {
        var scripted = new ScriptedDevice(new FakeDevice()) { ThrowPlainOnPing = true };
        await using var h = new SyncHarness(new SingleDeviceFactory(scripted));
        var d = h.AddDevice();
        h.Engine.Start();
        await h.Until(() => h.Db.Log.List(null, d, 10).Any(e => e.Action == "Ping" && !e.Ok));
        Assert.False(h.Engine.Status().Single().Online);
        Assert.Single(h.Db.Log.List(null, d, 10));

        scripted.ThrowPlainOnPing = false;
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Until(() => h.Engine.Status().Single().Online);
        Assert.Single(h.Db.Log.List(null, d, 10));   // no extra log rows
        await h.Engine.StopAsync();                  // does not throw
    }

    [Fact]
    public async Task Faulting_factory_is_logged_and_retried()
    {
        var factory = new ThrowingFactory { Fail = true };
        await using var h = new SyncHarness(factory);
        var d = h.AddDevice();
        h.Engine.Start();
        await h.Until(() => h.Db.Log.List(null, d, 10).Any(e => e.Action is "Worker" or "Ping"));
        factory.Fail = false;
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Until(() => h.Engine.Status().Single().Online);
        await h.Engine.StopAsync();
    }

    [Fact]
    public async Task Upsert_verify_compares_valid_dates()
    {
        var scripted = new ScriptedDevice(new FakeDevice());
        await using var h = new SyncHarness(new SingleDeviceFactory(scripted));
        var d = h.AddDevice();
        h.AddPerson();
        h.Enqueue(d, "1001", JobKind.UpsertUser);
        h.Engine.Start();
        await h.WaitDone(d);

        scripted.KeepOldEndTime = true;
        var p = h.Db.People.Get("1001")!;
        h.Db.People.Update(p with { ValidTo = new DateOnly(2026, 6, 30) });
        h.Enqueue(d, "1001", JobKind.UpsertUser);
        h.Engine.Wake(d);
        await h.Until(() => h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed), TimeSpan.FromSeconds(1));
        Assert.Equal("not_saved", Assert.Single(h.Db.Jobs.ListOpen(d)).ErrorKind);
    }

    [Fact]
    public void Job_superseded_before_running_is_not_started()
    {
        var d = _h.AddDevice();
        _h.Enqueue(d, "1001", JobKind.SetFace);
        var j = _h.Job(d, "1001", JobKind.SetFace);
        _h.Enqueue(d, "1001", JobKind.DeleteUser);   // removes the SetFace row
        Assert.False(_h.Db.Jobs.MarkRunning(j, _h.Now));
    }

    [Fact]
    public async Task Cross_group_success_copies_template()
    {
        var d = _h.AddDevice("Máy tầng 1", "ModelA");
        _h.AddPerson();
        _h.AddFinger("1001", 1, "ModelB", "ZZZZ");
        _h.Enqueue(d, "1001", JobKind.SetFinger, 1);
        _h.Engine.Start();
        await _h.WaitDone(d);
        var copy = _h.Db.Biometrics.GetFingers("1001").Single(f => f.FpGroup == "ModelA");
        Assert.Equal(SyncHarness.Tpl("ModelB", "ZZZZ"), copy.TemplateBase64);
        Assert.Equal(2, _h.Db.Biometrics.GetFingers("1001").Count);
        Assert.Equal(new[] { 1 }, _h.Db.Jobs.GetState("1001", d)!.Fingers);
        Assert.Empty(_h.Db.Jobs.GetState("1001", d)!.NeedsScan);
    }

    [Fact]
    public async Task Own_group_template_is_preferred()
    {
        var d = _h.AddDevice("Máy tầng 1", "ModelA");
        _h.AddPerson();
        _h.AddFinger("1001", 1, "ModelB", "BBBB");
        _h.AddFinger("1001", 1, "ModelA", "AAAA");
        _h.Enqueue(d, "1001", JobKind.SetFinger, 1);
        _h.Engine.Start();
        await _h.WaitDone(d);
        var sent = (await _h.Factory.For(d).ReadFingerprintsAsync("1001", default)).Single();
        Assert.Equal(SyncHarness.Tpl("ModelA", "AAAA"), sent.TemplateBase64);
    }

    [Fact]
    public async Task DeleteUser_removes_everything_and_clears_state()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.AddFace();
        _h.AddFinger("1001", 1, "ModelA");
        foreach (var k in new[] { JobKind.UpsertUser, JobKind.SetFace }) _h.Enqueue(d, "1001", k);
        _h.Enqueue(d, "1001", JobKind.SetFinger, 1);
        _h.Engine.Start();
        await _h.WaitDone(d);
        Assert.True(_h.Db.Jobs.GetState("1001", d)!.UserPresent);

        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        _h.Engine.Wake(d);
        await _h.WaitDone(d);
        var fake = _h.Factory.For(d);
        Assert.Null(await fake.FindUserAsync("1001", default));
        var s = _h.Db.Jobs.GetState("1001", d)!;
        Assert.False(s.UserPresent || s.FacePresent);
        Assert.Empty(s.Fingers);
    }

    [Fact]
    public async Task Upsert_does_not_merge_into_another_device_user_with_the_same_number()
    {
        var d = _h.AddDevice("Máy tầng 1");
        _h.AddPerson("1001", "Nguyễn An");
        var fake = _h.Factory.For(d);
        await fake.UpsertUserAsync(new HikUserWrite("1001", "Trần Cũ", "2020-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed));
        var j = Assert.Single(_h.Db.Jobs.ListOpen(d));
        Assert.Equal("conflict", j.ErrorKind);
        Assert.Equal("Máy đang có người khác dùng mã này (Trần Cũ). Đổi mã hoặc xử lý bằng So khớp.", j.ErrorMessage);
        Assert.Equal("Trần Cũ", (await fake.FindUserAsync("1001", default))!.Name);   // untouched
        Assert.DoesNotContain(nameof(IHikDevice.UpsertUserAsync), fake.Calls.Skip(1));

        // Once So khớp has confirmed the device user (state row), a retry updates it on purpose.
        _h.Db.Jobs.SetState(new DeviceStateRecord("1001", d, true, true, false, new HashSet<int>(), new HashSet<int>(), _h.Now));
        _h.Db.Jobs.ResetFailed(d, _h.Now);
        _h.Engine.Wake(d);
        await _h.WaitDone(d);
        Assert.Equal("Nguyễn An", (await fake.FindUserAsync("1001", default))!.Name);
    }

    [Fact]
    public async Task Fresh_state_row_never_claims_a_finger_list_nobody_read()
    {
        var d = _h.AddDevice();
        _h.AddPerson("1001", "Nguyễn An");
        _h.AddPerson("1002", "Trần Bình");
        _h.AddFace("1002");
        var fake = _h.Factory.For(d);
        await fake.UpsertUserAsync(new HikUserWrite("1001", "Nguyễn An", "2026-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        await fake.SetFingerprintAsync("1001", 3, SyncHarness.Tpl("ModelA"), 1, default);
        await fake.UpsertUserAsync(new HikUserWrite("1002", "Trần Bình", "2026-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        await fake.SetFingerprintAsync("1002", 4, SyncHarness.Tpl("ModelA"), 1, default);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);   // its read-back says numOfFP = 1: which finger is unknown
        _h.Enqueue(d, "1002", JobKind.SetFace);      // no user read at all
        _h.AddPerson("1003", "Lê Cường");
        _h.Enqueue(d, "1003", JobKind.UpsertUser);   // new on the device: numOfFP = 0 is a complete answer
        _h.Engine.Start();
        await _h.WaitDone(d);
        Assert.False(_h.Db.Jobs.GetState("1001", d)!.FingersKnown);
        Assert.False(_h.Db.Jobs.GetState("1002", d)!.FingersKnown);
        var none = _h.Db.Jobs.GetState("1003", d)!;
        Assert.True(none.FingersKnown);
        Assert.Empty(none.Fingers);
    }

    [Fact]
    public async Task Upsert_with_no_state_row_and_the_same_name_on_the_device_proceeds()
    {
        var d = _h.AddDevice();
        _h.AddPerson("1001", "Nguyễn An");
        await _h.Factory.For(d).UpsertUserAsync(new HikUserWrite("1001", " Nguyễn An ", "2020-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.WaitDone(d);
        Assert.True(_h.Db.Jobs.GetState("1001", d)!.UserPresent);
    }

    private async Task<FakeDevice> LockedOnDevice(int d, bool deviceFace, params int[] deviceFingers)
    {
        _h.AddPerson(status: PersonStatus.Locked);
        var fake = _h.Factory.For(d);
        await fake.UpsertUserAsync(new HikUserWrite("1001", "Nguyễn An", "2026-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        if (deviceFace) await fake.SetFaceAsync("1001", [0xFF, 0xD8, 0xFF, 0xD9], default);
        foreach (var f in deviceFingers) await fake.SetFingerprintAsync("1001", f, SyncHarness.Tpl("ModelA", "D" + f), 1, default);
        return fake;
    }

    [Fact]
    public async Task DeleteUser_of_locked_person_only_disables_when_the_device_has_a_face_the_app_lacks()
    {
        var d = _h.AddDevice();
        var fake = await LockedOnDevice(d, deviceFace: true);
        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Count == 0, TimeSpan.FromSeconds(1));
        var u = await fake.FindUserAsync("1001", default);
        Assert.NotNull(u);
        Assert.False(u.Enabled);
        Assert.True(await fake.HasFaceAsync("1001", default));   // the only copy of the face is still there
        Assert.DoesNotContain(nameof(IHikDevice.DeleteUserAsync), fake.Calls);
        var s = _h.Db.Jobs.GetState("1001", d)!;
        Assert.True(s.UserPresent && !s.UserEnabled && s.FacePresent);
        Assert.Contains(_h.Db.Log.List("1001", d, 10), l => l.Ok && l.Message == JobRules.KeptOnDeviceMessage);
    }

    [Fact]
    public async Task DeleteUser_of_locked_person_only_disables_when_a_finger_has_no_template_for_this_sensor()
    {
        var d = _h.AddDevice("Máy tầng 1", "ModelA");
        var fake = await LockedOnDevice(d, false, 1, 2);
        _h.AddFinger("1001", 1, "ModelA");
        _h.AddFinger("1001", 2, "ModelB");   // another sensor's template: this device may refuse it later
        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Count == 0, TimeSpan.FromSeconds(1));
        Assert.Equal(2, (await fake.ReadFingerprintsAsync("1001", default)).Count);
        Assert.False((await fake.FindUserAsync("1001", default))!.Enabled);
        Assert.Equal(new HashSet<int> { 1, 2 }, _h.Db.Jobs.GetState("1001", d)!.Fingers);
    }

    [Fact]
    public async Task DeleteUser_of_locked_person_with_a_full_copy_removes_the_user()
    {
        var d = _h.AddDevice("Máy tầng 1", "ModelA");
        var fake = await LockedOnDevice(d, true, 3);
        _h.AddFace();
        _h.AddFinger("1001", 3, "ModelA");
        _h.Enqueue(d, "1001", JobKind.DeleteUser);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Count == 0, TimeSpan.FromSeconds(1));
        Assert.Null(await fake.FindUserAsync("1001", default));
        var s = _h.Db.Jobs.GetState("1001", d)!;
        Assert.False(s.UserPresent);
        Assert.True(s.FingersKnown);
    }

    [Fact]
    public async Task DeleteUser_of_locked_person_with_unreadable_fingers_only_disables()
    {
        var fake = new FakeDevice();
        await using var h = new SyncHarness(new SingleDeviceFactory(new UnreadableFingersDevice(fake, "1001")));
        var d = h.AddDevice("Máy tầng 1", "ModelA");
        h.AddPerson(status: PersonStatus.Locked);
        await fake.UpsertUserAsync(new HikUserWrite("1001", "Nguyễn An", "2026-01-01T00:00:00", JobRules.NoEndDate, true, 1), default);
        await fake.SetFingerprintAsync("1001", 4, SyncHarness.Tpl("ModelA"), 1, default);
        h.Enqueue(d, "1001", JobKind.DeleteUser);
        h.Engine.Start();
        await h.Until(() => h.Db.Jobs.ListOpen(d).Count == 0, TimeSpan.FromSeconds(1));
        Assert.NotNull(await fake.FindUserAsync("1001", default));
        Assert.False(h.Db.Jobs.GetState("1001", d)!.FingersKnown);
    }

    [Fact]
    public async Task Delete_face_and_finger_verify_absence()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.AddFace();
        _h.AddFinger("1001", 3, "ModelA");
        foreach (var k in new[] { JobKind.UpsertUser, JobKind.SetFace }) _h.Enqueue(d, "1001", k);
        _h.Enqueue(d, "1001", JobKind.SetFinger, 3);
        _h.Engine.Start();
        await _h.WaitDone(d);
        _h.Enqueue(d, "1001", JobKind.DeleteFace);
        _h.Enqueue(d, "1001", JobKind.DeleteFinger, 3);
        _h.Engine.Wake(d);
        await _h.WaitDone(d);
        var s = _h.Db.Jobs.GetState("1001", d)!;
        Assert.False(s.FacePresent);
        Assert.Empty(s.Fingers);
        Assert.Empty(await _h.Factory.For(d).ReadFingerprintsAsync("1001", default));
    }

    [Fact]
    public async Task DisableUser_turns_off_the_user_on_the_device()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.WaitDone(d);
        _h.Enqueue(d, "1001", JobKind.DisableUser);
        _h.Engine.Wake(d);
        await _h.WaitDone(d);
        Assert.False((await _h.Factory.For(d).FindUserAsync("1001", default))!.Enabled);
        Assert.False(_h.Db.Jobs.GetState("1001", d)!.UserEnabled);
    }

    [Fact]
    public async Task Locked_person_upsert_writes_disabled_user_and_verifies()
    {
        var d = _h.AddDevice();
        _h.AddPerson(status: PersonStatus.Locked);
        _h.Enqueue(d, "1001", JobKind.UpsertUser);
        _h.Engine.Start();
        await _h.WaitDone(d);
        Assert.False((await _h.Factory.For(d).FindUserAsync("1001", default))!.Enabled);
    }

    [Fact]
    public async Task Missing_data_is_a_noop_done()
    {
        var d = _h.AddDevice();
        _h.AddPerson();   // no face stored
        _h.Enqueue(d, "1001", JobKind.SetFace);
        _h.Enqueue(d, "9999", JobKind.UpsertUser);   // person unknown
        _h.Engine.Start();
        await _h.WaitDone(d);
        Assert.DoesNotContain(nameof(IHikDevice.SetFaceAsync), _h.Factory.For(d).Calls);
        Assert.DoesNotContain(nameof(IHikDevice.UpsertUserAsync), _h.Factory.For(d).Calls);
    }

    [Fact]
    public async Task Rejected_face_fails_with_rejected_and_events_fire()
    {
        var d = _h.AddDevice();
        _h.AddPerson();
        _h.AddFace();
        _h.Factory.For(d).RejectFace = true;
        var people = new List<string>();
        _h.Engine.PersonChanged += p => { lock (people) people.Add(p); };
        _h.Enqueue(d, "1001", JobKind.SetFace);
        _h.Engine.Start();
        await _h.Until(() => _h.Db.Jobs.ListOpen(d).Any(j => j.Status == JobStatus.Failed));
        var j = Assert.Single(_h.Db.Jobs.ListOpen(d));
        Assert.Equal("rejected", j.ErrorKind);
        Assert.Contains("ảnh khuôn mặt", j.ErrorMessage);
        await _h.Until(() => { lock (people) return people.Contains("1001"); });
        Assert.Equal(1, _h.Engine.Status().Single().Failed);
    }

    [Fact]
    public async Task Idle_device_is_pinged_every_30_seconds()
    {
        var d = _h.AddDevice();
        _h.Engine.Start();
        var fake = _h.Factory.For(d);
        await _h.Until(() => fake.Calls.Count == 1);
        Assert.NotNull(_h.Db.Devices.Get(d)!.LastSeen);
        Assert.Equal("DS-K1T343", _h.Db.Devices.Get(d)!.Model);
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        await _h.Until(() => fake.Calls.Count == 2);
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        await _h.Until(() => fake.Calls.Count == 3);
        Assert.All(fake.Calls, c => Assert.Equal(nameof(IHikDevice.GetDeviceInfoAsync), c));
    }

    [Fact]
    public async Task Stop_then_start_again_is_clean()
    {
        _h.AddDevice();
        _h.Engine.Start();
        await _h.Engine.StopAsync();
        _h.Engine.Start();
        await _h.Engine.StopAsync();
    }
}
