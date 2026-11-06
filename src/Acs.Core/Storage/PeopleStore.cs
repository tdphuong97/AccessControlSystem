using System.Numerics;
using Acs.Core.Model;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Storage;

public sealed class PeopleStore
{
    private readonly Database _db;
    internal PeopleStore(Database db) => _db = db;

    private const string Cols = "employee_no, full_name, group_id, valid_from, valid_to, status, created_at, updated_at, locked_at";

    private static PersonRecord Map(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), Database.NInt(r, "group_id"), Database.ParseD(r.GetString(3)),
        Database.NStr(r, "valid_to") is { } vt ? Database.ParseD(vt) : null,
        r.GetString(5) == "locked" ? PersonStatus.Locked : PersonStatus.Active,
        Database.ParseTs(r.GetString(6)), Database.ParseTs(r.GetString(7)), Database.NTs(r, "locked_at"));

    private static string St(PersonStatus s) => s == PersonStatus.Locked ? "locked" : "active";

    public void Insert(PersonRecord p) => _db.Exec(
        "INSERT INTO people(employee_no, full_name, group_id, valid_from, valid_to, status, created_at, updated_at, locked_at) VALUES($e,$n,$g,$vf,$vt,$s,$c,$u,$l)",
        ("$e", p.EmployeeNo), ("$n", p.FullName), ("$g", p.GroupId), ("$vf", Database.D(p.ValidFrom)),
        ("$vt", p.ValidTo is { } v ? Database.D(v) : null), ("$s", St(p.Status)), ("$c", Database.Ts(p.CreatedAt)),
        ("$u", Database.Ts(p.UpdatedAt)), ("$l", p.LockedAt is { } l ? Database.Ts(l) : null));

    public void Update(PersonRecord p) => _db.Exec(
        "UPDATE people SET full_name=$n, group_id=$g, valid_from=$vf, valid_to=$vt, status=$s, updated_at=$u, locked_at=$l WHERE employee_no=$e",
        ("$e", p.EmployeeNo), ("$n", p.FullName), ("$g", p.GroupId), ("$vf", Database.D(p.ValidFrom)),
        ("$vt", p.ValidTo is { } v ? Database.D(v) : null), ("$s", St(p.Status)),
        ("$u", Database.Ts(p.UpdatedAt)), ("$l", p.LockedAt is { } l ? Database.Ts(l) : null));

    public PersonRecord? Get(string employeeNo) =>
        _db.QueryOne($"SELECT {Cols} FROM people WHERE employee_no=$e", Map, ("$e", employeeNo));

    public IReadOnlyList<PersonRecord> List(string? query = null, PersonStatus? status = null)
    {
        var where = new List<string>();
        var ps = new List<(string, object?)>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add("(employee_no LIKE $q ESCAPE '\\' OR full_name LIKE $q ESCAPE '\\')");
            ps.Add(("$q", "%" + query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
        }
        if (status is { } s) { where.Add("status=$s"); ps.Add(("$s", St(s))); }
        var sql = $"SELECT {Cols} FROM people" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY full_name, employee_no";
        return _db.Query(sql, Map, ps.ToArray());
    }

    public int Count() => Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM people"));

    public void Delete(string employeeNo) => _db.Exec("DELETE FROM people WHERE employee_no=$e", ("$e", employeeNo));

    /// <summary>Settings key: the highest all-digit employee number ever used here (created, imported or seen on a device).</summary>
    public const string HighWaterKey = "employeeNo.highWater";

    private static bool IsNumber(string? s) => !string.IsNullOrEmpty(s) && s.All(char.IsAsciiDigit);

    /// <summary>The larger number wins; on a tie the wider one (more leading zeros) is kept.</summary>
    private static bool Above(string a, string b)
    {
        var (x, y) = (BigInteger.Parse(a), BigInteger.Parse(b));
        return x > y || (x == y && a.Length > b.Length);
    }

    /// <summary>
    /// Remembers <paramref name="employeeNo"/> as used, so a deleted or device-only number is never suggested again
    /// (a reused number would merge a new person into an old device user). Call inside the caller's transaction.
    /// </summary>
    public void BumpHighWater(string employeeNo)
    {
        if (!IsNumber(employeeNo)) return;
        var cur = _db.Settings.Get(HighWaterKey);
        if (!IsNumber(cur) || Above(employeeNo, cur!)) _db.Settings.Set(HighWaterKey, employeeNo);
    }

    /// <summary>max(highest all-digit number in the app, high-water mark) + 1, keeping the width of leading zeros.
    /// No numeric id gives "1001".</summary>
    public string NextEmployeeNo()
    {
        var ids = _db.Query("SELECT employee_no FROM people", r => r.GetString(0));
        if (_db.Settings.Get(HighWaterKey) is { } hw) ids.Add(hw);
        string? best = null;
        foreach (var id in ids)
            if (IsNumber(id) && (best is null || Above(id, best))) best = id;
        if (best is null) return "1001";
        return (BigInteger.Parse(best) + 1).ToString().PadLeft(best.Length, '0');
    }

    public int GetOrCreateGroup(string name) => _db.Tx(() =>
    {
        name = name.Trim();
        var id = _db.Scalar("SELECT id FROM groups WHERE name=$n", ("$n", name));
        if (id is not null) return Convert.ToInt32(id);
        return (int)_db.InsertId("INSERT INTO groups(name) VALUES($n)", ("$n", name));
    });

    public Dictionary<int, string> GroupNames() =>
        _db.Query("SELECT id, name FROM groups", r => (r.GetInt32(0), r.GetString(1))).ToDictionary(x => x.Item1, x => x.Item2);

    public string? GroupName(int? id) =>
        id is null ? null : _db.Scalar("SELECT name FROM groups WHERE id=$i", ("$i", id)) as string;
}
