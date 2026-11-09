using System.Text.Json.Nodes;
using Acs.Core.Cloud;
using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Tests.Cloud;

public sealed class SyncTablesTests : IDisposable
{
    private const char S = OutboxStore.Sep;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acs-st-" + Guid.NewGuid().ToString("N"));
    private readonly Database _a, _b;
    private static DateTime Now => new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);

    public SyncTablesTests()
    {
        _a = new Database(Path.Combine(_dir, "a.db"), new DataProtector(Enumerable.Repeat((byte)1, 32).ToArray()));
        _b = new Database(Path.Combine(_dir, "b.db"), new DataProtector(Enumerable.Repeat((byte)2, 32).ToArray()));
    }

    public void Dispose() { _a.Dispose(); _b.Dispose(); SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static PersonRecord Person(string no, int? group = null) =>
        new(no, "Nguyễn Văn " + no, group, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), PersonStatus.Active, Now, Now, null);
    private static DeviceRecord Dev(string group = "device-1", string pw = "Secret#1") =>
        new(0, "Máy tầng trệt", "10.9.0.84", "admin", pw, 1, true, false, "SH-K1T9342MFWX", "SN1", "V4.39", group, false, Now);

    /// <summary>Copies one key from A to B the way the sync engine does (JSON in between).</summary>
    private ApplyResult Copy(string tbl, string key)
    {
        var t = SyncTables.Get(tbl);
        var row = t.Read(_a, key);
        var json = row is null ? null : JsonNode.Parse(row.ToJsonString())!.AsObject();   // through text, like the cloud
        return _b.Outbox.Applying(() => t.Apply(_b, key, json));
    }

    [Fact]
    public void apply_order_puts_parents_first()
    {
        var names = SyncTables.All.Select(t => t.Name).ToList();
        Assert.Equal(["settings", "groups", "devices", "people", "faces", "fingerprints", "device_state", "jobs", "trash", "log"], names);
    }

    [Fact]
    public void people_round_trip_with_group_by_name()
    {
        var g = _a.People.GetOrCreateGroup("CRAF");
        _b.People.GetOrCreateGroup("OTHER");   // B's ids differ from A's
        _a.People.Insert(Person("118", g));
        Assert.Equal(ApplyResult.Applied, Copy("groups", "CRAF"));
        Assert.Equal(ApplyResult.Applied, Copy("people", "118"));
        var p = _b.People.Get("118")!;
        Assert.Equal("Nguyễn Văn 118", p.FullName);
        Assert.Equal("CRAF", _b.People.GroupName(p.GroupId));
        Assert.Equal(new DateOnly(2027, 1, 1), p.ValidTo);
        Assert.False(_b.People.DraftFlag("118"));
    }

    [Fact]
    public void drafts_are_not_read()
    {
        _a.People.Insert(Person("9"));
        _a.People.SetDraft("9", true);
        Assert.Null(SyncTables.Get("people").Read(_a, "9"));
    }

    [Fact]
    public void face_and_fingers_round_trip_bytes_equal()
    {
        _a.People.Insert(Person("5")); _b.People.Insert(Person("5"));
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 0xFF, 0xD9 };
        _a.Biometrics.SetFace(new FaceRecord("5", jpeg, BiometricSource.Device, Now));
        _a.Biometrics.SetFinger(new FingerRecord("5", 2, "device-1", Convert.ToBase64String([7, 8, 9]), 80, BiometricSource.Device, Now));
        Assert.Equal(ApplyResult.Applied, Copy("faces", "5"));
        Assert.Equal(ApplyResult.Applied, Copy("fingerprints", $"5{S}2{S}device-1"));
        Assert.Equal(jpeg, _b.Biometrics.GetFace("5")!.Jpeg);
        var f = _b.Biometrics.GetFingers("5").Single();
        Assert.Equal((2, "device-1", Convert.ToBase64String([7, 8, 9]), (int?)80), (f.FingerId, f.FpGroup, f.TemplateBase64, f.Quality));
    }

    [Fact]
    public void face_for_a_missing_person_defers()
    {
        _a.People.Insert(Person("6"));
        _a.Biometrics.SetFace(new FaceRecord("6", [0xFF, 0xD8, 0xFF, 0xD9], BiometricSource.Device, Now));
        Assert.Equal(ApplyResult.Deferred, Copy("faces", "6"));
        Assert.Null(_b.Biometrics.GetFace("6"));
    }

    [Fact]
    public void device_round_trip_keeps_password_and_local_status()
    {
        _a.Devices.Insert(Dev(pw: "MatKhau#9"));
        var bid = _b.Devices.Insert(Dev(pw: "cu") with { Name = "Cũ", Model = "B-MODEL", AuthPaused = true });
        Assert.Equal(ApplyResult.Applied, Copy("devices", "device-1"));
        var d = _b.Devices.Get(bid)!;
        Assert.Equal(("Máy tầng trệt", "MatKhau#9", "B-MODEL", true), (d.Name, d.Password, d.Model, d.AuthPaused));
        Assert.Null(SyncTables.Get("devices").Read(_a, "device-1")!["last_seen"]);
    }

    [Fact]
    public void new_device_is_inserted_with_the_same_fp_group()
    {
        _a.Devices.Insert(Dev("device-ab12cd34"));
        Assert.Equal(ApplyResult.Applied, Copy("devices", "device-ab12cd34"));
        Assert.Equal("10.9.0.84", _b.Devices.List().Single(x => x.FpGroup == "device-ab12cd34").Host);
    }

    [Fact]
    public void device_state_and_jobs_map_device_by_fp_group()
    {
        var aid = _a.Devices.Insert(Dev("device-2"));
        _b.Devices.Insert(Dev("device-1"));
        var bid = _b.Devices.Insert(Dev("device-2"));   // B's id for device-2 is 2, A's is 1
        _a.People.Insert(Person("3")); _b.People.Insert(Person("3"));
        _a.Jobs.Enqueue(aid, "3", JobKind.SetFace, 0, Now);
        _a.Jobs.SetState(new DeviceStateRecord("3", aid, true, true, true, new HashSet<int> { 1 }, new HashSet<int>(), Now));
        var created = (string)_a.Scalar("SELECT created_at FROM jobs")!;
        Assert.Equal(ApplyResult.Applied, Copy("jobs", $"device-2{S}3{S}SetFace{S}0{S}{created}"));
        Assert.Equal(ApplyResult.Applied, Copy("device_state", $"3{S}device-2"));
        Assert.Equal(bid, _b.Jobs.NextDue(bid, Now.AddMinutes(1))!.DeviceId);
        Assert.Equal(new HashSet<int> { 1 }, _b.Jobs.GetState("3", bid)!.Fingers);
    }

    [Fact]
    public void missing_device_defers()
    {
        var aid = _a.Devices.Insert(Dev("device-5"));
        _a.People.Insert(Person("3")); _b.People.Insert(Person("3"));
        _a.Jobs.SetState(new DeviceStateRecord("3", aid, true, true, false, new HashSet<int>(), new HashSet<int>(), Now));
        Assert.Equal(ApplyResult.Deferred, Copy("device_state", $"3{S}device-5"));
    }

    [Fact]
    public void delete_removes_local_row()
    {
        _b.People.Insert(Person("8"));
        Assert.Equal(ApplyResult.Applied, Copy("people", "8"));   // A has no person 8: a tombstone
        Assert.Null(_b.People.Get("8"));
    }

    [Fact]
    public void trash_and_log_round_trip()
    {
        var aid = _a.Devices.Insert(Dev("device-1"));
        _b.Devices.Insert(Dev("device-1"));
        _a.Trash.Add(new TrashedPerson(Person("44"), "CRAF", null, []), Now);
        _a.Log.Add("44", aid, "Xoá hẳn", true, "ok");
        var origin = (string)_a.Scalar("SELECT origin FROM log")!;
        Assert.Equal(ApplyResult.Applied, Copy("trash", $"44{S}{Database.Ts(Now)}"));
        Assert.Equal(ApplyResult.Applied, Copy("log", origin));
        var t = _b.Trash.List().Single();
        Assert.Equal(("44", "CRAF"), (t.EmployeeNo, t.GroupName));
        Assert.Equal("Nguyễn Văn 44", _b.Trash.Get(t.Id)!.Person.FullName);
        var e = _b.Log.List(null, null, 5).Single();
        Assert.Equal(("44", "Xoá hẳn", Environment.MachineName), (e.EmployeeNo, e.Action, e.Pc));
        Assert.Equal(_b.Devices.List().Single().Id, e.DeviceId);
        Assert.Equal(ApplyResult.Applied, Copy("log", origin));   // applying twice keeps one line
        Assert.Single(_b.Log.List(null, null, 5));
    }

    [Fact]
    public void open_job_clash_updates_existing_job()
    {
        var aid = _a.Devices.Insert(Dev("device-1"));
        var bid = _b.Devices.Insert(Dev("device-1"));
        _a.People.Insert(Person("3")); _b.People.Insert(Person("3"));
        _b.Jobs.Enqueue(bid, "3", JobKind.UpsertUser, 0, Now.AddMinutes(-5));
        _a.Jobs.Enqueue(aid, "3", JobKind.UpsertUser, 0, Now);
        var created = (string)_a.Scalar("SELECT created_at FROM jobs")!;
        Assert.Equal(ApplyResult.Applied, Copy("jobs", $"device-1{S}3{S}UpsertUser{S}0{S}{created}"));
        Assert.Equal(1L, _b.Scalar("SELECT COUNT(*) FROM jobs"));
        Assert.Equal(created, (string)_b.Scalar("SELECT created_at FROM jobs")!);
    }

    [Fact]
    public void settings_high_water_round_trip()
    {
        _a.Settings.Set(PeopleStore.HighWaterKey, "180");
        Assert.Equal(ApplyResult.Applied, Copy("settings", PeopleStore.HighWaterKey));
        Assert.Equal("180", _b.Settings.Get(PeopleStore.HighWaterKey));
    }
}
