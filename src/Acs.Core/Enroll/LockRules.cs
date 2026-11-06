using Acs.Core.Model;
using Acs.Core.Storage;

namespace Acs.Core.Enroll;

public static class LockRules
{
    /// <summary>Per device: DeleteUser when the app holds a full copy of what the device has, else DisableUser.
    /// Call inside a Database.Tx so the enqueues commit together with the status change.</summary>
    public static void EnqueueLock(Database db, string employeeNo, DateTime now)
    {
        var hasFace = db.Biometrics.GetFace(employeeNo) is not null;
        var fingerIds = db.Biometrics.GetFingers(employeeNo).Select(f => f.FingerId).ToHashSet();
        foreach (var dev in db.Devices.List())
        {
            var state = db.Jobs.GetState(employeeNo, dev.Id);
            // Same rule the worker uses when it picks a template: any group's template may serve a finger.
            // No state row = device unknown: never delete what the app cannot prove it can restore.
            var full = state is not null && ((!state.FacePresent || hasFace) && state.Fingers.All(fingerIds.Contains));
            db.Jobs.Enqueue(dev.Id, employeeNo, full ? JobKind.DeleteUser : JobKind.DisableUser, 0, now);
        }
    }
}
