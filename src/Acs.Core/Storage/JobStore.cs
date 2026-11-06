using Acs.Core.Model;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Storage;

public sealed class JobStore
{
    private readonly Database _db;
    internal JobStore(Database db) => _db = db;

    private const string Cols = "id, device_id, employee_no, kind, finger_id, status, attempts, next_at, error_kind, error_message, error_detail";

    private static JobRecord Map(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt32(1), r.GetString(2), Enum.Parse<JobKind>(r.GetString(3)), r.GetInt32(4),
        Enum.Parse<JobStatus>(r.GetString(5), true), r.GetInt32(6), Database.ParseTs(r.GetString(7)),
        Database.NStr(r, "error_kind"), Database.NStr(r, "error_message"), Database.NStr(r, "error_detail"));

    /// <summary>Inserts a job (the Id in the record is ignored) and returns the new id.</summary>
    public long Insert(JobRecord j)
    {
        var now = Database.Ts(DateTime.UtcNow);
        return _db.InsertId(
            "INSERT INTO jobs(device_id, employee_no, kind, finger_id, status, attempts, next_at, error_kind, error_message, error_detail, created_at, updated_at) VALUES($d,$e,$k,$f,$s,$a,$n,$ek,$em,$ed,$c,$c)",
            ("$d", j.DeviceId), ("$e", j.EmployeeNo), ("$k", j.Kind.ToString()), ("$f", j.FingerId),
            ("$s", j.Status.ToString().ToLowerInvariant()), ("$a", j.Attempts), ("$n", Database.Ts(j.NextAt)),
            ("$ek", j.ErrorKind), ("$em", j.ErrorMessage), ("$ed", j.ErrorDetail), ("$c", now));
    }

    public JobRecord? Get(long id) => _db.QueryOne($"SELECT {Cols} FROM jobs WHERE id=$i", Map, ("$i", id));

    /// <summary>Jobs that are pending, running or failed.</summary>
    public IReadOnlyList<JobRecord> ListOpen(int? deviceId = null) => _db.Query(
        $"SELECT {Cols} FROM jobs WHERE status IN ('pending','running','failed') AND ($d IS NULL OR device_id=$d) ORDER BY id",
        Map, ("$d", deviceId));

    public DeviceStateRecord? GetState(string employeeNo, int deviceId) => _db.QueryOne(
        "SELECT employee_no, device_id, user_present, user_enabled, face_present, fingers, needs_scan, verified_at FROM device_state WHERE employee_no=$e AND device_id=$d",
        r => new DeviceStateRecord(r.GetString(0), r.GetInt32(1), r.GetInt32(2) != 0, r.GetInt32(3) != 0, r.GetInt32(4) != 0,
            ParseSet(r.GetString(5)), ParseSet(r.GetString(6)), Database.NTs(r, "verified_at")),
        ("$e", employeeNo), ("$d", deviceId));

    public void SetState(DeviceStateRecord s) => _db.Exec(
        "INSERT INTO device_state(employee_no, device_id, user_present, user_enabled, face_present, fingers, needs_scan, verified_at) VALUES($e,$d,$up,$ue,$fp,$f,$n,$v) " +
        "ON CONFLICT(employee_no, device_id) DO UPDATE SET user_present=$up, user_enabled=$ue, face_present=$fp, fingers=$f, needs_scan=$n, verified_at=$v",
        ("$e", s.EmployeeNo), ("$d", s.DeviceId), ("$up", s.UserPresent ? 1 : 0), ("$ue", s.UserEnabled ? 1 : 0),
        ("$fp", s.FacePresent ? 1 : 0), ("$f", FormatSet(s.Fingers)), ("$n", FormatSet(s.NeedsScan)),
        ("$v", s.VerifiedAt is { } v ? Database.Ts(v) : null));

    public void DeleteState(string employeeNo, int deviceId) =>
        _db.Exec("DELETE FROM device_state WHERE employee_no=$e AND device_id=$d", ("$e", employeeNo), ("$d", deviceId));

    private static string FormatSet(IReadOnlySet<int> s) => string.Join(",", s.OrderBy(x => x));
    private static IReadOnlySet<int> ParseSet(string s) =>
        s.Length == 0 ? new HashSet<int>() : s.Split(',').Select(int.Parse).ToHashSet();
}
