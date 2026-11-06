namespace Acs.Core.Host;

/// <summary>View records for the UI bridge (C3). Serialized camelCase with <see cref="AppHost.Json"/>.</summary>
public sealed record PersonRow(string EmployeeNo, string FullName, string Initials, string? GroupName, bool HasFace,
    int FingerCount, string Sync, IReadOnlyList<string> WaitingDevices, bool IsNew, bool Locked);

public sealed record FingerView(int FingerId, string Label, int? Quality);

public sealed record SyncItem(string Label, string State, string? Message, string? Detail);

public sealed record DeviceSync(int DeviceId, string DeviceName, string State, IReadOnlyList<SyncItem> Items);

public sealed record PersonDetail(string EmployeeNo, string FullName, string? GroupName, string ValidFrom, string? ValidTo,
    bool Locked, string? FaceDataUrl, IReadOnlyList<FingerView> Fingers, IReadOnlyList<DeviceSync> Devices);

public sealed record DeviceView(int Id, string Name, string Host, string Username, int DoorNo, bool IsCaptureDefault,
    bool IsFingerReader, string? Model, bool Online, bool AuthPaused, DateTime? LastSeen, int Pending, int Failed, string? Message);

public sealed record UpdateView(string Version, string Notes, double SizeMb);

/// <param name="NeedsReconcile">True after a new device was added (with data to send) or a backup was restored, until a
/// reconcile.apply succeeds or a plan finds nothing to send: the people screen asks the admin to run So khớp.</param>
public sealed record AppStateView(string Version, bool FirstRun, IReadOnlyList<DeviceView> Devices, UpdateView? Update,
    int PendingJobs, int FailedJobs, int StaleLocked, bool NeedsReconcile);

public sealed record FaceResult(string ImageDataUrl, IReadOnlyList<DeviceSync> Devices);

public sealed record FingerResult(int FingerId, string Label, int Quality, IReadOnlyList<DeviceSync> Devices);

public sealed record DeviceTestResult(bool Ok, string? Model, string? Firmware, int Users, bool CanCaptureFace,
    bool CanCaptureFinger, string Message);

public sealed record BackupFileView(string Name, string At, long Size);

public sealed record BackupStatus(bool HasPassword, string? LastAt, IReadOnlyList<BackupFileView> Files, string? ExtraFolder);

public sealed record SettingsView(bool AutoCheckUpdates, bool StartWithWindows);

public sealed record LogRow(DateTime At, string? DeviceName, string? EmployeeNo, string Action, bool Ok, string? Message);

/// <summary>A message for the user that is already Vietnamese and passes through <see cref="AppHost.HandleAsync"/> unchanged.</summary>
public sealed class HostException(string message) : Exception(message);
