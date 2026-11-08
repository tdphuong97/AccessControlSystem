using Acs.Core.Enroll;
using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Tests.Sync;

namespace Acs.Core.Tests.Enroll;

/// <summary>Thùng rác (deleted people kept until emptied), new people without face/finger (drafts), numbering.</summary>
public sealed class TrashAndDraftTests : IAsyncDisposable
{
    private readonly SyncHarness _h = new();
    private readonly EnrollService _svc;
    private readonly int _d1, _d2;
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    public TrashAndDraftTests()
    {
        _d1 = _h.AddDevice("A", "ModelA");
        _d2 = _h.AddDevice("B", "ModelB");
        _svc = new EnrollService(_h.Db, _h.Factory, _h.Engine, _h.Time);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private IReadOnlyList<JobRecord> Jobs(int dev) => _h.Db.Jobs.ListOpen(dev);

    private void Drain()
    {
        foreach (var d in new[] { _d1, _d2 })
            foreach (var j in Jobs(d).ToList()) { _h.Db.Jobs.MarkRunning(j, _h.Now); _h.Db.Jobs.MarkDone(j.Id, _h.Now); }
    }

    private void Sent(string no, string name, string? group = null)
    {
        _svc.CreatePerson(new NewPerson(name, no, group, null));
        _svc.SaveFace(no, Jpeg, BiometricSource.File);
    }

    // ---- numbering: the highest number in use + 1 ----

    [Fact]
    public void A_deleted_last_number_is_suggested_again_once_the_devices_removed_it()
    {
        Sent("2001", "An");
        Sent("2002", "Bình");
        _svc.Delete("2002");
        Assert.Equal("2003", _svc.NextEmployeeNo());   // still being removed from the devices
        Drain();
        Assert.Equal("2002", _svc.NextEmployeeNo());
    }

    [Fact]
    public void A_number_seen_on_a_device_is_never_suggested()
    {
        Sent("2001", "An");
        _h.Db.People.BumpHighWater("2005");   // a stranger 2005 seen on a device
        Assert.Equal("2006", _svc.NextEmployeeNo());
    }

    // ---- drafts ----

    [Fact]
    public void A_draft_is_discarded_without_trash_or_any_device_work()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Assert.True(_svc.IsDraft("2001"));

        Assert.True(_svc.DiscardDraft("2001"));

        Assert.Null(_h.Db.People.Get("2001"));
        Assert.Empty(_h.Db.Trash.List());
        Assert.Empty(Jobs(_d1));
        Assert.Equal("1001", _svc.NextEmployeeNo());   // nothing in use any more
    }

    [Fact]
    public void A_person_with_a_face_is_not_a_draft_and_is_never_discarded()
    {
        Sent("2001", "An");
        Assert.False(_svc.IsDraft("2001"));
        Assert.False(_svc.DiscardDraft("2001"));
        Assert.NotNull(_h.Db.People.Get("2001"));
    }

    [Fact]
    public void Deleting_a_draft_just_drops_it()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        _svc.Delete("2001");
        Assert.Null(_h.Db.People.Get("2001"));
        Assert.Empty(_h.Db.Trash.List());
        Assert.Empty(Jobs(_d1));
    }

    [Fact]
    public void Purge_drops_every_draft_and_keeps_the_rest()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", null, null));
        Sent("2002", "Bình");
        _h.AddPerson("2003", "Cường");   // imported without face or finger: on a device
        _h.Db.Jobs.SetState(new DeviceStateRecord("2003", _d1, true, true, false, new HashSet<int>(), new HashSet<int>(), _h.Now));

        Assert.Equal(1, _svc.PurgeDrafts());

        Assert.Null(_h.Db.People.Get("2001"));
        Assert.NotNull(_h.Db.People.Get("2002"));
        Assert.NotNull(_h.Db.People.Get("2003"));
    }

    [Fact]
    public void A_draft_can_take_another_number_and_a_sent_person_cannot()
    {
        _svc.CreatePerson(new NewPerson("An", "2001", "Kho", null));
        var p = _svc.Renumber("2001", "1999");
        Assert.Equal("1999", p.EmployeeNo);
        Assert.Null(_h.Db.People.Get("2001"));
        Assert.Equal("Kho", _h.Db.People.GroupName(_h.Db.People.Get("1999")!.GroupId));
        Assert.True(_svc.IsDraft("1999"));

        Sent("2002", "Bình");
        Assert.Throws<EnrollException>(() => _svc.Renumber("2002", "2010"));
        Assert.Throws<EnrollException>(() => _svc.Renumber("1999", "2002"));   // taken
    }

    // ---- trash ----

    [Fact]
    public void Delete_keeps_the_person_face_and_fingers_in_the_trash_and_restore_sends_them_again()
    {
        Sent("2001", "An", "Kho");
        _h.AddFinger("2001", 1, "ModelA");
        Drain();
        _svc.Delete("2001");
        Assert.All(new[] { _d1, _d2 }, d => Assert.Contains(Jobs(d), j => j.Kind == JobKind.DeleteUser));
        var t = Assert.Single(_h.Db.Trash.List());
        Assert.Equal(("2001", "An", "Kho", true, 1), (t.EmployeeNo, t.FullName, t.GroupName, t.HasFace, t.FingerCount));
        Drain();

        var p = _svc.Restore(t.Id, null);

        Assert.Equal("2001", p.EmployeeNo);
        Assert.Equal("Kho", _h.Db.People.GroupName(p.GroupId));
        Assert.Equal(Jpeg, _h.Db.Biometrics.GetFace("2001")!.Jpeg);
        Assert.Single(_h.Db.Biometrics.GetFingers("2001"));
        Assert.Empty(_h.Db.Trash.List());
        foreach (var d in new[] { _d1, _d2 })
            Assert.Equal([JobKind.UpsertUser, JobKind.SetFace, JobKind.SetFinger], Jobs(d).OrderBy(j => j.Id).Select(j => j.Kind).ToArray());
    }

    [Fact]
    public void Restore_waits_while_the_devices_still_remove_the_number()
    {
        Sent("2001", "An");
        _svc.Delete("2001");
        var id = _h.Db.Trash.List().Single().Id;
        var ex = Assert.Throws<EnrollException>(() => _svc.Restore(id, null));
        Assert.Equal(EnrollService.NumberBusyMessage, ex.Message);
    }

    [Fact]
    public void Restore_into_a_number_now_used_by_someone_else_needs_another_number()
    {
        Sent("2001", "An");
        _svc.Delete("2001");
        Drain();
        Sent("2001", "Bình");   // the number was given to someone new
        var id = _h.Db.Trash.List().Single().Id;

        var ex = Assert.Throws<EnrollException>(() => _svc.Restore(id, null));
        Assert.Contains("Bình", ex.Message);

        var p = _svc.Restore(id, "2050");
        Assert.Equal(("2050", "An"), (p.EmployeeNo, p.FullName));
        Assert.Equal("Bình", _h.Db.People.Get("2001")!.FullName);
    }

    [Fact]
    public void Removing_from_the_trash_is_for_good()
    {
        Sent("2001", "An");
        Sent("2002", "Bình");
        _svc.Delete("2001");
        _svc.Delete("2002");
        _h.Db.Trash.Remove(_h.Db.Trash.List().First(t => t.EmployeeNo == "2001").Id);
        Assert.Equal(["2002"], _h.Db.Trash.List().Select(t => t.EmployeeNo).ToArray());
        Assert.Equal(1, _h.Db.Trash.Clear());
        Assert.Empty(_h.Db.Trash.List());
    }
}
