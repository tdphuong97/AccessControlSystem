using Acs.Core.Model;

namespace Acs.Core.Storage;

public sealed class BiometricStore
{
    private readonly Database _db;
    internal BiometricStore(Database db) => _db = db;

    public void SetFace(FaceRecord f) => _db.Exec(
        "INSERT INTO faces(employee_no, jpeg, source, captured_at) VALUES($e,$j,$s,$c) ON CONFLICT(employee_no) DO UPDATE SET jpeg=$j, source=$s, captured_at=$c",
        ("$e", f.EmployeeNo), ("$j", _db.Protector.Seal(f.Jpeg)), ("$s", f.Source.ToString()), ("$c", Database.Ts(f.CapturedAt)));

    public FaceRecord? GetFace(string employeeNo) => _db.QueryOne(
        "SELECT employee_no, jpeg, source, captured_at FROM faces WHERE employee_no=$e",
        r => new FaceRecord(r.GetString(0), _db.Protector.Open((byte[])r["jpeg"]), Enum.Parse<BiometricSource>(r.GetString(2)), Database.ParseTs(r.GetString(3))),
        ("$e", employeeNo));

    public void DeleteFace(string employeeNo) => _db.Exec("DELETE FROM faces WHERE employee_no=$e", ("$e", employeeNo));

    public void SetFinger(FingerRecord f) => _db.Exec(
        "INSERT INTO fingerprints(employee_no, finger_id, fp_group, template, quality, source, captured_at) VALUES($e,$i,$g,$t,$q,$s,$c) " +
        "ON CONFLICT(employee_no, finger_id, fp_group) DO UPDATE SET template=$t, quality=$q, source=$s, captured_at=$c",
        ("$e", f.EmployeeNo), ("$i", f.FingerId), ("$g", f.FpGroup), ("$t", _db.Protector.Seal(Convert.FromBase64String(f.TemplateBase64))),
        ("$q", f.Quality), ("$s", f.Source.ToString()), ("$c", Database.Ts(f.CapturedAt)));

    public IReadOnlyList<FingerRecord> GetFingers(string employeeNo) => _db.Query(
        "SELECT employee_no, finger_id, fp_group, template, quality, source, captured_at FROM fingerprints WHERE employee_no=$e ORDER BY finger_id, fp_group",
        r => new FingerRecord(r.GetString(0), r.GetInt32(1), r.GetString(2), Convert.ToBase64String(_db.Protector.Open((byte[])r["template"])),
            Database.NInt(r, "quality"), Enum.Parse<BiometricSource>(r.GetString(5)), Database.ParseTs(r.GetString(6))),
        ("$e", employeeNo));

    /// <summary>Removes the finger for every fp_group.</summary>
    public void DeleteFinger(string employeeNo, int fingerId) =>
        _db.Exec("DELETE FROM fingerprints WHERE employee_no=$e AND finger_id=$i", ("$e", employeeNo), ("$i", fingerId));

    /// <summary>Employee numbers that have a face, without reading or decrypting the photos.</summary>
    public HashSet<string> FaceOwners() => _db.Query("SELECT employee_no FROM faces", r => r.GetString(0)).ToHashSet();

    /// <summary>Distinct finger ids per employee, without reading or decrypting the templates.</summary>
    public Dictionary<string, int> FingerCounts() => _db.Query(
        "SELECT employee_no, COUNT(DISTINCT finger_id) FROM fingerprints GROUP BY employee_no",
        r => (r.GetString(0), r.GetInt32(1))).ToDictionary(x => x.Item1, x => x.Item2);
}
