using System.Text.Json;
using System.Text.Json.Nodes;
using Acs.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Acs.Core.Cloud;

internal enum ApplyResult { Applied, Deferred }

/// <summary>
/// One shared table: reads a local row (by natural key) into plaintext JSON and applies such JSON on another PC.
/// Local ids never leave the PC: device ids travel as fp_group, group ids as the group name. Sealed columns (photo,
/// template, device password, trash data) are opened on read and sealed again with the receiving PC's own key.
/// </summary>
internal sealed class SyncTable
{
    public required string Name { get; init; }
    /// <summary>Natural key columns in key order. "fp_group" with <see cref="DeviceKey"/> means the device_id column.</summary>
    public required string[] KeyCols { get; init; }
    public required string[] DataCols { get; init; }
    public string[] Sealed { get; init; } = [];
    public bool DeviceKey { get; init; }
    /// <summary>A device_id data column sent as "device" (fp_group) and mapped back without deferring (log lines).</summary>
    public bool DeviceData { get; init; }
    /// <summary>people.group_id sent as "group" (name).</summary>
    public bool GroupData { get; init; }
    public string? ReadWhere { get; init; }
    public string? ExtraSet { get; init; }   // e.g. "draft=0"
    public bool OpenJobClash { get; init; }

    private string Table => Name;

    private (string Where, List<(string, object?)> Args)? KeyWhere(Database db, string key)
    {
        var parts = key.Split(OutboxStore.Sep);
        if (parts.Length != KeyCols.Length) return null;
        var where = new List<string>();
        var args = new List<(string, object?)>();
        for (var i = 0; i < KeyCols.Length; i++)
        {
            if (DeviceKey && KeyCols[i] == "fp_group")
            {
                var dev = db.Scalar("SELECT id FROM devices WHERE fp_group=$g", ("$g", parts[i]));
                if (dev is null) return null;
                where.Add("device_id=$k" + i);
                args.Add(("$k" + i, dev));
            }
            else
            {
                where.Add(KeyCols[i] + "=$k" + i);
                args.Add(("$k" + i, parts[i]));
            }
        }
        return (string.Join(" AND ", where), args);
    }

    public JsonObject? Read(Database db, string key)
    {
        if (KeyWhere(db, key) is not { } kw) return null;
        var cols = DataCols.ToList();
        if (DeviceData) cols.Add("device_id");
        if (GroupData) cols.Add("group_id");
        var where = kw.Where + (ReadWhere is null ? "" : " AND " + ReadWhere);
        if (cols.Count == 0) cols.Add("1");   // groups: the key is all there is
        return db.Query($"SELECT {string.Join(", ", cols)} FROM {Table} WHERE {where}", r =>
        {
            var o = new JsonObject();
            foreach (var c in DataCols)
            {
                var v = r[c];
                o[c] = v switch
                {
                    DBNull => null,
                    byte[] b when Sealed.Contains(c) => Convert.ToBase64String(db.Protector.Open(b)),
                    byte[] b => Convert.ToBase64String(b),
                    long l => l,
                    double d => d,
                    _ => v.ToString(),
                };
            }
            if (DeviceData) o["device"] = r["device_id"] is long id ? db.Scalar("SELECT fp_group FROM devices WHERE id=$i", ("$i", id)) as string : null;
            if (GroupData) o["group"] = r["group_id"] is long g ? db.Scalar("SELECT name FROM groups WHERE id=$i", ("$i", g)) as string : null;
            return o;
        }, kw.Args.ToArray()).FirstOrDefault();
    }

    public ApplyResult Apply(Database db, string key, JsonObject? row)
    {
        var parts = key.Split(OutboxStore.Sep);
        if (DeviceKey && db.Scalar("SELECT 1 FROM devices WHERE fp_group=$g", ("$g", parts[Array.IndexOf(KeyCols, "fp_group")])) is null)
            return row is null ? ApplyResult.Applied : ApplyResult.Deferred;   // nothing to delete without the device
        var kw = KeyWhere(db, key)!.Value;
        if (row is null)
        {
            db.Exec($"DELETE FROM {Table} WHERE {kw.Where}", kw.Args.ToArray());
            return ApplyResult.Applied;
        }

        var data = new List<(string Col, object? Val)>();
        foreach (var c in DataCols)
        {
            var v = Value(row[c]);
            if (v is string s && Sealed.Contains(c)) v = db.Protector.Seal(Convert.FromBase64String(s));
            data.Add((c, v));
        }
        if (DeviceData)
            data.Add(("device_id", row["device"]?.GetValue<string>() is { } fg ? db.Scalar("SELECT id FROM devices WHERE fp_group=$g", ("$g", fg)) : null));
        if (GroupData)
            data.Add(("group_id", row["group"]?.GetValue<string>() is { } gn ? (object)db.People.GetOrCreateGroup(gn) : null));

        var dataArgs = data.Select((d, i) => ("$v" + i, d.Val)).ToList();
        var set = string.Join(", ", data.Select((d, i) => $"{d.Col}=$v{i}").Concat(ExtraSet is null ? [] : [ExtraSet]));
        try
        {
            if (db.Scalar($"SELECT 1 FROM {Table} WHERE {kw.Where}", kw.Args.ToArray()) is not null)
            {
                if (set.Length > 0) db.Exec($"UPDATE {Table} SET {set} WHERE {kw.Where}", kw.Args.Concat(dataArgs).ToArray());
                return ApplyResult.Applied;
            }
            var keyCols = KeyCols.Select(k => DeviceKey && k == "fp_group" ? "device_id" : k);
            var cols = keyCols.Concat(data.Select(d => d.Col)).ToList();
            var vals = kw.Args.Select(a => a.Item1).Concat(dataArgs.Select(a => a.Item1)).ToList();
            if (ExtraSet is not null) { var p = ExtraSet.Split('='); cols.Add(p[0]); vals.Add(p[1]); }
            db.Exec($"INSERT INTO {Table}({string.Join(", ", cols)}) VALUES({string.Join(", ", vals)})", kw.Args.Concat(dataArgs).ToArray());
            return ApplyResult.Applied;
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == 787)   // SQLITE_CONSTRAINT_FOREIGNKEY: the person is not here yet
        {
            return ApplyResult.Deferred;
        }
        catch (SqliteException e) when (OpenJobClash && e.SqliteErrorCode == 19)
        {
            // Another open job for the same device/person/kind/finger exists here: that is the same job, keep one.
            var open = kw.Where.Replace("created_at=$k4", "status IN ('pending','running','failed')");
            db.Exec($"UPDATE {Table} SET {set}, created_at=$k4 WHERE {open}", kw.Args.Concat(dataArgs).ToArray());
            return ApplyResult.Applied;
        }
    }

    private static object? Value(JsonNode? n)
    {
        if (n is null) return null;
        var e = n.GetValue<JsonElement>();
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.True => 1L,
            JsonValueKind.False => 0L,
            _ => null,
        };
    }
}

internal static class SyncTables
{
    /// <summary>Apply order: parents before children.</summary>
    public static readonly IReadOnlyList<SyncTable> All =
    [
        new() { Name = "settings", KeyCols = ["key"], DataCols = ["value"] },
        new() { Name = "groups", KeyCols = ["name"], DataCols = [] },
        new() { Name = "devices", KeyCols = ["fp_group"], DataCols = ["name", "host", "username", "password", "door_no", "is_capture_default", "is_finger_reader", "created_at"], Sealed = ["password"] },
        new() { Name = "people", KeyCols = ["employee_no"], DataCols = ["full_name", "valid_from", "valid_to", "status", "created_at", "updated_at", "locked_at"], GroupData = true, ReadWhere = "draft=0", ExtraSet = "draft=0" },
        new() { Name = "faces", KeyCols = ["employee_no"], DataCols = ["jpeg", "source", "captured_at"], Sealed = ["jpeg"] },
        new() { Name = "fingerprints", KeyCols = ["employee_no", "finger_id", "fp_group"], DataCols = ["template", "quality", "source", "captured_at"], Sealed = ["template"] },
        new() { Name = "device_state", KeyCols = ["employee_no", "fp_group"], DeviceKey = true, DataCols = ["user_present", "user_enabled", "face_present", "fingers", "needs_scan", "verified_at"] },
        new() { Name = "jobs", KeyCols = ["fp_group", "employee_no", "kind", "finger_id", "created_at"], DeviceKey = true, OpenJobClash = true, DataCols = ["status", "attempts", "next_at", "error_kind", "error_message", "error_detail", "updated_at"] },
        new() { Name = "trash", KeyCols = ["employee_no", "deleted_at"], DataCols = ["full_name", "group_name", "has_face", "finger_count", "data"], Sealed = ["data"] },
        new() { Name = "log", KeyCols = ["origin"], DataCols = ["at", "employee_no", "action", "ok", "message", "pc"], DeviceData = true },
    ];

    public static SyncTable Get(string name) => All.First(t => t.Name == name);
}
