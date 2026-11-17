namespace FirebirdStudio.Core.Database;

/// <summary>
/// Supported compatibility targets. Actual support must be verified per feature
/// against real server versions; this enum is not a compatibility guarantee.
/// </summary>
public enum FirebirdServerVersion
{
    Firebird25,
    Firebird30,
    Firebird40,
    Firebird50
}
