using Acs.Core.Model;

namespace Acs.Core.Hik;

// Contract C2 (DeviceInfo lives in DeviceInfo.cs).
public interface IHikDevice
{
    Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken ct);
    Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct);
    IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct);
    Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct);
    Task UpsertUserAsync(HikUserWrite user, CancellationToken ct);
    Task DeleteUserAsync(string employeeNo, CancellationToken ct);
    Task<byte[]> CaptureFaceAsync(CancellationToken ct);
    Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct);
    Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct);
    /// <summary>Null means the device keeps no photo.</summary>
    Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct);
    Task DeleteFaceAsync(string employeeNo, CancellationToken ct);
    Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct);
    Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct);
    Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct);
    Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct);
}

public interface IHikDeviceFactory { IHikDevice Create(DeviceRecord device); }

public sealed record DeviceCapabilities(bool? CaptureFace, bool? CaptureFingerprint, int? MaxUsers);
public sealed record HikUser(string EmployeeNo, string Name, string BeginTime, string EndTime, bool Enabled,
    int FaceCount, int FingerCount);
public sealed record HikUserWrite(string EmployeeNo, string Name, string BeginTime, string EndTime, bool Enabled, int DoorNo);
public sealed record CapturedFinger(string TemplateBase64, int Quality);
public sealed record HikFinger(int FingerId, string TemplateBase64);
