using Acs.Core.Backup;
using Acs.Core.Model;
using Acs.Core.Security;
using Acs.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Acs.Core.Tests.Backup;

public sealed class BackupServiceTests : IDisposable
{
    private const int Iter = 1000; // production uses 600 000; tests keep it fast
    private readonly string _root = Path.Combine(Path.GetTempPath(), "acs-bak-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));
    private readonly List<Database> _dbs = [];

    private sealed class PassThrough : IKeyProtector
    {
        public byte[] Protect(byte[] data) => (byte[])data.Clone();
        public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
    }

    public void Dispose()
    {
        foreach (var d in _dbs) d.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private (Database Db, KeyStore Keys, BackupService Svc, string Folder) Make(string name)
    {
        var dir = Path.Combine(_root, name);
        var keys = new KeyStore(Path.Combine(dir, "keys"), new PassThrough());
        var db = new Database(Path.Combine(dir, "acs.db"), new DataProtector(keys.GetOrCreateDataKey()));
        _dbs.Add(db);
        var folder = Path.Combine(dir, "backups");
        return (db, keys, new BackupService(db, keys, folder, _time, Iter), folder);
    }

    private static byte[] FakeJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFF, 0xD9];

    private static void AddPerson(Database db, string no, string name)
    {
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        db.People.Insert(new PersonRecord(no, name, null, new DateOnly(2026, 11, 6), null, PersonStatus.Active, now, now, null));
        db.Biometrics.SetFace(new FaceRecord(no, FakeJpeg(), BiometricSource.File, now));
    }

    [Fact]
    public void Backup_restore_round_trip_on_new_keystore()
    {
        var a = Make("a");
        AddPerson(a.Db, "1001", "Nguyễn Văn A");
        a.Svc.SetPassword("mat-khau-12345");
        var file = a.Svc.Create("manual");

        var newDir = Path.Combine(_root, "b");
        var keysB = new KeyStore(Path.Combine(newDir, "keys"), new PassThrough());
        var dbFileB = Path.Combine(newDir, "acs.db");
        var svcB = new BackupService(null!, keysB, Path.Combine(newDir, "backups"), _time, Iter);
        svcB.Restore(file, "mat-khau-12345", dbFileB);

        using var restored = new Database(dbFileB, new DataProtector(keysB.GetOrCreateDataKey()));
        Assert.Equal("Nguyễn Văn A", restored.People.Get("1001")!.FullName);
        Assert.Equal(FakeJpeg(), restored.Biometrics.GetFace("1001")!.Jpeg);
        Assert.NotNull(keysB.GetBackupKey()); // the new PC can keep backing up
    }

    [Fact]
    public void Wrong_password_throws_BackupException_and_leaves_no_file()
    {
        var a = Make("a");
        a.Svc.SetPassword("mat-khau-12345");
        var file = a.Svc.Create("manual");
        var target = Path.Combine(_root, "c", "acs.db");
        var ex = Assert.Throws<BackupException>(() => a.Svc.Restore(file, "sai-mat-khau-999", target));
        Assert.Equal("Mật khẩu sao lưu không đúng hoặc file đã hỏng.", ex.Message);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Corrupt_file_throws_BackupException()
    {
        var a = Make("a");
        var bad = Path.Combine(_root, "bad.acsbak");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(bad, [1, 2, 3]);
        Assert.Throws<BackupException>(() => a.Svc.Restore(bad, "mat-khau-12345", Path.Combine(_root, "x.db")));
    }

    [Fact]
    public void Short_password_is_rejected_and_create_needs_a_password()
    {
        var a = Make("a");
        Assert.Throws<BackupException>(() => a.Svc.SetPassword("1234567"));
        Assert.False(a.Svc.HasPassword);
        Assert.Throws<BackupException>(() => a.Svc.Create("manual"));
    }

    [Fact]
    public void Keeps_14_newest()
    {
        var a = Make("a");
        a.Svc.SetPassword("mat-khau-12345");
        for (var i = 0; i < 17; i++)
        {
            a.Svc.Create("daily");
            _time.Advance(TimeSpan.FromMinutes(1));
        }
        var list = a.Svc.List();
        Assert.Equal(14, list.Count);
        Assert.True(list[0].At > list[^1].At); // newest first
        Assert.Equal(14, Directory.GetFiles(a.Folder, "*.acsbak").Length);
    }

    [Fact]
    public void Backup_file_contains_no_plain_jpeg_magic_and_follows_the_name_format()
    {
        var a = Make("a");
        AddPerson(a.Db, "1001", "A");
        a.Svc.SetPassword("mat-khau-12345");
        var file = a.Svc.Create("Trước khi/cập nhật!");
        var bytes = File.ReadAllBytes(file);
        Assert.Equal("ACSB"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, bytes[4]);
        Assert.Equal(-1, bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xD8, 0xFF }));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("SQLite format 3"u8));
        Assert.Matches(@"^ACS-\d{8}-\d{6}-[a-z0-9-]+\.acsbak$", Path.GetFileName(file));
    }

    [Fact]
    public void Copies_to_the_extra_folder_when_set()
    {
        var a = Make("a");
        a.Svc.SetPassword("mat-khau-12345");
        var extra = Path.Combine(_root, "extra");
        a.Db.Settings.Set("backup.extraFolder", extra);
        var file = a.Svc.Create("manual");
        Assert.True(File.Exists(Path.Combine(extra, Path.GetFileName(file))));
    }
}
