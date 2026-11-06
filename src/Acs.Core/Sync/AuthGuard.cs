using Acs.Core.Hik;
using Acs.Core.Model;
using Acs.Core.Storage;

namespace Acs.Core.Sync;

/// <summary>
/// Spec 5.2 / 7: after a wrong password a device is paused (AuthPaused) and nothing may log in to it again until the
/// admin changes the login (devices.save clears the pause) or presses Thử lại (sync.retry): Hikvision locks the IP
/// for about 30 minutes after a few failed logins. The device worker already obeys this; user-started calls
/// (captures, devices.test, reconcile, import) use this guard.
/// </summary>
public static class AuthGuard
{
    /// <summary>Refuses locally, without touching the device, while it is paused for a wrong password.</summary>
    public static void Require(DeviceRecord d)
    {
        if (d.AuthPaused) throw new HikAuthException(JobRules.AuthMessage);
    }

    /// <summary>A user-started call got a 401 after the digest retry: pause the device, as its worker would.</summary>
    public static void Pause(Database db, int deviceId, SyncEngine? sync, string? employeeNo = null)
    {
        try
        {
            db.Devices.SetAuthPaused(deviceId, true);
            db.Log.Add(employeeNo, deviceId, "Auth", false, JobRules.AuthMessage);
        }
        catch { /* best-effort: the worker pauses on its own next 401 */ }
        sync?.Wake(deviceId);   // the worker sees the pause and announces it (devices.changed)
    }
}
