namespace FirebirdStudio.Core.Connections;

public static class ConnectionProfileValidator
{
    public static IReadOnlyList<string> Validate(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<string>();

        if (profile.Id == Guid.Empty)
            errors.Add("A connection profile must have a non-empty identifier.");

        if (string.IsNullOrWhiteSpace(profile.Name))
            errors.Add("Connection name is required.");
        else if (ContainsControlCharacters(profile.Name))
            errors.Add("Connection name contains invalid control characters.");

        if (!Enum.IsDefined(profile.ServerType))
            errors.Add("Server type is invalid.");

        if (profile.ServerType == ConnectionServerType.Remote)
        {
            if (string.IsNullOrWhiteSpace(profile.Host))
                errors.Add("Server host is required for a remote connection.");
            else if (ContainsControlCharacters(profile.Host))
                errors.Add("Server host contains invalid control characters.");
        }

        if (profile.Port is < 1 or > 65535)
            errors.Add("Port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(profile.Database))
            errors.Add("Database path or server-side alias is required.");
        else if (ContainsControlCharacters(profile.Database))
            errors.Add("Database path contains invalid control characters.");

        if (string.IsNullOrWhiteSpace(profile.UserName))
            errors.Add("User name is required.");
        else if (ContainsControlCharacters(profile.UserName))
            errors.Add("User name contains invalid control characters.");

        if (profile.Role is not null && ContainsControlCharacters(profile.Role))
            errors.Add("Role contains invalid control characters.");

        if (string.IsNullOrWhiteSpace(profile.CharacterSet))
            errors.Add("Character set is required.");
        else if (ContainsControlCharacters(profile.CharacterSet))
            errors.Add("Character set contains invalid control characters.");

        if (profile.ClientLibraryPath is not null && ContainsControlCharacters(profile.ClientLibraryPath))
            errors.Add("Client library path contains invalid control characters.");

        if (profile.ServerType == ConnectionServerType.Embedded &&
            profile.EmbeddedEngineVersion is null && string.IsNullOrWhiteSpace(profile.ClientLibraryPath))
            errors.Add("A client library path is required for an embedded connection.");
        else if (profile.ServerType == ConnectionServerType.Remote &&
                 !string.IsNullOrWhiteSpace(profile.ClientLibraryPath))
            errors.Add("Client library path is only used by embedded connections.");

        if (profile.EmbeddedEngineVersion is { } engineVersion)
        {
            if (profile.ServerType != ConnectionServerType.Embedded || !string.IsNullOrWhiteSpace(profile.ClientLibraryPath))
                errors.Add("A managed engine requires an embedded connection without an external client library.");
            if (engineVersion is not ("2.5" or "3.0" or "4.0" or "5.0"))
                errors.Add("The managed Firebird engine version is unavailable.");
        }

        if (profile.SqlDialect is < 1 or > 3)
            errors.Add("SQL dialect must be 1, 2, or 3.");

        if (profile.ConnectionTimeoutSeconds is < 1 or > 300)
            errors.Add("Connection timeout must be between 1 and 300 seconds.");

        if (profile.PacketSize is < 512 or > 32767)
            errors.Add("Packet size must be between 512 and 32767 bytes.");

        if (profile.FetchSize is < 1 or > 100000)
            errors.Add("Fetch size must be between 1 and 100000 rows.");

        if (profile.DatabaseCachePages < 0)
            errors.Add("Database cache pages cannot be negative.");

        if (profile.MinPoolSize is < 0 or > 10000)
            errors.Add("Minimum pool size must be between 0 and 10000.");

        if (profile.MaxPoolSize is < 1 or > 10000)
            errors.Add("Maximum pool size must be between 1 and 10000.");

        if (profile.MinPoolSize > profile.MaxPoolSize)
            errors.Add("Minimum pool size cannot exceed maximum pool size.");

        if (profile.ConnectionLifetimeSeconds is < 0 or > 86400)
            errors.Add("Connection lifetime must be between 0 and 86400 seconds.");

        if (profile.DisableDatabaseTriggers && profile.Pooling)
            errors.Add("Connection pooling cannot be enabled when database triggers are suppressed.");

        return errors;
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);
}
