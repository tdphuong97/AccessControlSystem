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

    // ---- queue ----
    private const string Open = "status IN ('pending','running','failed')";

    // Pending jobs a worker may run: a person's other jobs wait while their UpsertUser is still open.
    private const string Eligible =
        "status='pending' AND device_id=$d AND NOT (kind<>'UpsertUser' AND EXISTS (SELECT 1 FROM jobs u WHERE u.device_id=jobs.device_id " +
        "AND u.employee_no=jobs.employee_no AND u.kind='UpsertUser' AND u.status IN ('pending','running','failed')))";

    public void Enqueue(int deviceId, string employeeNo, JobKind kind, int fingerId, DateTime now) => _db.Tx(() =>
    {
        if (kind == JobKind.DeleteUser)
            _db.Exec($"DELETE FROM jobs WHERE device_id=$d AND employee_no=$e AND kind<>'DeleteUser' AND {Open}", ("$d", deviceId), ("$e", employeeNo));
        else if (kind == JobKind.UpsertUser)
            _db.Exec($"DELETE FROM jobs WHERE device_id=$d AND employee_no=$e AND kind IN ('DeleteUser','DisableUser') AND {Open}", ("$d", deviceId), ("$e", employeeNo));
        var t = Database.Ts(now);
        _db.Exec(
            "INSERT INTO jobs(device_id, employee_no, kind, finger_id, status, attempts, next_at, error_kind, error_message, error_detail, created_at, updated_at) " +
            "VALUES($d,$e,$k,$f,'pending',0,$t,NULL,NULL,NULL,$t,$t) " +
            "ON CONFLICT(device_id, employee_no, kind, finger_id) WHERE status IN ('pending','running','failed') DO UPDATE SET " +
            "status='pending', attempts=0, next_at=$t, error_kind=NULL, error_message=NULL, error_detail=NULL, updated_at=$t",
            ("$d", deviceId), ("$e", employeeNo), ("$k", kind.ToString()), ("$f", fingerId), ("$t", t));
        return 0;
    });

    /// <summary>Next runnable job: rank (delete user, user, face, fingers) then created_at.</summary>
    public JobRecord? NextDue(int deviceId, DateTime now) => _db.QueryOne(
        $"SELECT {Cols} FROM jobs WHERE {Eligible} AND next_at<=$n ORDER BY CASE kind WHEN 'DeleteUser' THEN 0 WHEN 'UpsertUser' THEN 1 " +
        "WHEN 'DisableUser' THEN 1 WHEN 'SetFace' THEN 2 WHEN 'DeleteFace' THEN 2 ELSE 3 END, created_at, id LIMIT 1",
        Map, ("$d", deviceId), ("$n", Database.Ts(now)));

    /// <summary>Earliest time a currently-waiting runnable job becomes due, or null.</summary>
    public DateTime? NextFuture(int deviceId, DateTime now)
    {
        var s = _db.Scalar($"SELECT MIN(next_at) FROM jobs WHERE {Eligible} AND next_at>$n", ("$d", deviceId), ("$n", Database.Ts(now))) as string;
        return s is null ? null : Database.ParseTs(s);
    }

    /// <summary>False when the job is no longer pending (superseded or removed since NextDue): it must not be sent.</summary>
    public bool MarkRunning(JobRecord j, DateTime now) =>
        // kind/person/finger are checked too: SQLite reuses the rowid of a deleted job for the next insert.
        _db.Exec("UPDATE jobs SET status='running', updated_at=$u WHERE id=$i AND status='pending' AND employee_no=$e AND kind=$k AND finger_id=$f",
            ("$u", Database.Ts(now)), ("$i", j.Id), ("$e", j.EmployeeNo), ("$k", j.Kind.ToString()), ("$f", j.FingerId)) > 0;

    /// <summary>False when the job was re-enqueued or removed while running (it then stays pending).</summary>
    public bool MarkDone(long id, DateTime now) =>
        _db.Exec("UPDATE jobs SET status='done', error_kind=NULL, error_message=NULL, error_detail=NULL, updated_at=$u WHERE id=$i AND status='running'",
            ("$u", Database.Ts(now)), ("$i", id)) > 0;

    /// <summary>Back to pending with one more attempt, due at <paramref name="nextAt"/>.</summary>
    public void Retry(long id, DateTime nextAt, string errorKind, string message, string? detail, DateTime now) => _db.Exec(
        "UPDATE jobs SET status='pending', attempts=attempts+1, next_at=$n, error_kind=$ek, error_message=$em, error_detail=$ed, updated_at=$u WHERE id=$i AND status='running'",
        ("$n", Database.Ts(nextAt)), ("$ek", errorKind), ("$em", message), ("$ed", detail), ("$u", Database.Ts(now)), ("$i", id));

    /// <summary>Back to pending without counting an attempt (auth pause, shutdown).</summary>
    public void Requeue(long id, DateTime now) => _db.Exec(
        "UPDATE jobs SET status='pending', updated_at=$u WHERE id=$i AND status='running'", ("$u", Database.Ts(now)), ("$i", id));

    public void Fail(long id, string errorKind, string message, string? detail, DateTime now) => _db.Exec(
        "UPDATE jobs SET status='failed', error_kind=$ek, error_message=$em, error_detail=$ed, updated_at=$u WHERE id=$i AND status='running'",
        ("$ek", errorKind), ("$em", message), ("$ed", detail), ("$u", Database.Ts(now)), ("$i", id));

    /// <summary>Failed jobs (one device, or all when null) go back to pending, due now, with the attempt count and error cleared.</summary>
    public int ResetFailed(int? deviceId, DateTime now) => _db.Exec(
        "UPDATE jobs SET status='pending', attempts=0, next_at=$n, error_kind=NULL, error_message=NULL, error_detail=NULL, updated_at=$n WHERE status='failed' AND ($d IS NULL OR device_id=$d)",
        ("$n", Database.Ts(now)), ("$d", deviceId));

    /// <summary>Jobs left 'running' by a crash go back to pending.</summary>
    public int ResetRunning() => _db.Exec("UPDATE jobs SET status='pending' WHERE status='running'");

    /// <summary>Device came back online: waiting retries run now instead of after their backoff.</summary>
    public void ExpediteRetries(int deviceId, DateTime now) => _db.Exec(
        "UPDATE jobs SET next_at=$n WHERE device_id=$d AND status='pending' AND next_at>$n", ("$n", Database.Ts(now)), ("$d", deviceId));

    public (int Pending, int Failed) Counts(int deviceId)
    {
        var p = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM jobs WHERE device_id=$d AND status IN ('pending','running')", ("$d", deviceId)));
        var f = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM jobs WHERE device_id=$d AND status='failed'", ("$d", deviceId)));
        return (p, f);
    }

    private const string StateCols = "employee_no, device_id, user_present, user_enabled, face_present, fingers, needs_scan, verified_at";

    private static DeviceStateRecord MapState(SqliteDataReader r) => new(r.GetString(0), r.GetInt32(1), r.GetInt32(2) != 0,
        r.GetInt32(3) != 0, r.GetInt32(4) != 0, ParseSet(r.GetString(5)), ParseSet(r.GetString(6)), Database.NTs(r, "verified_at"));

    public DeviceStateRecord? GetState(string employeeNo, int deviceId) => _db.QueryOne(
        $"SELECT {StateCols} FROM device_state WHERE employee_no=$e AND device_id=$d", MapState,
        ("$e", employeeNo), ("$d", deviceId));

    /// <summary>Every device_state row in one query (people.list must not ask per person).</summary>
    public IReadOnlyList<DeviceStateRecord> ListStates() => _db.Query($"SELECT {StateCols} FROM device_state", MapState);

    /// <summary>True while any device still has a pending, running or failed job for this employee number.</summary>
    public bool HasOpen(string employeeNo) =>
        Convert.ToInt32(_db.Scalar($"SELECT COUNT(*) FROM jobs WHERE employee_no=$e AND {Open}", ("$e", employeeNo))) > 0;

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
