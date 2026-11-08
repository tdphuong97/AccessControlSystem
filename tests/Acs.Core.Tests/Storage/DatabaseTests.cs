using System.Text;
using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Tests.Storage;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acs-test-" + Guid.NewGuid().ToString("N"));
    private readonly DataProtector _prot = new(new byte[32]);
    private string DbFile => Path.Combine(_dir, "acs.db");
    private Database Open() => new(DbFile, _prot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static DateTime Now => DateTime.UtcNow;
    private static PersonRecord Person(string no, string name = "Nguyễn Văn A") =>
        new(no, name, null, new DateOnly(2026, 1, 1), null, PersonStatus.Active, Now, Now, null);
    private static DeviceRecord Dev(string pw = "Secret#1") =>
        new(0, "Máy tầng trệt", "192.168.1.201", "admin", pw, 1, true, false, null, null, null, "device-1", false, null);

    [Fact]
    public void Upgrading_a_version_1_database_marks_only_never_sent_people_without_face_or_finger_as_drafts()
    {
        using (var db = Open())
        {
            var dev = db.Devices.Insert(Dev());
            foreach (var no in new[] { "1", "2", "3", "4" }) db.People.Insert(Person(no));
            db.Biometrics.SetFace(new FaceRecord("2", [0xFF, 0xD8, 0xFF, 0xD9], BiometricSource.File, Now));          // has a face
            db.Jobs.SetState(new DeviceStateRecord("3", dev, true, true, false, new HashSet<int>(), new HashSet<int>(), Now)); // on a device
            db.Jobs.Enqueue(dev, "4", JobKind.UpsertUser, 0, Now);                                                       // was sent
        }
        using (var raw = new SqliteConnection($"Data Source={DbFile};Pooling=False"))
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "DROP TABLE trash; ALTER TABLE people DROP COLUMN draft; PRAGMA user_version=1;";
            cmd.ExecuteNonQuery();
        }

        using var again = Open();
        Assert.Equal(["1"], again.People.DraftNumbers().ToArray());
        Assert.Empty(again.Trash.List());
    }

    [Fact]
    public void Device_password_is_not_stored_in_plain_text()
    {
        int id;
        using (var db = Open())
        {
            id = db.Devices.Insert(Dev());
            Assert.Equal("Secret#1", db.Devices.Get(id)!.Password);
        }
        using var raw = new SqliteConnection($"Data Source={DbFile};Pooling=False");
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = "SELECT password FROM devices WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id);
        var blob = (byte[])cmd.ExecuteScalar()!;
        Assert.DoesNotContain("Secret#1", Encoding.Latin1.GetString(blob));
    }

    [Fact]
    public void EmployeeNo_keeps_leading_zeros()
    {
        using var db = Open();
        db.People.Insert(Person("00123"));
        Assert.NotNull(db.People.Get("00123"));
        Assert.Equal("00123", Assert.Single(db.People.List(null, null)).EmployeeNo);
    }

    [Theory]
    [InlineData("", "1001")]
    [InlineData("1136,1137", "1138")]
    [InlineData("0099", "0100")]
    [InlineData("NV-7,1001", "1002")]
    public void NextEmployeeNo_cases(string existing, string expected)
    {
        using var db = Open();
        foreach (var e in existing.Split(',', StringSplitOptions.RemoveEmptyEntries)) db.People.Insert(Person(e));
        Assert.Equal(expected, db.People.NextEmployeeNo());
    }

    [Fact]
    public void Deleting_person_cascades_face_fingers_state()
    {
        using var db = Open();
        var dev = db.Devices.Insert(Dev());
        db.People.Insert(Person("1001"));
        db.Biometrics.SetFace(new("1001", [0xFF, 0xD8, 0xFF, 1], BiometricSource.File, Now));
        db.Biometrics.SetFinger(new("1001", 2, "g", Convert.ToBase64String([1, 2, 3]), 80, BiometricSource.Device, Now));
        db.Jobs.SetState(new("1001", dev, true, true, true, new HashSet<int> { 2 }, new HashSet<int>(), Now));
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), db.Biometrics.GetFingers("1001").Single().TemplateBase64);
        Assert.Equal(new HashSet<int> { 2 }, db.Jobs.GetState("1001", dev)!.Fingers);

        db.People.Delete("1001");

        Assert.Null(db.Biometrics.GetFace("1001"));
        Assert.Empty(db.Biometrics.GetFingers("1001"));
        Assert.Null(db.Jobs.GetState("1001", dev));
    }

    [Fact]
    public void Migration_sets_the_current_user_version_and_is_idempotent()
    {
        using (var db = Open()) db.Settings.Set("k", "v");
        using (var db = Open()) Assert.Equal("v", db.Settings.Get("k"));
        using var raw = new SqliteConnection($"Data Source={DbFile};Pooling=False");
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Jobs_roundtrip_and_open_index_blocks_duplicates()
    {
        using var db = Open();
        var dev = db.Devices.Insert(Dev());
        db.People.Insert(Person("1001"));
        var job = new JobRecord(0, dev, "1001", JobKind.SetFace, 0, JobStatus.Pending, 0, Now, null, null, null);
        var id = db.Jobs.Insert(job);
        Assert.Equal(JobKind.SetFace, db.Jobs.Get(id)!.Kind);
        Assert.Single(db.Jobs.ListOpen(dev));
        Assert.Throws<SqliteException>(() => db.Jobs.Insert(job));
    }
}
