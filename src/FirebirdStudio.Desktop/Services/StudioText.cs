using System.Globalization;
using System.Resources;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Desktop.Services;

internal static class StudioText
{
    public const string English = "en-US";
    public const string Portuguese = "pt-BR";

    private static readonly ResourceManager Resources = new(
        "FirebirdStudio.Desktop.Localization.Interface", typeof(StudioText).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(English);

    public static string ResolveLanguage(string? language) =>
        string.Equals(language, Portuguese, StringComparison.OrdinalIgnoreCase) ? Portuguese : English;

    public static void SelectLanguage(string? language) =>
        Culture = CultureInfo.GetCultureInfo(ResolveLanguage(language));

    public static string T(string key) =>
        Resources.GetString(key, Culture) ?? throw new MissingManifestResourceException(key);

    public static string T(string key, params object?[] arguments) =>
        string.Format(Culture, T(key), arguments);

    public static string ThemeName(string theme) => T(theme == "Dark" ? "ThemeDark" : "ThemeLight");

    public static string MetadataCategoryName(string category) => category switch
    {
        "Column" => T("Columns"),
        "Tables" or DatabaseMetadataObject.SystemTablesKind or "Views" or "Procedures" or "Triggers" or "Indexes" or "Domains" => T(category),
        _ => category
    };
}
