namespace FirebirdStudio.Desktop.Views;

public sealed class QueryResultGridRow(IReadOnlyList<string> values)
{
    public string this[int index] => index >= 0 && index < values.Count ? values[index] : string.Empty;
}

public sealed record TableFieldGridRow(
    string Name,
    string DataType,
    string Domain,
    string Length,
    string Scale,
    string Nullability,
    string Position);
