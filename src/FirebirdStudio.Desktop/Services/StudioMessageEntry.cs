namespace FirebirdStudio.Desktop.Services;

internal sealed record StudioMessageEntry(
    DateTimeOffset Timestamp,
    string Severity,
    string Context,
    string Details)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("T", StudioText.Culture);
    public string TimestampText => Timestamp.ToLocalTime().ToString("G", StudioText.Culture) +
                                   Timestamp.ToLocalTime().ToString(" zzz", StudioText.Culture);
    public string Summary { get; } = Summarize(Details);
    public string SeverityLabel => StudioText.T(Severity);
    public string AccessibleDetails => $"{TimestampText} · {SeverityLabel} · {Summary}";

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
