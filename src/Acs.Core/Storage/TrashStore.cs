using System.Text.Json;
using Acs.Core.Model;

namespace Acs.Core.Storage;

/// <summary>One row of Thùng rác, without its sealed contents.</summary>
public sealed record TrashEntry(long Id, string EmployeeNo, string FullName, string? GroupName, bool HasFace, int FingerCount, DateTime DeletedAt);

/// <summary>Everything needed to bring a deleted person back: the record, the group by name, the face and every finger copy.</summary>
public sealed record TrashedPerson(PersonRecord Person, string? GroupName, FaceRecord? Face, IReadOnlyList<FingerRecord> Fingers);

/// <summary>
/// Thùng rác: people deleted with "Xoá hẳn" are kept here, face and fingerprints sealed like the live tables, until the
/// owner restores them or removes them for good (no automatic expiry, by the owner's choice).
/// </summary>
public sealed class TrashStore
{
    private readonly Database _db;
    internal TrashStore(Database db) => _db = db;

    public long Add(TrashedPerson t, DateTime at)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(t);
        return _db.InsertId(
            "INSERT INTO trash(employee_no, full_name, group_name, has_face, finger_count, deleted_at, data) VALUES($e,$n,$g,$f,$c,$t,$d)",
            ("$e", t.Person.EmployeeNo), ("$n", t.Person.FullName), ("$g", t.GroupName), ("$f", t.Face is null ? 0 : 1),
            ("$c", t.Fingers.Select(f => f.FingerId).Distinct().Count()), ("$t", Database.Ts(at)), ("$d", _db.Protector.Seal(json)));
    }

    public IReadOnlyList<TrashEntry> List() => _db.Query(
        "SELECT id, employee_no, full_name, group_name, has_face, finger_count, deleted_at FROM trash ORDER BY id DESC",
        r => new TrashEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), Database.NStr(r, "group_name"), r.GetInt32(4) != 0, r.GetInt32(5),
            Database.ParseTs(r.GetString(6))));

    public TrashedPerson? Get(long id)
    {
        var data = _db.Scalar("SELECT data FROM trash WHERE id=$i", ("$i", id)) as byte[];
        return data is null ? null : JsonSerializer.Deserialize<TrashedPerson>(_db.Protector.Open(data));
    }

    public void Remove(long id) => _db.Exec("DELETE FROM trash WHERE id=$i", ("$i", id));

    /// <summary>Dọn sạch thùng rác; returns how many people were removed for good.</summary>
    public int Clear()
    {
        var n = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM trash"));
        _db.Exec("DELETE FROM trash");
        return n;
    }
}
