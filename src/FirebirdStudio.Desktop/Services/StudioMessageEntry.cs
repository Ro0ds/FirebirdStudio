using Avalonia.Media;

namespace FirebirdStudio.Desktop.Services;

internal sealed record StudioMessageEntry(
    DateTimeOffset Timestamp,
    string Severity,
    string Context,
    string Details)
{
    private static readonly IBrush SuccessBackground = new SolidColorBrush(Color.Parse("#E7F4E8"));
    private static readonly IBrush InformationBackground = new SolidColorBrush(Color.Parse("#FFF5D9"));
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#FBE9E7"));
    private static readonly IBrush SuccessForeground = new SolidColorBrush(Color.Parse("#245B2A"));
    private static readonly IBrush InformationForeground = new SolidColorBrush(Color.Parse("#765500"));
    private static readonly IBrush ErrorForeground = new SolidColorBrush(Color.Parse("#8A2924"));

    public string TimestampText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    public string Header => $"{Severity.ToUpperInvariant()}  ·  {Context}";

    public IBrush Background => Severity switch
    {
        "Success" => SuccessBackground,
        "Information" => InformationBackground,
        _ => ErrorBackground
    };

    public IBrush Foreground => Severity switch
    {
        "Success" => SuccessForeground,
        "Information" => InformationForeground,
        _ => ErrorForeground
    };
}
