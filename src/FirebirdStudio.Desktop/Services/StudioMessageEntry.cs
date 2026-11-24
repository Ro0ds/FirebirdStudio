namespace FirebirdStudio.Desktop.Services;

internal sealed record StudioMessageEntry(
    DateTimeOffset Timestamp,
    string Severity,
    string Context,
    string Details)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("T");
    public string TimestampText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    public string Summary { get; } = Summarize(Details);
    public string AccessibleDetails => $"{TimestampText} · {Severity} · {Summary}";

    public bool IsSuccess => Severity == "Success";
    public bool IsWarning => Severity == "Warning";
    public bool IsError => Severity == "Error";

    private static string Summarize(string details)
    {
        const int maximumLength = 160;
        var summary = details.AsSpan().TrimStart();
        var lineEnd = summary.IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
            summary = summary[..lineEnd].TrimEnd();
        return summary.Length <= maximumLength ? summary.ToString() : $"{summary[..maximumLength]}…";
    }
}
