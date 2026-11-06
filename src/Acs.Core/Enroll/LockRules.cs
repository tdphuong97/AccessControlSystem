using Acs.Core.Model;
using Acs.Core.Storage;

namespace Acs.Core.Enroll;

public static class LockRules
{
    /// <summary>Per device: DeleteUser when the app holds a full copy of what the device has, else DisableUser.
    /// Call inside a Database.Tx so the enqueues commit together with the status change. The worker checks the
    /// device itself again before deleting (DeviceWorker, DeleteUser of a locked person).</summary>
    public static void EnqueueLock(Database db, string employeeNo, DateTime now)
    {
        var hasFace = db.Biometrics.HasFace(employeeNo);
        var groups = db.Biometrics.FingerGroups(employeeNo);
        foreach (var dev in db.Devices.List())
        {
            var state = db.Jobs.GetState(employeeNo, dev.Id);
            // Restorable here = a template in this device's OWN sensor group: a template of another group may be
            // refused by this sensor, and then the finger would be lost.
            var own = groups.Where(g => g.FpGroup == dev.FpGroup).Select(g => g.FingerId).ToHashSet();
            // No state row, or a finger list that could not be read = device unknown: never delete what the app
            // cannot prove it can restore (R20).
            var full = state is { FingersKnown: true } && (!state.FacePresent || hasFace) && state.Fingers.All(own.Contains);
            db.Jobs.Enqueue(dev.Id, employeeNo, full ? JobKind.DeleteUser : JobKind.DisableUser, 0, now);
        }
    }
}
