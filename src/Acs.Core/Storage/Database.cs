using System.Globalization;
using Acs.Core.Security;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Storage;

/// <summary>One open connection, every call serialized by a re-entrant lock.</summary>
public sealed class Database : IDisposable
{
    private readonly object _gate = new();
    private readonly SqliteConnection _conn;
    private int _txDepth;

    internal DataProtector Protector { get; }
    public PeopleStore People { get; }
    public DeviceStore Devices { get; }
    public BiometricStore Biometrics { get; }
    public JobStore Jobs { get; }
    public LogStore Log { get; }
    public SettingsStore Settings { get; }

    public Database(string file, DataProtector protector)
    {
        Protector = protector;
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL");
        Exec("PRAGMA foreign_keys=ON");
        Migrate();
        People = new PeopleStore(this);
        Devices = new DeviceStore(this);
        Biometrics = new BiometricStore(this);
        Jobs = new JobStore(this);
        Log = new LogStore(this);
        Settings = new SettingsStore(this);
    }

    private void Migrate()
    {
        Tx(() =>
        {
            var v = Convert.ToInt32(Scalar("PRAGMA user_version") ?? 0, CultureInfo.InvariantCulture);
            if (v < 1) Exec(Schema.V1);
            if (v < Schema.Version) Exec($"PRAGMA user_version={Schema.Version}");
            return 0;
        });
    }

    public T Tx<T>(Func<T> body)
    {
        lock (_gate)
        {
            if (_txDepth > 0) return body();
            Exec("BEGIN IMMEDIATE");
            _txDepth++;
            try
            {
                var r = body();
                Exec("COMMIT");
                return r;
            }
            catch
            {
                try { Exec("ROLLBACK"); } catch { /* already rolled back */ }
                throw;
            }
            finally { _txDepth--; }
        }
    }

    internal int Exec(string sql, params (string, object?)[] p)
    {
        lock (_gate) { using var c = Cmd(sql, p); return c.ExecuteNonQuery(); }
    }

    internal object? Scalar(string sql, params (string, object?)[] p)
    {
        lock (_gate)
        {
            using var c = Cmd(sql, p);
            var r = c.ExecuteScalar();
            return r is DBNull ? null : r;
        }
    }

    internal List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] p)
    {
        lock (_gate)
        {
            using var c = Cmd(sql, p);
            using var r = c.ExecuteReader();
            var list = new List<T>();
            while (r.Read()) list.Add(map(r));
            return list;
        }
    }

    internal T? QueryOne<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] p) where T : class
        => Query(sql, map, p).FirstOrDefault();

    internal long InsertId(string sql, params (string, object?)[] p)
    {
        lock (_gate)
        {
            Exec(sql, p);
            return Convert.ToInt64(Scalar("SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
        }
    }

    private SqliteCommand Cmd(string sql, (string, object?)[] p)
    {
        var c = _conn.CreateCommand();
        c.CommandText = sql;
        foreach (var (n, v) in p) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return c;
    }

    // ---- shared value conversions ----
    internal static string Ts(DateTime t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    internal static DateTime ParseTs(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    internal static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static DateOnly ParseD(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static string? NStr(SqliteDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    internal static int? NInt(SqliteDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt32(i); }
    internal static DateTime? NTs(SqliteDataReader r, string col) { var s = NStr(r, col); return s is null ? null : ParseTs(s); }

    public void Dispose()
    {
        lock (_gate) { _conn.Dispose(); }
    }
}
