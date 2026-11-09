using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Acs.Core.Storage;

namespace Acs.Core.Cloud;

public sealed record CloudSyncStatus(DateTime? LastSyncAt, int Pending, bool Online, string? Error);

/// <summary>
/// Two-way sync between this PC's acs.db and the acs-sync Worker. Push: every key the v3 triggers queued is read,
/// sealed with K and sent with the cloud version this PC last saw; a refusal means another PC wrote in between, and
/// the later write (this one) wins with a log line. Pull: rows newer than <c>cloud.since</c> are opened and applied
/// with capture off; rows this PC wrote itself are skipped; a row whose device or person is not here yet is parked
/// and retried. Each sealed payload carries its natural key (<c>_k</c>), the PC name (<c>_pc</c>) and time (<c>_at</c>).
/// </summary>
public sealed class CloudSync(Database db, CloudClient client, byte[] key, string pcId, string pcName, TimeProvider time)
{
    public const string SinceKey = "cloud.since";
    public const string LogAction = "Dữ liệu chung";
    // A face row is ~50 KB once sealed: keep each push small enough for the Worker to answer well within the timeout.
    private const int PushBatch = 20, PullLimit = 200, MaxBatchChars = 1_000_000, PublishRetries = 8;

    private readonly SemaphoreSlim _gate = new(1, 1);   // one sync operation at a time on this PC
    private DateTime? _lastSync;
    private bool _online = true;
    private string? _error;

    /// <summary>A pulled row was applied (table, natural key): the host turns this into UI events.</summary>
    public event Action<string, string>? Applied;

    public CloudSyncStatus Status => new(_lastSync, db.Outbox.Count(), _online, _error);

    private string NowIso => time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    // ---------------- push ----------------

    public Task<int> PushAsync(CancellationToken ct) => Locked(() => Guard(() => PushCore(ct)), ct);

    private async Task<int> PushCore(CancellationToken ct)
    {
        var pushed = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = new List<(string Tbl, string Key, long Seq)>();
            var changes = new List<RowChange>();
            var size = 0;
            foreach (var e in db.Outbox.Take(PushBatch))
            {
                var c = Change(e.Tbl, e.Key, db.Outbox.KnownVer(e.Tbl, e.Key) ?? 0);
                if (changes.Count > 0 && size + (c.Data?.Length ?? 0) > MaxBatchChars) break;
                batch.Add(e); changes.Add(c); size += c.Data?.Length ?? 0;
            }
            if (batch.Count == 0) break;
            var results = await client.PushAsync(changes, ct);
            var retry = new List<(int Index, RowChange Change)>();
            for (var i = 0; i < results.Count; i++)
            {
                var (tbl, k, seq) = batch[i];
                var r = results[i];
                if (r.Ok) { db.Outbox.SetKnown(tbl, k, r.Rid, r.Ver); db.Outbox.RemoveIf(tbl, k, seq); pushed++; continue; }
                if (r.Current is { } cur)
                {
                    if (cur.Pc != pcId) LogOverwrite(cur, tbl, k);   // a row this PC wrote itself is no one else's change
                    retry.Add((i, changes[i] with { Base = cur.Ver }));
                }
                else retry.Add((i, changes[i] with { Base = 0 }));   // a version from another space: the row is new here
            }
            if (retry.Count > 0)
            {
                var again = await client.PushAsync(retry.Select(x => x.Change).ToList(), ct);
                for (var j = 0; j < again.Count; j++)
                {
                    var (tbl, k, seq) = batch[retry[j].Index];
                    if (again[j].Ok) { db.Outbox.SetKnown(tbl, k, again[j].Rid, again[j].Ver); db.Outbox.RemoveIf(tbl, k, seq); pushed++; }
                    else if (again[j].Current is { } c2) db.Outbox.SetKnown(tbl, k, again[j].Rid, c2.Ver);   // next push uses the newest base
                }
                if (again.Any(a => !a.Ok)) break;   // someone keeps writing: leave the rest for the next round
            }
        }
        return pushed;
    }

    private RowChange Change(string tbl, string k, long baseVer)
    {
        var rid = CloudCrypto.RowId(key, tbl, k);
        var row = SyncTables.Get(tbl).Read(db, k);
        var payload = row ?? new JsonObject();
        payload["_k"] = k;
        payload["_pc"] = pcName;
        payload["_at"] = NowIso;
        var data = CloudCrypto.Seal(key, tbl, rid, Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return new RowChange(tbl, rid, baseVer, row is null, data, pcId);
    }

    private void LogOverwrite(CloudRow cur, string tbl, string k)
    {
        var who = Describe(cur, tbl);
        db.Log.Add(EmployeeOf(tbl, k), null, LogAction, true, $"Đã ghi đè thay đổi của {who} ({Label(tbl)} {Show(k)}).");
    }

    // ---------------- pull ----------------

    public Task<int> PullAsync(CancellationToken ct) => Locked(() => Guard(() => PullCore(ct)), ct);

    /// <param name="includeOwn">A full replace needs this PC's own rows too (it just deleted them locally).</param>
    private async Task<int> PullCore(CancellationToken ct, bool includeOwn = false)
    {
        var applied = RetryDeferred();
        var since = long.TryParse(db.Settings.Get(SinceKey), out var s) ? s : 0;
        while (!ct.IsCancellationRequested)
        {
            var page = await client.PullAsync(since, PullLimit, ct);
            foreach (var row in page.Rows)
            {
                if (includeOwn || row.Pc != pcId) applied += ApplyRow(row) ? 1 : 0;
                since = row.Ver;
            }
            db.Settings.Set(SinceKey, since.ToString(CultureInfo.InvariantCulture));
            if (!page.More) break;
        }
        return applied + RetryDeferred();   // a parked row whose device arrived in this pull
    }

    private bool ApplyRow(CloudRow row)
    {
        if (row.Data is null) return false;
        JsonObject payload;
        string k;
        try
        {
            if (!SyncTables.All.Any(t => t.Name == row.Tbl)) throw new CloudException("unknown table " + row.Tbl);
            payload = JsonNode.Parse(CloudCrypto.Open(key, row.Tbl, row.Rid, row.Data))!.AsObject();
            k = payload["_k"]!.GetValue<string>();
        }
        catch (Exception e) when (e is CloudException or System.Text.Json.JsonException or InvalidOperationException or NullReferenceException)
        {
            // Written with another key or by something else: one bad row must not stop every pull after it.
            db.Log.Add(null, null, LogAction, false, $"Bỏ qua một dòng dữ liệu chung không đọc được ({row.Tbl}, phiên bản {row.Ver}).");
            return false;
        }
        if (db.Outbox.IsPending(row.Tbl, k))
        {
            // This PC changed the same row and has not pushed yet: its change is the later one and will win.
            db.Outbox.SetKnown(row.Tbl, k, row.Rid, row.Ver);
            db.Log.Add(EmployeeOf(row.Tbl, k), null, LogAction, true,
                $"Thay đổi của {Describe(payload)} được thay bằng thay đổi trên máy này ({Label(row.Tbl)} {Show(k)}).");
            return false;
        }
        ApplyResult result;
        try { result = db.Outbox.Applying(() => SyncTables.Get(row.Tbl).Apply(db, k, row.Deleted ? null : payload)); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A row this version cannot apply (bad data, a newer PC's format): skip it rather than stop every pull.
            db.Log.Add(EmployeeOf(row.Tbl, k), null, LogAction, false, $"Bỏ qua một dòng dữ liệu chung không áp dụng được ({Label(row.Tbl)} {Show(k)}): {e.Message}");
            return false;
        }
        if (result == ApplyResult.Deferred) { db.Outbox.Defer(row.Tbl, row.Rid, row.Ver, row.Deleted, row.Data); return false; }
        db.Outbox.SetKnown(row.Tbl, k, row.Rid, row.Ver);
        db.Outbox.Undefer(row.Tbl, row.Rid);
        Applied?.Invoke(row.Tbl, k);
        return true;
    }

    private int RetryDeferred()
    {
        var n = 0;
        foreach (var d in db.Outbox.Deferred())
            if (ApplyRow(new CloudRow(d.Tbl, d.Rid, d.Ver, d.Deleted, d.Data, null, ""))) n++;
        return n;
    }

    // ---------------- whole-data operations ----------------

    /// <summary>First PC: queue every shared row and push until the outbox is empty.</summary>
    public Task PublishAllAsync(IProgress<double>? progress, CancellationToken ct) => Locked(() => Guard(async () =>
    {
        db.Outbox.Capture = true;
        db.Outbox.MarkAll();
        var total = Math.Max(1, db.Outbox.Count());
        var failures = 0;
        while (db.Outbox.Count() > 0 && !ct.IsCancellationRequested)
        {
            try { if (await PushCore(ct) == 0) break; failures = 0; }
            catch (CloudOfflineException e) when (++failures < PublishRetries)
            {
                db.Log.Add(null, null, LogAction, false, $"Đưa dữ liệu lên bị gián đoạn, đang thử lại ({failures}): {e.InnerException?.Message ?? e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(20, 1 << failures)), ct);   // 2, 4, 8, 16, 20 … s
                continue;
            }
            progress?.Report(1 - (double)db.Outbox.Count() / total);
        }
        progress?.Report(1);
        return 0;
    }), ct);

    /// <summary>Joining PC (and switching back on): local shared rows are replaced by the cloud's. Returns the people count.</summary>
    public Task<int> ReplaceLocalFromCloudAsync(IProgress<double>? progress, CancellationToken ct) => Locked(() => Guard(async () =>
    {
        db.Outbox.Applying(() =>
        {
            foreach (var t in SyncTables.All.Reverse())
                db.Exec(t.Name == "settings" ? "DELETE FROM settings WHERE key='employeeNo.deviceHighWater'" : $"DELETE FROM {t.Name}");
            db.Outbox.Clear();
            db.Outbox.ClearKnown();
            db.Outbox.ClearDeferred();
            db.Settings.Set(SinceKey, "0");
            return 0;
        });
        progress?.Report(0.1);
        await PullCore(ct, includeOwn: true);
        db.Outbox.Capture = true;
        progress?.Report(1);
        return db.People.Count();
    }), ct);

    /// <summary>After a restore: the cloud becomes exactly this PC's data (rows the backup lacks become tombstones).</summary>
    public Task RepublishAsync(CancellationToken ct) => Locked(() => Guard(async () =>
    {
        var server = new Dictionary<(string, string), CloudRow>();
        long since = 0;
        while (true)
        {
            var page = await client.PullAsync(since, PullLimit, ct);
            foreach (var r in page.Rows) server[(r.Tbl, r.Rid)] = r;
            since = page.Next;
            if (!page.More) break;
        }
        db.Outbox.Capture = true;
        db.Outbox.ClearKnown();
        db.Outbox.ClearDeferred();
        db.Outbox.MarkAll();
        var local = new HashSet<(string, string)>();
        foreach (var (tbl, k, _) in db.Outbox.Take(int.MaxValue))
        {
            var rid = CloudCrypto.RowId(key, tbl, k);
            local.Add((tbl, rid));
            if (server.TryGetValue((tbl, rid), out var r)) db.Outbox.SetKnown(tbl, k, rid, r.Ver);
        }
        foreach (var r in server.Values.Where(r => !r.Deleted && r.Data is not null && !local.Contains((r.Tbl, r.Rid))))
        {
            var k = JsonNode.Parse(CloudCrypto.Open(key, r.Tbl, r.Rid, r.Data!))!["_k"]!.GetValue<string>();
            db.Outbox.SetKnown(r.Tbl, k, r.Rid, r.Ver);
            db.Exec("INSERT OR REPLACE INTO outbox(tbl, k) VALUES($t,$k)", ("$t", r.Tbl), ("$k", k));   // no local row: pushes a tombstone
        }
        while (db.Outbox.Count() > 0 && !ct.IsCancellationRequested)
            if (await PushCore(ct) == 0) break;
        db.Settings.Set(SinceKey, Math.Max(since, server.Count == 0 ? 0 : server.Values.Max(r => r.Ver)).ToString(CultureInfo.InvariantCulture));
        return 0;
    }), ct);

    // ---------------- helpers ----------------

    private async Task<T> Locked<T>(Func<Task<T>> body, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await body(); }
        finally { _gate.Release(); }
    }

    private async Task<T> Guard<T>(Func<Task<T>> body)
    {
        try
        {
            var r = await body();
            _online = true; _error = null; _lastSync = time.GetUtcNow().UtcDateTime;
            return r;
        }
        catch (CloudOfflineException e) { _online = false; _error = e.Message; throw; }
        catch (CloudException e) { _online = true; _error = e.Message; throw; }
    }

    private string Describe(CloudRow cur, string tbl)
    {
        if (cur.Data is null) return "máy khác";
        try { return Describe(JsonNode.Parse(CloudCrypto.Open(key, tbl, cur.Rid, cur.Data))!.AsObject()); }
        catch (CloudException) { return "máy khác"; }
    }

    private static string Describe(JsonObject payload)
    {
        var pc = payload["_pc"]?.GetValue<string>() ?? "máy khác";
        return DateTime.TryParse(payload["_at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? $"{pc} lúc {at.ToLocalTime():HH:mm dd/MM}" : pc;
    }

    private static string? EmployeeOf(string tbl, string k) => tbl switch
    {
        "people" or "faces" or "fingerprints" or "device_state" or "trash" => k.Split(OutboxStore.Sep)[0],
        "jobs" => k.Split(OutboxStore.Sep)[1],
        _ => null,
    };

    private static string Show(string k) => k.Replace(OutboxStore.Sep, ' ');

    private static string Label(string tbl) => tbl switch
    {
        "people" => "người", "faces" => "khuôn mặt", "fingerprints" => "vân tay", "devices" => "máy chấm công",
        "device_state" => "trạng thái trên máy", "jobs" => "việc gửi", "trash" => "Thùng rác", "groups" => "công ty",
        "settings" => "thiết lập", _ => tbl,
    };
}
