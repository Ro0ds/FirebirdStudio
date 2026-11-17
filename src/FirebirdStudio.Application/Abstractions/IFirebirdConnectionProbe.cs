namespace FirebirdStudio.Application.Abstractions;

/// <summary>
/// Minimal connectivity boundary. This only defines the application contract;
/// connection testing and its version-specific behavior are implemented later.
/// </summary>
public interface IFirebirdConnectionProbe
{
    Task<bool> CanConnectAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
}
