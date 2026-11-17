namespace FirebirdStudio.Core.Connections;

public enum ConnectionServerType
{
    Remote = 0,
    Embedded = 1
}

/// <summary>
/// Non-secret connection settings. Passwords and other credentials must never be
/// added to this type because profiles are persisted as ordinary application data.
/// </summary>
public sealed record ConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public ConnectionServerType ServerType { get; init; } = ConnectionServerType.Remote;
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 3050;
    /// <summary>Database file path or server-side database alias.</summary>
    public string Database { get; init; } = string.Empty;
    public string UserName { get; init; } = "SYSDBA";
    public string? Role { get; init; }
    public string CharacterSet { get; init; } = "UTF8";
    public int SqlDialect { get; init; } = 3;
    public int ConnectionTimeoutSeconds { get; init; } = 15;

    /// <summary>Optional native client library path, primarily for embedded connections.</summary>
    public string? ClientLibraryPath { get; init; }

    public int PacketSize { get; init; } = 8192;
    public int FetchSize { get; init; } = 200;
    public int DatabaseCachePages { get; init; }
    public bool Pooling { get; init; } = true;
    public int MinPoolSize { get; init; }
    public int MaxPoolSize { get; init; } = 100;
    public int ConnectionLifetimeSeconds { get; init; }
    public bool Compression { get; init; }
    public bool DisableDatabaseTriggers { get; init; }
    public bool DisableGarbageCollection { get; init; }
}
