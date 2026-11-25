using Avalonia.Data.Converters;
using FirebirdStudio.Core.Database;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop.Views;

public sealed class MetadataObjectLabelConverter : FuncValueConverter<DatabaseMetadataObject, string?>
{
    public MetadataObjectLabelConverter() : base(item =>
        item?.Kind == "Category" ? StudioText.MetadataCategoryName(item.Name) : item?.Name)
    {
    }
}
