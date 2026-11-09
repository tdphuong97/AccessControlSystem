using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Tests.Storage;

public sealed class OutboxTests : IDisposable
{
    private const char S = '\u001f';
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acs-ob-" + Guid.NewGuid().ToString("N"));
    private readonly DataProtector _prot = new(new byte[32]);
    private string DbFile => Path.Combine(_dir, "acs.db");
    private Database Open() => new(DbFile, _prot);
    private static DateTime Now => DateTime.UtcNow;
    private static PersonRecord Person(string no) => new(no, "Nguyễn Văn A", null, new DateOnly(2026, 1, 1), null, PersonStatus.Active, Now, Now, null);
    private static DeviceRecord Dev(string group = "device-1") =>
        new(0, "Máy tầng trệt", "192.168.1.201", "admin", "Secret#1", 1, true, false, null, null, null, group, false, null);
    private static HashSet<string> Keys(Database db) => db.Outbox.Take(1000).Select(x => x.Tbl + ":" + x.Key).ToHashSet();

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void migrates_v2_database_and_keeps_rows()
    {
        using (var db = Open())
        {
            db.People.Insert(Person("7"));
            db.Log.Add("7", null, "Thử", true);
        }
        using (var c = new SqliteConnection($"Data Source={DbFile};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            var drops = string.Join(";", new[] { "people", "groups", "faces", "fingerprints", "devices", "device_state", "jobs", "trash", "log", "settings" }
                .SelectMany(t => new[] { "i", "u", "d", "bd" }.Select(op => $"DROP TRIGGER IF EXISTS ob_{t}_{op}")));
            cmd.CommandText = drops + "; DROP TABLE outbox; DROP TABLE sync_rows; DROP TABLE sync_flags; DROP TABLE sync_deferred;" +
                "ALTER TABLE log DROP COLUMN pc; ALTER TABLE log DROP COLUMN origin; PRAGMA user_version=2;";
            cmd.ExecuteNonQuery();
        }
        using var again = Open();
        Assert.NotNull(again.People.Get("7"));
        Assert.False(again.Outbox.Capture);
        Assert.Equal(0, again.Outbox.Count());
        Assert.Single(again.Log.List(null, null, 10));
        Assert.Equal(1L, again.Scalar("SELECT COUNT(*) FROM log WHERE origin IS NOT NULL"));
    }

    [Fact]
    public void capture_off_records_nothing()
    {
        using var db = Open();
        db.People.Insert(Person("1"));
        db.Devices.Insert(Dev());
        Assert.Equal(0, db.Outbox.Count());
    }

    [Fact]
    public void insert_update_delete_people_record_keys()
    {
        using var db = Open();
        db.Outbox.Capture = true;
        db.People.Insert(Person("1"));
        Assert.Equal(["people:1"], Keys(db));
        db.Outbox.Remove("people", "1");
        db.People.Update(Person("1") with { FullName = "Trần B" });
        Assert.Equal(["people:1"], Keys(db));
        db.Outbox.Remove("people", "1");
        db.People.Delete("1");
        Assert.Equal(["people:1"], Keys(db));
    }

    [Fact]
    public void draft_person_is_not_recorded_until_it_stops_being_a_draft()
    {
        using var db = Open();
        db.Outbox.Capture = true;
        db.Outbox.Applying(() => { db.People.Insert(Person("5")); db.People.SetDraft("5", true); return 0; });
        db.People.Update(Person("5") with { FullName = "Nháp" });
        Assert.Equal(0, db.Outbox.Count());
        db.People.SetDraft("5", false);
        Assert.Equal(["people:5"], Keys(db));
    }

    [Fact]
    public void renumber_records_old_and_new_key()
    {
        using var db = Open();
        db.People.Insert(Person("10"));
        db.Outbox.Capture = true;
        db.Exec("UPDATE people SET employee_no='11' WHERE employee_no='10'");
        Assert.Equal(["people:10", "people:11"], Keys(db));
    }

    [Fact]
    public void device_ping_does_not_record()
    {
        using var db = Open();
        var id = db.Devices.Insert(Dev());
        db.Outbox.Capture = true;
        db.Devices.SetSeen(id, null, Now);
        db.Devices.SetAuthPaused(id, true);
        Assert.Equal(0, db.Outbox.Count());
    }

    [Fact]
    public void device_rename_records()
    {
        using var db = Open();
        var id = db.Devices.Insert(Dev());
        db.Outbox.Capture = true;
        db.Devices.Update(db.Devices.Get(id)! with { Name = "Trệt" });
        Assert.Equal(["devices:device-1"], Keys(db));
    }

    [Fact]
    public void job_and_device_state_keys_use_fp_group()
    {
        using var db = Open();
        var id = db.Devices.Insert(Dev("device-7"));
        db.People.Insert(Person("3"));
        db.Outbox.Capture = true;
        db.Jobs.Enqueue(id, "3", JobKind.SetFace, 0, Now);
        db.Jobs.SetState(new DeviceStateRecord("3", id, true, true, true, new HashSet<int>(), new HashSet<int>(), Now));
        var created = (string)db.Scalar("SELECT created_at FROM jobs")!;
        Assert.Contains($"jobs:device-7{S}3{S}SetFace{S}0{S}{created}", Keys(db));
        Assert.Contains($"device_state:3{S}device-7", Keys(db));
    }

    [Fact]
    public void deleting_a_device_records_its_jobs_and_device_states()
    {
        using var db = Open();
        var id = db.Devices.Insert(Dev("device-9"));
        db.People.Insert(Person("4"));
        db.Jobs.Enqueue(id, "4", JobKind.UpsertUser, 0, Now);
        db.Jobs.SetState(new DeviceStateRecord("4", id, true, true, false, new HashSet<int>(), new HashSet<int>(), Now));
        db.Outbox.Capture = true;
        db.Devices.Delete(id);
        var k = Keys(db);
        Assert.Contains("devices:device-9", k);
        Assert.Contains($"device_state:4{S}device-9", k);
        Assert.Contains(k, x => x.StartsWith($"jobs:device-9{S}4{S}UpsertUser"));
    }

    [Fact]
    public void log_rows_have_pc_and_origin()
    {
        using var db = Open();
        db.Outbox.Capture = true;
        db.Log.Add(null, null, "Thử", true);
        var e = db.Log.List(null, null, 1).Single();
        Assert.Equal(Environment.MachineName, e.Pc);
        var origin = (string)db.Scalar("SELECT origin FROM log")!;
        Assert.Equal(32, origin.Length);
        Assert.Equal(["log:" + origin], Keys(db));
    }

    [Fact]
    public void only_high_water_setting_is_recorded()
    {
        using var db = Open();
        db.Outbox.Capture = true;
        db.Settings.Set("backup.extraFolder", "E:\\x");
        db.Settings.Set(PeopleStore.HighWaterKey, "180");
        Assert.Equal(["settings:" + PeopleStore.HighWaterKey], Keys(db));
    }

    [Fact]
    public void Applying_suspends_capture()
    {
        using var db = Open();
        db.Outbox.Capture = true;
        db.Outbox.Applying(() => { db.People.Insert(Person("2")); return 0; });
        Assert.Equal(0, db.Outbox.Count());
        Assert.True(db.Outbox.Capture);
        db.People.Insert(Person("3"));
        Assert.Equal(1, db.Outbox.Count());
    }

    [Fact]
    public void MarkAll_lists_every_shared_row_except_drafts()
    {
        using var db = Open();
        var id = db.Devices.Insert(Dev());
        db.People.Insert(Person("1"));
        db.People.Insert(Person("2"));
        db.People.SetDraft("2", true);
        db.Biometrics.SetFace(new FaceRecord("1", [0xFF, 0xD8, 0xFF, 0xD9], BiometricSource.Device, Now));
        db.Settings.Set(PeopleStore.HighWaterKey, "5");
        db.Log.Add("1", id, "Thử", true);
        db.Outbox.MarkAll();
        var k = Keys(db);
        Assert.Contains("people:1", k);
        Assert.DoesNotContain("people:2", k);
        Assert.Contains("faces:1", k);
        Assert.Contains("devices:device-1", k);
        Assert.Contains("settings:" + PeopleStore.HighWaterKey, k);
        Assert.Contains(k, x => x.StartsWith("log:"));
    }

    [Fact]
    public void known_versions_round_trip()
    {
        using var db = Open();
        Assert.Null(db.Outbox.KnownVer("people", "1"));
        db.Outbox.SetKnown("people", "1", "rid", 12);
        Assert.Equal(12, db.Outbox.KnownVer("people", "1"));
        db.Outbox.ClearKnown();
        Assert.Null(db.Outbox.KnownVer("people", "1"));
    }
}
