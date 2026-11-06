namespace Acs.Core.Storage;

public sealed record LogEntry(long Id, DateTime At, string? EmployeeNo, int? DeviceId, string Action, bool Ok, string? Message);

public sealed class LogStore
{
    private readonly Database _db;
    internal LogStore(Database db) => _db = db;

    public void Add(string? employeeNo, int? deviceId, string action, bool ok, string? message = null) => _db.Exec(
        "INSERT INTO log(at, employee_no, device_id, action, ok, message) VALUES($t,$e,$d,$a,$o,$m)",
        ("$t", Database.Ts(DateTime.UtcNow)), ("$e", employeeNo), ("$d", deviceId), ("$a", action), ("$o", ok ? 1 : 0), ("$m", message));

    public IReadOnlyList<LogEntry> List(string? employeeNo, int? deviceId, int limit) => _db.Query(
        "SELECT id, at, employee_no, device_id, action, ok, message FROM log WHERE ($e IS NULL OR employee_no=$e) AND ($d IS NULL OR device_id=$d) ORDER BY id DESC LIMIT $l",
        r => new LogEntry(r.GetInt64(0), Database.ParseTs(r.GetString(1)), Database.NStr(r, "employee_no"), Database.NInt(r, "device_id"),
            r.GetString(4), r.GetInt32(5) != 0, Database.NStr(r, "message")),
        ("$e", employeeNo), ("$d", deviceId), ("$l", limit));
}
