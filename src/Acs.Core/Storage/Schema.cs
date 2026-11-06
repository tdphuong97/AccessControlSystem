namespace Acs.Core.Storage;

/// <summary>Schema v1 as a constant (an embedded Schema.sql would need a csproj edit).</summary>
internal static class Schema
{
    public const int Version = 1;

    public const string V1 = """
CREATE TABLE devices(
  id INTEGER PRIMARY KEY, name TEXT NOT NULL, host TEXT NOT NULL, username TEXT NOT NULL, password BLOB NOT NULL,
  door_no INTEGER NOT NULL DEFAULT 1, is_capture_default INTEGER NOT NULL DEFAULT 0, is_finger_reader INTEGER NOT NULL DEFAULT 0,
  model TEXT, serial TEXT, firmware TEXT, fp_group TEXT NOT NULL, auth_paused INTEGER NOT NULL DEFAULT 0,
  last_seen TEXT, created_at TEXT NOT NULL);
CREATE TABLE groups(id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL);
CREATE TABLE people(
  employee_no TEXT PRIMARY KEY, full_name TEXT NOT NULL, group_id INTEGER REFERENCES groups(id) ON DELETE SET NULL,
  valid_from TEXT NOT NULL, valid_to TEXT NULL, status TEXT NOT NULL DEFAULT 'active',
  created_at TEXT NOT NULL, updated_at TEXT NOT NULL, locked_at TEXT NULL);
CREATE TABLE faces(
  employee_no TEXT PRIMARY KEY REFERENCES people(employee_no) ON DELETE CASCADE,
  jpeg BLOB NOT NULL, source TEXT NOT NULL, captured_at TEXT NOT NULL);
CREATE TABLE fingerprints(
  employee_no TEXT NOT NULL REFERENCES people(employee_no) ON DELETE CASCADE,
  finger_id INTEGER NOT NULL CHECK(finger_id BETWEEN 1 AND 10), fp_group TEXT NOT NULL, template BLOB NOT NULL,
  quality INTEGER, source TEXT NOT NULL, captured_at TEXT NOT NULL, PRIMARY KEY(employee_no, finger_id, fp_group));
CREATE TABLE device_state(
  employee_no TEXT NOT NULL REFERENCES people(employee_no) ON DELETE CASCADE,
  device_id INTEGER NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  user_present INTEGER NOT NULL DEFAULT 0, user_enabled INTEGER NOT NULL DEFAULT 0, face_present INTEGER NOT NULL DEFAULT 0,
  fingers TEXT NOT NULL DEFAULT '', needs_scan TEXT NOT NULL DEFAULT '', verified_at TEXT,
  PRIMARY KEY(employee_no, device_id));
CREATE TABLE jobs(
  id INTEGER PRIMARY KEY, device_id INTEGER NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  employee_no TEXT NOT NULL, kind TEXT NOT NULL, finger_id INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'pending',
  attempts INTEGER NOT NULL DEFAULT 0, next_at TEXT NOT NULL, error_kind TEXT, error_message TEXT, error_detail TEXT,
  created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE UNIQUE INDEX jobs_open ON jobs(device_id, employee_no, kind, finger_id) WHERE status IN ('pending','running','failed');
CREATE TABLE log(id INTEGER PRIMARY KEY, at TEXT NOT NULL, employee_no TEXT, device_id INTEGER, action TEXT NOT NULL, ok INTEGER NOT NULL, message TEXT);
CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT);
""";
}
