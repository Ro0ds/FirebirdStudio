using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Application.Sql;

public static class FirebirdDdlPreview
{
    public static string FormatSource(string kind, string? source) => kind switch
    {
        "View source" => FirebirdSqlFormatter.Format(source),
        "Trigger source" or "Procedure source" => FirebirdSqlFormatter.FormatProceduralSource(source),
        _ => source ?? string.Empty
    };

    public static string? ReadDetail(string details, string key)
    {
        foreach (var part in details.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf(':');
            if (separator <= 0)
                continue;
            if (string.Equals(part[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return part[(separator + 1)..].Trim();
        }

        return null;
    }

    public static string DescribeFieldType(string details)
    {
        var typeCode = ReadDetail(details, "type code");
        var subtype = ReadDetail(details, "subtype") ?? "0";
        var length = ReadDetail(details, "length") ?? "?";
        var characterLength = ReadDetail(details, "character length");
        var precision = int.TryParse(ReadDetail(details, "precision"), out var parsedPrecision)
            ? parsedPrecision
            : 0;
        var scale = int.TryParse(ReadDetail(details, "scale"), out var parsedScale) ? Math.Abs(parsedScale) : 0;
        return typeCode switch
        {
            "7" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 4)}, {scale})",
            "7" => "SMALLINT",
            "8" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 9)}, {scale})",
            "8" => "INTEGER",
            "9" => "BIGINT",
            "10" => "FLOAT",
            "11" => "D_FLOAT",
            "12" => "DATE",
            "13" => "TIME",
            "14" => $"CHAR({characterLength ?? length})",
            "16" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 18)}, {scale})",
            "16" => "BIGINT",
            "23" => "BOOLEAN",
            "24" => "DECFLOAT(16)",
            "25" => "DECFLOAT(34)",
            "26" => "INT128",
            "27" => "DOUBLE PRECISION",
            "28" => "TIME WITH TIME ZONE",
            "29" => "TIMESTAMP WITH TIME ZONE",
            "35" => "TIMESTAMP",
            "37" => $"VARCHAR({characterLength ?? length})",
            "40" => $"CSTRING({length})",
            "261" => subtype == "1" ? "BLOB SUB_TYPE TEXT" : "BLOB",
            _ => $"TYPE {typeCode ?? "?"} (length {length})"
        };
    }


    public static string BuildTableDdl(
        string tableName,
        IReadOnlyList<DatabaseMetadataObject> fields,
        IReadOnlyList<DatabaseMetadataObject> constraints)
    {
        var definitions = new List<string>();
        foreach (var field in fields)
        {
            var definition = $"    {QuoteIdentifier(field.Name)} {DescribeFieldType(field.Details)}";
            var typeCode = ReadDetail(field.Details, "type code");
            var characterSet = ReadDetail(field.Details, "charset");
            if (((typeCode is "14" or "37") || (typeCode == "261" && ReadDetail(field.Details, "subtype") == "1")) &&
                !string.IsNullOrWhiteSpace(characterSet))
                definition += $" CHARACTER SET {QuoteIdentifier(characterSet)}";
            if (field.Details.Contains("not null", StringComparison.OrdinalIgnoreCase))
                definition += " NOT NULL";
            definitions.Add(definition);
        }

        foreach (var constraint in constraints)
        {
            var type = ReadDetail(constraint.Details, "Type");
            var keyColumns = constraint.Children?.Select(item => QuoteIdentifier(item.Name)).ToArray()
                ?? Array.Empty<string>();
            if (keyColumns.Length == 0 || type is null)
                continue;

            var clause = type switch
            {
                "PRIMARY KEY" => $"    CONSTRAINT {QuoteIdentifier(constraint.Name)} PRIMARY KEY ({string.Join(", ", keyColumns)})",
                "UNIQUE" => $"    CONSTRAINT {QuoteIdentifier(constraint.Name)} UNIQUE ({string.Join(", ", keyColumns)})",
                "FOREIGN KEY" => BuildForeignKeyDdl(constraint, keyColumns),
                _ => null
            };
            if (clause is not null)
                definitions.Add(clause);
        }

        var ddl = $"CREATE TABLE {QuoteIdentifier(tableName)} (" + Environment.NewLine +
                  string.Join("," + Environment.NewLine, definitions) + Environment.NewLine + ");";
        return ddl + Environment.NewLine + Environment.NewLine +
               "-- Generated from the metadata currently exposed by Firebird Studio." + Environment.NewLine +
               "-- Review domain identity, defaults, computed fields, check constraints and dependencies before using this as a migration script.";
    }

    private static string? BuildForeignKeyDdl(DatabaseMetadataObject constraint, IReadOnlyList<string> localColumns)
    {
        var referencePairs = constraint.Children?
            .Select(item => FirebirdDdlPreview.ReadDetail(item.Details, "references"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value =>
            {
                var separator = value!.LastIndexOf('.');
                return separator > 0 && separator < value.Length - 1
                    ? (Table: value[..separator], Column: value[(separator + 1)..])
                    : (Table: string.Empty, Column: string.Empty);
            })
            .ToArray() ?? Array.Empty<(string Table, string Column)>();
        if (referencePairs.Length == 0 || referencePairs.Any(pair => pair.Table.Length == 0 || pair.Column.Length == 0))
            return null;

        var referencedTable = referencePairs[0].Table;
        if (referencePairs.Any(pair => !string.Equals(pair.Table, referencedTable, StringComparison.OrdinalIgnoreCase)) ||
            referencePairs.Length != localColumns.Count)
            return null;

        var referencedColumns = referencePairs.Select(pair => QuoteIdentifier(pair.Column));
        var clause = $"    CONSTRAINT {QuoteIdentifier(constraint.Name)} FOREIGN KEY ({string.Join(", ", localColumns)}) " +
                     $"REFERENCES {QuoteIdentifier(referencedTable)} ({string.Join(", ", referencedColumns)})";
        var updateRule = ReadRule(constraint.Details, "ON UPDATE");
        var deleteRule = ReadRule(constraint.Details, "ON DELETE");
        if (updateRule is not null)
            clause += $" ON UPDATE {updateRule}";
        if (deleteRule is not null)
            clause += $" ON DELETE {deleteRule}";
        return clause;
    }

    private static string? ReadRule(string details, string prefix)
    {
        var part = details.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase));
        return part is null ? null : part[(prefix.Length + 1)..].Trim();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
