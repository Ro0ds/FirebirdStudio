namespace FirebirdStudio.Core.Database;

/// <summary>A read-only object discovered in the Firebird system catalog.</summary>
public sealed record DatabaseMetadataObject(
    string Kind,
    string Name,
    string? ParentName,
    string Details,
    IReadOnlyList<DatabaseMetadataObject>? Children = null)
{
    /// <summary>
    /// Stores expansion state with the item rather than its virtualized TreeViewItem
    /// container, which may be recycled when additional metadata pages are loaded.
    /// </summary>
    public bool IsExpanded { get; set; }
}
