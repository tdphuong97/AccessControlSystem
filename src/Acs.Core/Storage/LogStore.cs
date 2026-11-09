namespace Acs.Core.Storage;

public sealed record LogEntry(long Id, DateTime At, string? EmployeeNo, int? DeviceId, string Action, bool Ok, string? Message, string? Pc = null);

public sealed class LogStore
{
    private readonly Database _db;
    internal LogStore(Database db) => _db = db;

    public void Add(string? employeeNo, int? deviceId, string action, bool ok, string? message = null) => _db.Exec(
        "INSERT INTO log(at, employee_no, device_id, action, ok, message, pc, origin) VALUES($t,$e,$d,$a,$o,$m,$p,$g)",
        ("$t", Database.Ts(DateTime.UtcNow)), ("$e", employeeNo), ("$d", deviceId), ("$a", action), ("$o", ok ? 1 : 0), ("$m", message),
        ("$p", Environment.MachineName), ("$g", Guid.NewGuid().ToString("N")));

    public IReadOnlyList<LogEntry> List(string? employeeNo, int? deviceId, int limit) => _db.Query(
        "SELECT id, at, employee_no, device_id, action, ok, message, pc FROM log WHERE ($e IS NULL OR employee_no=$e) AND ($d IS NULL OR device_id=$d) ORDER BY id DESC LIMIT $l",
        r => new LogEntry(r.GetInt64(0), Database.ParseTs(r.GetString(1)), Database.NStr(r, "employee_no"), Database.NInt(r, "device_id"),
            r.GetString(4), r.GetInt32(5) != 0, Database.NStr(r, "message"), Database.NStr(r, "pc")),
        ("$e", employeeNo), ("$d", deviceId), ("$l", limit));
}
