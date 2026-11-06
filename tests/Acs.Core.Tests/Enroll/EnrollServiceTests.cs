using Acs.Core.Enroll;
using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Tests.Fakes;
using Acs.Core.Tests.Sync;

namespace Acs.Core.Tests.Enroll;

public sealed class EnrollServiceTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    private readonly EnrollService _svc;
    private readonly int _d1, _d2;

    public EnrollServiceTests()
    {
        _d1 = _h.AddDevice("A", "ModelA");
        _d2 = _h.AddDevice("B", "ModelB");
        _svc = new EnrollService(_h.Db, _h.Factory, _h.Engine, _h.Time);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private IReadOnlyList<JobRecord> Jobs(int dev) => _h.Db.Jobs.ListOpen(dev);

    [Fact]
    public void Name_over_32_chars_is_refused_before_saving()
    {
        var ex = Assert.Throws<EnrollException>(() => _svc.CreatePerson(new NewPerson(new string('a', 33), "2001", null, null)));
        Assert.Equal("Họ tên dài tối đa 32 ký tự (máy chấm công không nhận tên dài hơn).", ex.Message);
        Assert.Null(_h.Db.People.Get("2001"));
        Assert.Empty(Jobs(_d1));
    }

    [Fact]
    public void Create_stores_and_enqueues_upsert_on_every_device()
    {
        var p = _svc.CreatePerson(new NewPerson("  Nguyễn An ", "2001", "Kho", null));
        Assert.Equal("Nguyễn An", p.FullName);
        Assert.NotNull(p.GroupId);
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.UpsertUser);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.UpsertUser);
    }

    [Fact]
    public void Duplicate_employeeNo_refused()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        var ex = Assert.Throws<EnrollException>(() => _svc.CreatePerson(new NewPerson("Bình", "2001", null, null)));
        Assert.Equal("Mã nhân viên đã có người dùng.", ex.Message);
    }

    [Fact]
    public void Bad_employeeNo_refused()
    {
        Assert.Throws<EnrollException>(() => _svc.CreatePerson(new NewPerson("An", "ab-1", null, null)));
        Assert.Throws<EnrollException>(() => _svc.CreatePerson(new NewPerson("An", "", null, null)));
    }

    [Fact]
    public async Task Second_capture_on_busy_device_is_refused()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        var fake = _h.Factory.For(_d1);
        fake.CaptureDelay = TimeSpan.FromMilliseconds(500);
        var tasks = new[]
        {
            Task.Run(() => _svc.CaptureFaceAsync("2001", _d1, CancellationToken.None)),
            Task.Run(() => _svc.CaptureFaceAsync("2001", _d1, CancellationToken.None)),
        };
        var results = await Task.WhenAll(tasks.Select(async t => { try { await t; return (Exception?)null; } catch (Exception e) { return e; } }));
        Assert.Single(results, r => r is EnrollException { Message: "Máy đang chụp/quét, đợi xong rồi bấm lại." });
        Assert.Equal(1, fake.ConcurrentCaptures);
    }

    [Fact]
    public async Task Low_quality_finger_refused_not_stored()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.Factory.For(_d1).NextFinger = new CapturedFinger(SyncHarness.Tpl("ModelA"), 39);
        var ex = await Assert.ThrowsAsync<EnrollException>(() => _svc.CaptureFingerAsync("2001", _d1, 1, CancellationToken.None));
        Assert.Equal("Vân tay chưa rõ (chất lượng 39). Đặt ngón ngay giữa, giữ yên rồi quét lại.", ex.Message);
        Assert.Empty(_h.Db.Biometrics.GetFingers("2001"));
        Assert.DoesNotContain(Jobs(_d1), j => j.Kind == JobKind.SetFinger);
    }

    [Fact]
    public async Task Finger_rescan_replaces_all_copies_and_enqueues_everywhere()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFinger("2001", 1, "ModelA");
        _h.AddFinger("2001", 1, "ModelB");
        _h.Factory.For(_d2).NextFinger = new CapturedFinger(SyncHarness.Tpl("ModelB", "NEW"), 80);
        await _svc.CaptureFingerAsync("2001", _d2, 1, CancellationToken.None);
        var row = Assert.Single(_h.Db.Biometrics.GetFingers("2001"));
        Assert.Equal("ModelB", row.FpGroup);
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.SetFinger && j.FingerId == 1);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.SetFinger && j.FingerId == 1);
    }

    [Fact]
    public void SaveFace_validates_jpeg_and_size()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Assert.Throws<EnrollException>(() => _svc.SaveFace("2001", [1, 2, 3, 4], BiometricSource.File));
        var big = new byte[EnrollService.MaxFaceBytes + 1]; big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        Assert.Throws<EnrollException>(() => _svc.SaveFace("2001", big, BiometricSource.File));
        Assert.Null(_h.Db.Biometrics.GetFace("2001"));
        _svc.SaveFace("2001", [0xFF, 0xD8, 0xFF, 0xD9], BiometricSource.File);
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.SetFace);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.SetFace);
    }

    private void Settle(string no, int dev, bool face, params int[] fingers)
    {
        foreach (var j in Jobs(dev).ToList()) { _h.Db.Jobs.MarkRunning(j, _h.Now); _h.Db.Jobs.MarkDone(j.Id, _h.Now); }
        _h.Db.Jobs.SetState(new DeviceStateRecord(no, dev, true, true, face, fingers.ToHashSet(), new HashSet<int>(), _h.Now));
    }

    [Fact]
    public void Lock_deletes_when_full_copy_else_disables()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFace("2001");
        _h.AddFinger("2001", 1, "ModelA");
        Settle("2001", _d1, true, 1);       // app has face + finger 1 -> full copy
        Settle("2001", _d2, true, 1, 2);    // device has finger 2 the app lacks -> not a full copy
        _svc.Lock("2001");
        Assert.Equal(PersonStatus.Locked, _h.Db.People.Get("2001")!.Status);
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DeleteUser);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.DisableUser);
        Assert.DoesNotContain(Jobs(_d2), j => j.Kind == JobKind.DeleteUser);
    }

    [Fact]
    public void Lock_disables_when_the_only_template_is_for_another_sensor_group()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFinger("2001", 1, "ModelB");   // d1 is ModelA: its sensor may refuse this template
        Settle("2001", _d1, false, 1);
        Settle("2001", _d2, false, 1);
        _svc.Lock("2001");
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DisableUser);
        Assert.DoesNotContain(Jobs(_d1), j => j.Kind == JobKind.DeleteUser);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.DeleteUser);   // own group (ModelB) copy exists
    }

    [Fact]
    public void Lock_disables_when_the_device_finger_list_is_unknown()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Settle("2001", _d1, false);
        _h.Db.Jobs.SetState(_h.Db.Jobs.GetState("2001", _d1)! with { FingersKnown = false });
        Assert.False(_h.Db.Jobs.GetState("2001", _d1)!.FingersKnown);   // stored, not just in memory
        _svc.Lock("2001");
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DisableUser);
        Assert.DoesNotContain(Jobs(_d1), j => j.Kind == JobKind.DeleteUser);
    }

    [Fact]
    public void Lock_disables_when_device_face_not_in_app()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Settle("2001", _d1, true);
        _svc.Lock("2001");
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DisableUser);
    }

    [Fact]
    public void Unlock_requeues_face_and_fingers()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFace("2001");
        _h.AddFinger("2001", 1, "ModelA");
        _h.AddFinger("2001", 2, "ModelA");
        Settle("2001", _d1, true, 1, 2); Settle("2001", _d2, true, 1, 2);
        _svc.Lock("2001");
        _svc.Unlock("2001");
        Assert.Equal(PersonStatus.Active, _h.Db.People.Get("2001")!.Status);
        foreach (var d in new[] { _d1, _d2 })
        {
            var js = Jobs(d);
            Assert.Contains(js, j => j.Kind == JobKind.UpsertUser);
            Assert.Contains(js, j => j.Kind == JobKind.SetFace);
            Assert.Contains(js, j => j.Kind == JobKind.SetFinger && j.FingerId == 1);
            Assert.Contains(js, j => j.Kind == JobKind.SetFinger && j.FingerId == 2);
            Assert.DoesNotContain(js, j => j.Kind is JobKind.DeleteUser or JobKind.DisableUser);
        }
    }

    [Fact]
    public void Lock_disables_on_device_with_no_state_row()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFace("2001");
        _h.AddFinger("2001", 1, "ModelA");
        Settle("2001", _d1, true, 1);
        foreach (var j in Jobs(_d2).ToList()) { _h.Db.Jobs.MarkRunning(j, _h.Now); _h.Db.Jobs.MarkDone(j.Id, _h.Now); }
        _svc.Lock("2001");
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DeleteUser);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.DisableUser);
        Assert.DoesNotContain(Jobs(_d2), j => j.Kind == JobKind.DeleteUser);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Invalid_finger_id_refused_before_device_call(int id)
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        var ex = await Assert.ThrowsAsync<EnrollException>(() => _svc.CaptureFingerAsync("2001", _d1, id, CancellationToken.None));
        Assert.Equal("Số ngón tay không hợp lệ.", ex.Message);
        Assert.DoesNotContain("CaptureFingerprintAsync", _h.Factory.For(_d1).Calls);
        Assert.Throws<EnrollException>(() => _svc.DeleteFinger("2001", id));
    }

    [Fact]
    public void UpdatePerson_on_locked_person_saves_without_enqueuing_upsert()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Settle("2001", _d1, false); Settle("2001", _d2, false);
        _svc.Lock("2001");
        _svc.UpdatePerson(new PersonEdit("2001", "An Mới", null, null));
        Assert.Equal("An Mới", _h.Db.People.Get("2001")!.FullName);
        foreach (var d in new[] { _d1, _d2 })
        {
            Assert.DoesNotContain(Jobs(d), j => j.Kind == JobKind.UpsertUser);
            Assert.Contains(Jobs(d), j => j.Kind == JobKind.DeleteUser);
        }
    }

    [Fact]
    public async Task Capture_semaphore_released_after_device_throws()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        var fake = _h.Factory.For(_d1);
        fake.Offline = true;
        await Assert.ThrowsAsync<HikTransientException>(() => _svc.CaptureFaceAsync("2001", _d1, CancellationToken.None));
        fake.Offline = false;
        await _svc.CaptureFaceAsync("2001", _d1, CancellationToken.None);
        Assert.NotNull(_h.Db.Biometrics.GetFace("2001"));
    }

    [Fact]
    public void Delete_enqueues_deleteuser_and_removes_person()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _h.AddFace("2001");
        _svc.Delete("2001");
        Assert.Null(_h.Db.People.Get("2001"));
        Assert.Null(_h.Db.Biometrics.GetFace("2001"));
        Assert.Contains(Jobs(_d1), j => j.Kind == JobKind.DeleteUser);
        Assert.Contains(Jobs(_d2), j => j.Kind == JobKind.DeleteUser);
    }
}
