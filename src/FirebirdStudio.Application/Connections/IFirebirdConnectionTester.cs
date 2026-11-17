using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Application.Connections;

public sealed record ConnectionTestResult(
    bool Succeeded,
    string Message,
    string? ServerVersion = null,
    string? Diagnostics = null);

public interface IFirebirdConnectionTester
{
    Task<ConnectionTestResult> TestAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);
}
