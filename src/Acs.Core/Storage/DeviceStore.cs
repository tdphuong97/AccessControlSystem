using System.Text;
using Acs.Core.Hik;
using Acs.Core.Model;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Storage;

public sealed class DeviceStore
{
    private readonly Database _db;
    internal DeviceStore(Database db) => _db = db;

    private const string Cols = "id, name, host, username, password, door_no, is_capture_default, is_finger_reader, model, serial, firmware, fp_group, auth_paused, last_seen";

    private DeviceRecord Map(SqliteDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
        Encoding.UTF8.GetString(_db.Protector.Open((byte[])r["password"])), r.GetInt32(5),
        r.GetInt32(6) != 0, r.GetInt32(7) != 0, Database.NStr(r, "model"), Database.NStr(r, "serial"),
        Database.NStr(r, "firmware"), r.GetString(11), r.GetInt32(12) != 0, Database.NTs(r, "last_seen"));

    private byte[] Seal(string pw) => _db.Protector.Seal(Encoding.UTF8.GetBytes(pw));

    public int Insert(DeviceRecord d) => (int)_db.InsertId(
        "INSERT INTO devices(name, host, username, password, door_no, is_capture_default, is_finger_reader, model, serial, firmware, fp_group, auth_paused, last_seen, created_at) VALUES($n,$h,$u,$p,$d,$c,$f,$m,$s,$fw,$g,$a,$l,$t)",
        ("$n", d.Name), ("$h", d.Host), ("$u", d.Username), ("$p", Seal(d.Password)), ("$d", d.DoorNo),
        ("$c", d.IsCaptureDefault ? 1 : 0), ("$f", d.IsFingerReader ? 1 : 0), ("$m", d.Model), ("$s", d.Serial),
        ("$fw", d.Firmware), ("$g", d.FpGroup), ("$a", d.AuthPaused ? 1 : 0),
        ("$l", d.LastSeen is { } l ? Database.Ts(l) : null), ("$t", Database.Ts(DateTime.UtcNow)));

    public void Update(DeviceRecord d) => _db.Exec(
        "UPDATE devices SET name=$n, host=$h, username=$u, password=$p, door_no=$d, is_capture_default=$c, is_finger_reader=$f, model=$m, serial=$s, firmware=$fw, fp_group=$g, auth_paused=$a, last_seen=$l WHERE id=$id",
        ("$id", d.Id), ("$n", d.Name), ("$h", d.Host), ("$u", d.Username), ("$p", Seal(d.Password)), ("$d", d.DoorNo),
        ("$c", d.IsCaptureDefault ? 1 : 0), ("$f", d.IsFingerReader ? 1 : 0), ("$m", d.Model), ("$s", d.Serial),
        ("$fw", d.Firmware), ("$g", d.FpGroup), ("$a", d.AuthPaused ? 1 : 0),
        ("$l", d.LastSeen is { } l ? Database.Ts(l) : null));

    public DeviceRecord? Get(int id) => _db.QueryOne($"SELECT {Cols} FROM devices WHERE id=$i", Map, ("$i", id));

    public IReadOnlyList<DeviceRecord> List() => _db.Query($"SELECT {Cols} FROM devices ORDER BY id", Map);

    public void Delete(int id) => _db.Exec("DELETE FROM devices WHERE id=$i", ("$i", id));

    public void SetAuthPaused(int id, bool paused) =>
        _db.Exec("UPDATE devices SET auth_paused=$a WHERE id=$i", ("$a", paused ? 1 : 0), ("$i", id));

    public void SetSeen(int id, DeviceInfo? info, DateTime at)
    {
        if (info is null)
            _db.Exec("UPDATE devices SET last_seen=$l WHERE id=$i", ("$l", Database.Ts(at)), ("$i", id));
        else
            _db.Exec("UPDATE devices SET last_seen=$l, model=$m, serial=$s, firmware=$f WHERE id=$i",
                ("$l", Database.Ts(at)), ("$m", info.Model), ("$s", info.Serial), ("$f", info.Firmware), ("$i", id));
    }
}
