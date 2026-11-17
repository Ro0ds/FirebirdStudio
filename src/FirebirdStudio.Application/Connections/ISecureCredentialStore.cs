namespace FirebirdStudio.Application.Connections;

/// <summary>
/// Boundary for OS-backed credential storage. Implementations must use the
/// platform's protected credential facility and must fail closed when unavailable.
/// </summary>
public interface ISecureCredentialStore
{
    Task SavePasswordAsync(Guid profileId, string password, CancellationToken cancellationToken = default);
    Task<string?> GetPasswordAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task DeletePasswordAsync(Guid profileId, CancellationToken cancellationToken = default);
}
