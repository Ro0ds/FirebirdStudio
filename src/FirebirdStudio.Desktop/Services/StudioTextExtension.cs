namespace FirebirdStudio.Desktop.Services;

public sealed class StudioTextExtension(string key)
{
    public string ProvideValue() => StudioText.T(key);
}
