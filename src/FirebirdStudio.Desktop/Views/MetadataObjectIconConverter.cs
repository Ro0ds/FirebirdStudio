using Avalonia.Data.Converters;
using Avalonia.Media;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Desktop.Views;

public sealed class MetadataObjectIconConverter : FuncValueConverter<DatabaseMetadataObject, Geometry>
{
    private static readonly Geometry Folder = Geometry.Parse("M1 3H6L8 5H15V13H1Z");
    private static readonly Geometry Table = Geometry.Parse("F0 M1 2H15V14H1Z M3 6H7V9H3Z M9 6H13V9H9Z M3 10H7V12H3Z M9 10H13V12H9Z");
    private static readonly Geometry View = Geometry.Parse("F0 M0 8Q4 2 8 2Q12 2 16 8Q12 14 8 14Q4 14 0 8Z M5 8A3 3 0 1 0 11 8A3 3 0 1 0 5 8Z");
    private static readonly Geometry Column = Geometry.Parse("F0 M4 1H12V15H4Z M6 5V13H10V5Z");
    private static readonly Geometry Procedure = Geometry.Parse("M1 2H15V4H1Z M1 7H6V9H1Z M8 6L14 9.5L8 13Z M1 12H6V14H1Z");
    private static readonly Geometry Trigger = Geometry.Parse("M9 1L3 9H7L6 15L13 7H9Z");
    private static readonly Geometry Index = Geometry.Parse("M1 2H4V5H1Z M6 3H15V4.5H6Z M1 7H4V10H1Z M6 8H13V9.5H6Z M1 12H4V15H1Z M6 13H11V14.5H6Z");
    private static readonly Geometry Domain = Geometry.Parse("F0 M8 1L15 8L8 15L1 8Z M8 4L4 8L8 12L12 8Z");
    private static readonly Geometry GenericObject = Geometry.Parse("F0 M3 1H10L13 4V15H3Z M5 7H11V13H5Z");

    public MetadataObjectIconConverter() : base(item => item?.Kind switch
    {
        "Category" => Folder,
        "Tables" or DatabaseMetadataObject.SystemTablesKind => Table,
        "Views" => View,
        "Column" => Column,
        "Procedures" => Procedure,
        "Triggers" => Trigger,
        "Indexes" => Index,
        "Domains" => Domain,
        _ => GenericObject
    })
    {
    }
}
