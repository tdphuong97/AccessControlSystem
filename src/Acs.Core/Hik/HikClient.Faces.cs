namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    // STUBS: Task 7 replaces these.
    public Task<byte[]> CaptureFaceAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task SetFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct) => throw new NotImplementedException();
    public Task<bool> HasFaceAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
    public Task<byte[]?> DownloadFaceAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteFaceAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
}
