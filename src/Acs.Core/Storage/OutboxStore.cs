namespace Acs.Core.Storage;

/// <summary>
/// Dữ liệu chung bookkeeping: the outbox of natural keys changed on this PC (filled by the v3 triggers while capture is
/// on), the cloud version last seen for each key, and rows waiting for a device they reference.
/// Natural key parts are joined with U+001F, exactly as the triggers build them.
/// </summary>
public sealed class OutboxStore
{
    public const char Sep = '\u001f';
    private readonly Database _db;
    internal OutboxStore(Database db) => _db = db;

    /// <summary>True while changes are recorded for the cloud. Off on PCs that do not use shared data.</summary>
    public bool Capture
    {
        get => Convert.ToInt32(_db.Scalar("SELECT on_ FROM sync_flags WHERE name='capture'") ?? 0) == 1;
        set => _db.Exec("UPDATE sync_flags SET on_=$v WHERE name='capture'", ("$v", value ? 1 : 0));
    }

    public IReadOnlyList<(string Tbl, string Key)> Take(int max) =>
        _db.Query("SELECT tbl, k FROM outbox ORDER BY rowid LIMIT $m", r => (r.GetString(0), r.GetString(1)), ("$m", max));

    public bool IsPending(string tbl, string key) =>
        _db.Scalar("SELECT 1 FROM outbox WHERE tbl=$t AND k=$k", ("$t", tbl), ("$k", key)) is not null;

    public void Remove(string tbl, string key) => _db.Exec("DELETE FROM outbox WHERE tbl=$t AND k=$k", ("$t", tbl), ("$k", key));

    public int Count() => Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM outbox"));

    public void Clear() => _db.Exec("DELETE FROM outbox");

    /// <summary>Records every current shared row (drafts excluded), for the first publish and a republish.</summary>
    public void MarkAll() => _db.Exec("""
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'settings', key FROM settings WHERE key='employeeNo.deviceHighWater';
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'groups', name FROM groups;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'devices', fp_group FROM devices;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'people', employee_no FROM people WHERE draft=0;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'faces', employee_no FROM faces;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'fingerprints', employee_no || char(31) || finger_id || char(31) || fp_group FROM fingerprints;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'device_state', s.employee_no || char(31) || d.fp_group FROM device_state s JOIN devices d ON d.id=s.device_id;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'jobs', d.fp_group || char(31) || j.employee_no || char(31) || j.kind || char(31) || j.finger_id || char(31) || j.created_at FROM jobs j JOIN devices d ON d.id=j.device_id;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'trash', employee_no || char(31) || deleted_at FROM trash;
        INSERT OR IGNORE INTO outbox(tbl, k) SELECT 'log', origin FROM log WHERE origin IS NOT NULL;
        """);

    public long? KnownVer(string tbl, string key) =>
        _db.Scalar("SELECT ver FROM sync_rows WHERE tbl=$t AND k=$k", ("$t", tbl), ("$k", key)) is { } v ? Convert.ToInt64(v) : null;

    public void SetKnown(string tbl, string key, string rid, long ver) => _db.Exec(
        "INSERT INTO sync_rows(tbl, k, rid, ver) VALUES($t,$k,$r,$v) ON CONFLICT(tbl, k) DO UPDATE SET rid=excluded.rid, ver=excluded.ver",
        ("$t", tbl), ("$k", key), ("$r", rid), ("$v", ver));

    public void ClearKnown() => _db.Exec("DELETE FROM sync_rows");

    // ---- rows that wait for a device they reference ----
    public void Defer(string tbl, string rid, long ver, bool deleted, string? data) => _db.Exec(
        "INSERT INTO sync_deferred(tbl, rid, ver, deleted, data) VALUES($t,$r,$v,$d,$x) ON CONFLICT(tbl, rid) DO UPDATE SET ver=excluded.ver, deleted=excluded.deleted, data=excluded.data",
        ("$t", tbl), ("$r", rid), ("$v", ver), ("$d", deleted ? 1 : 0), ("$x", data));

    public IReadOnlyList<(string Tbl, string Rid, long Ver, bool Deleted, string? Data)> Deferred() => _db.Query(
        "SELECT tbl, rid, ver, deleted, data FROM sync_deferred ORDER BY ver",
        r => (r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt32(3) != 0, r.IsDBNull(4) ? null : r.GetString(4)));

    public void Undefer(string tbl, string rid) => _db.Exec("DELETE FROM sync_deferred WHERE tbl=$t AND rid=$r", ("$t", tbl), ("$r", rid));

    public void ClearDeferred() => _db.Exec("DELETE FROM sync_deferred");

    /// <summary>Runs <paramref name="body"/> in one transaction with capture off (rows pulled from the cloud must not be pushed back).</summary>
    public T Applying<T>(Func<T> body) => _db.Tx(() =>
    {
        var was = Capture;
        Capture = false;
        try { return body(); }
        finally { Capture = was; }
    });
}
