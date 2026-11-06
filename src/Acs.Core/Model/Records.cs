namespace Acs.Core.Model;

public enum PersonStatus { Active, Locked }
public enum BiometricSource { Device, Webcam, File, Imported }
public enum JobKind { UpsertUser, DisableUser, DeleteUser, SetFace, DeleteFace, SetFinger, DeleteFinger }
public enum JobStatus { Pending, Running, Done, Failed }
public sealed record PersonRecord(string EmployeeNo, string FullName, int? GroupId, DateOnly ValidFrom, DateOnly? ValidTo,
    PersonStatus Status, DateTime CreatedAt, DateTime UpdatedAt, DateTime? LockedAt);
public sealed record DeviceRecord(int Id, string Name, string Host, string Username, string Password, int DoorNo,
    bool IsCaptureDefault, bool IsFingerReader, string? Model, string? Serial, string? Firmware, string FpGroup,
    bool AuthPaused, DateTime? LastSeen);
public sealed record FaceRecord(string EmployeeNo, byte[] Jpeg, BiometricSource Source, DateTime CapturedAt);
public sealed record FingerRecord(string EmployeeNo, int FingerId, string FpGroup, string TemplateBase64, int? Quality,
    BiometricSource Source, DateTime CapturedAt);
public sealed record JobRecord(long Id, int DeviceId, string EmployeeNo, JobKind Kind, int FingerId, JobStatus Status,
    int Attempts, DateTime NextAt, string? ErrorKind, string? ErrorMessage, string? ErrorDetail);
public sealed record DeviceStateRecord(string EmployeeNo, int DeviceId, bool UserPresent, bool UserEnabled,
    bool FacePresent, IReadOnlySet<int> Fingers, IReadOnlySet<int> NeedsScan, DateTime? VerifiedAt)
{
    /// <summary>False when the device's finger list could not be read: <see cref="Fingers"/> then holds only the
    /// fingers confirmed present, not the complete list, so nothing may be deleted on the strength of it.</summary>
    public bool FingersKnown { get; init; } = true;
}
