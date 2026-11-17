namespace FirebirdStudio.Core.Database;

/// <summary>A read-only object discovered in the Firebird system catalog.</summary>
public sealed record DatabaseMetadataObject(
    string Kind,
    string Name,
    string? ParentName,
    string Details,
    IReadOnlyList<DatabaseMetadataObject>? Children = null);
