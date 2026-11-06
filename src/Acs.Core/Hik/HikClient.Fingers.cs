namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    // STUBS: Task 8 replaces these.
    public Task<CapturedFinger> CaptureFingerprintAsync(int fingerId, CancellationToken ct) => throw new NotImplementedException();
    public Task SetFingerprintAsync(string employeeNo, int fingerId, string templateBase64, int readerNo, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<HikFinger>> ReadFingerprintsAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteFingerprintAsync(string employeeNo, int fingerId, CancellationToken ct) => throw new NotImplementedException();
}
