namespace FirebirdStudio.Application.Connections;

public sealed record DatabaseTableDataPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int Offset,
    int PageSize,
    bool HasPrevious,
    bool HasNext,
    bool HasStableOrder,
    IReadOnlyList<object?>? FirstKeyValues,
    IReadOnlyList<object?>? LastKeyValues);
