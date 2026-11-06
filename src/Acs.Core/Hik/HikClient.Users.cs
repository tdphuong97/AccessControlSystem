namespace Acs.Core.Hik;

public sealed partial class HikClient
{
    // STUBS: Task 6 replaces these.
    public IAsyncEnumerable<HikUser> ListUsersAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<HikUser?> FindUserAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
    public Task UpsertUserAsync(HikUserWrite user, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteUserAsync(string employeeNo, CancellationToken ct) => throw new NotImplementedException();
}
