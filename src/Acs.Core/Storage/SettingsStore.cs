namespace Acs.Core.Storage;

public sealed class SettingsStore
{
    private readonly Database _db;
    internal SettingsStore(Database db) => _db = db;

    public string? Get(string key) => _db.Scalar("SELECT value FROM settings WHERE key=$k", ("$k", key)) as string;

    public void Set(string key, string value) => _db.Exec(
        "INSERT INTO settings(key, value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
}
