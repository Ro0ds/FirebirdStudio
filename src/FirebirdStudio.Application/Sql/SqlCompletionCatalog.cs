using System.Text.RegularExpressions;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Application.Sql;

public sealed record SqlCompletionSuggestion(string Text, string InsertText, string Description);

public static partial class SqlCompletionCatalog
{
    private static readonly string[] Keywords =
    [
        "SELECT", "DISTINCT", "ALL", "FIRST", "SKIP", "FROM", "WHERE", "GROUP BY", "HAVING",
        "ORDER BY", "ROWS", "OFFSET", "FETCH", "NEXT", "ONLY", "PLAN", "JOIN", "INNER JOIN",
        "LEFT JOIN", "LEFT OUTER JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "ON", "USING",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT", "AS", "WITH", "RECURSIVE", "UPDATE", "SET",
        "DELETE", "INSERT", "INTO", "VALUES", "RETURNING", "EXECUTE PROCEDURE", "EXECUTE BLOCK",
        "EXECUTE STATEMENT", "MERGE", "WHEN MATCHED", "WHEN NOT MATCHED", "THEN", "CASE", "WHEN",
        "ELSE", "END", "AND", "OR", "NOT", "NULL", "IS NULL", "IS NOT NULL", "IN", "NOT IN",
        "EXISTS", "BETWEEN", "LIKE", "CONTAINING", "STARTING WITH", "ESCAPE", "ASC", "DESC",
        "NULLS FIRST", "NULLS LAST", "FIRST ROW", "ROWS BETWEEN", "CURRENT ROW", "UNBOUNDED PRECEDING",
        "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE SAVEPOINT", "CREATE", "ALTER", "DROP", "TABLE",
        "VIEW", "INDEX", "PROCEDURE", "TRIGGER", "DOMAIN", "GENERATOR", "SEQUENCE", "EXCEPTION",
        "DATABASE", "ROLE", "USER", "GRANT", "REVOKE", "PRIMARY KEY", "FOREIGN KEY", "REFERENCES",
        "UNIQUE", "CHECK", "DEFAULT", "NOT NULL", "COLLATE", "CHARACTER SET", "CASCADE", "RESTRICT",
        "ON UPDATE", "ON DELETE", "NO ACTION", "SET NULL", "SET DEFAULT", "BEFORE", "AFTER", "ACTIVE",
        "INACTIVE", "FOR", "DO", "DECLARE", "BEGIN", "SUSPEND", "EXIT", "LEAVE", "WHILE", "LOOP",
        "IF", "THEN", "ELSEIF", "RETURN", "RETURNS", "INPUT_TYPE", "OUTPUT_TYPE", "CAST", "TYPE OF",
        "TYPE OF COLUMN", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURRENT_ROLE",
        "CURRENT_CONNECTION", "CURRENT_TRANSACTION", "RDB$DATABASE"
    ];

    private static readonly string[] Functions =
    [
        "ABS", "ACOS", "ASCII_CHAR", "ASCII_VAL", "ASIN", "ATAN", "ATAN2", "AVG", "BIN_AND", "BIN_OR",
        "BIN_SHL", "BIN_SHR", "BIN_XOR", "BIT_LENGTH", "CEIL", "CEILING", "CHAR_LENGTH", "CHARACTER_LENGTH",
        "COALESCE", "COS", "COSH", "COT", "COUNT", "DATEADD", "DATEDIFF", "DECODE", "EXP", "EXTRACT",
        "FLOOR", "GEN_ID", "HASH", "IIF", "LIST", "LN", "LOG", "LOG10", "LOWER", "LPAD", "MAX",
        "MAXVALUE", "MIN", "MINVALUE", "MOD", "NULLIF", "OCTET_LENGTH", "OVERLAY", "PI", "POSITION",
        "POWER", "RDB$GET_CONTEXT", "RDB$SET_CONTEXT", "REPLACE", "REVERSE", "ROUND", "RPAD", "SIGN",
        "SIN", "SINH", "SQRT", "SUBSTRING", "SUM", "TAN", "TANH", "TRIM", "TRUNC", "UPPER"
    ];

    private static readonly string[] Types =
    [
        "SMALLINT", "INTEGER", "BIGINT", "INT128", "NUMERIC", "DECIMAL", "FLOAT", "DOUBLE PRECISION",
        "DECFLOAT", "CHAR", "CHARACTER", "VARCHAR", "NCHAR", "NATIONAL CHARACTER", "BLOB", "DATE",
        "TIME", "TIMESTAMP", "BOOLEAN", "TIME WITH TIME ZONE", "TIMESTAMP WITH TIME ZONE"
    ];

    public static bool MayChangeSchema(string sql) =>
        Regex.IsMatch(RemoveCommentsAndStrings(sql),
            @"^\s*(CREATE|ALTER|DROP|RECREATE|COMMENT|GRANT|REVOKE)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<SqlCompletionSuggestion> Build(
        string documentText, int caretOffset, IReadOnlyList<DatabaseMetadataObject> schema)
    {
        caretOffset = Math.Clamp(caretOffset, 0, documentText.Length);
        var textBeforeCaret = documentText[..caretOffset];
        var prefixMatch = Regex.Match(textBeforeCaret, @"[\w$]*$", RegexOptions.CultureInvariant);
        var prefix = prefixMatch.Value;
        var cleanDocumentText = RemoveCommentsAndStrings(documentText);
        var cleanTextBeforeCaret = cleanDocumentText[..caretOffset];
        var cleanStatement = GetCurrentStatement(cleanDocumentText, caretOffset);
        var aliases = ReadAliases(cleanStatement, schema);

        var qualifierMatch = Regex.Match(cleanTextBeforeCaret,
            """(?<qualifier>"(?:[^"]|"")*"|[\w$]+)\s*\.\s*(?<prefix>[\w$]*)$""",
            RegexOptions.CultureInvariant);
        if (qualifierMatch.Success)
        {
            var qualifier = Unquote(qualifierMatch.Groups["qualifier"].Value);
            var relation = aliases.GetValueOrDefault(qualifier) ?? qualifier;
            var relationObject = schema.FirstOrDefault(item =>
                (item.Kind is "Table" or "View") && string.Equals(item.Name, relation, StringComparison.OrdinalIgnoreCase));
            return (relationObject?.Children ?? Array.Empty<DatabaseMetadataObject>())
                .Select(item => Suggest(item.Name, $"Column · {relation}"))
                .Where(item => Matches(item.Text, prefix))
                .OrderBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var lastWord = Regex.Match(cleanTextBeforeCaret, @"(?<word>[A-Za-z_][\w$]*)\s*$").Groups["word"].Value;
        var expectsRelation = new[] { "FROM", "JOIN", "UPDATE", "INTO", "TABLE", "REFERENCES" }
            .Contains(lastWord, StringComparer.OrdinalIgnoreCase) ||
            Regex.IsMatch(cleanTextBeforeCaret, @"\b(?:FROM|JOIN|UPDATE|INTO|REFERENCES)\s+[\w$]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var expectsType = IsTypeContext(cleanTextBeforeCaret);

        IEnumerable<SqlCompletionSuggestion> candidates;
        if (expectsRelation)
        {
            var statementVerb = Regex.Match(cleanStatement, @"^\s*(?<verb>[A-Za-z_]+)", RegexOptions.CultureInvariant)
                .Groups["verb"].Value;
            var relationContext = Regex.Match(cleanTextBeforeCaret,
                @"\b(?<keyword>FROM|JOIN|UPDATE|INTO|REFERENCES)\s+[\w$]*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["keyword"].Value;
            var allowSelectableProcedures = statementVerb.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
                (relationContext.Equals("FROM", StringComparison.OrdinalIgnoreCase) ||
                 relationContext.Equals("JOIN", StringComparison.OrdinalIgnoreCase));
            candidates = schema.Where(item => (item.Kind is "Table" or "View") ||
                    (allowSelectableProcedures && item.Kind == "Procedure"))
                .Select(item => Suggest(item.Name, item.Kind));
        }
        else if (expectsType)
        {
            candidates = Types.Select(item => Suggest(item, "Firebird data type"))
                .Concat(schema.Where(item => item.Kind == "Domain").Select(item => Suggest(item.Name, "Database domain")));
        }
        else
        {
            candidates = Keywords.Select(item => Suggest(item, "Firebird SQL keyword"))
                .Concat(Functions.Select(item => Suggest(item, "Firebird SQL function")))
                .Concat(Types.Select(item => Suggest(item, "Firebird data type")))
                .Concat(schema.Where(item => item.Kind is "Table" or "View" or "Procedure" or "Sequence" or "Exception" or "Index" or "Trigger" or "Function" or "Character set" or "Collation" or "Role")
                    .Select(item => Suggest(item.Name, item.Kind)));

            foreach (var relation in aliases.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relationObject = schema.FirstOrDefault(item =>
                    (item.Kind is "Table" or "View") && string.Equals(item.Name, relation, StringComparison.OrdinalIgnoreCase));
                if (relationObject?.Children is not null)
                    candidates = candidates.Concat(relationObject.Children.Select(item => Suggest(item.Name, $"Column · {relation}")));
            }
        }

        return candidates.Where(item => Matches(item.Text, prefix))
            .GroupBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool NeedsDomainSuggestions(string documentText, int caretOffset)
    {
        caretOffset = Math.Clamp(caretOffset, 0, documentText.Length);
        var cleanDocumentText = RemoveCommentsAndStrings(documentText);
        var cleanBeforeCaret = cleanDocumentText[..caretOffset];
        return IsTypeContext(GetCurrentStatement(cleanBeforeCaret, cleanBeforeCaret.Length));
    }

    public static string GetPrefix(string documentText, int caretOffset)
    {
        caretOffset = Math.Clamp(caretOffset, 0, documentText.Length);
        return Regex.Match(documentText[..caretOffset], @"[\w$]*$", RegexOptions.CultureInvariant).Value;
    }

    private static bool IsTypeContext(string cleanTextBeforeCaret)
    {
        var lastWord = Regex.Match(cleanTextBeforeCaret, @"(?<word>[A-Za-z_][\w$]*)\s*$").Groups["word"].Value;
        return new[] { "AS", "RETURNS", "TYPE" }.Contains(lastWord, StringComparer.OrdinalIgnoreCase) ||
            Regex.IsMatch(cleanTextBeforeCaret, @"\b(?:AS|RETURNS|TYPE)\s+[\w$]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(cleanTextBeforeCaret, @"\bCAST\s*\([^)]*\bAS\s+[\w$]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(cleanTextBeforeCaret,
                """\bCREATE\s+(?:GLOBAL\s+TEMPORARY\s+)?TABLE\b[\s\S]*\(\s*"?[\w$]+"?\s+[\w$]*$""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(cleanTextBeforeCaret,
                """\bALTER\s+TABLE\b[\s\S]*\bADD\s+(?:COLUMN\s+)?"?[\w$]+"?\s+[\w$]*$""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(cleanTextBeforeCaret, @"\bDECLARE\s+VARIABLE\s+[\w$]+\s+[\w$]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Dictionary<string, string> ReadAliases(string sql, IReadOnlyList<DatabaseMetadataObject> schema)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relationPattern = RelationAliasRegex();
        foreach (Match match in relationPattern.Matches(sql))
        {
            var relationName = Unquote(match.Groups["relation"].Value);
            if (!schema.Any(item => (item.Kind is "Table" or "View") &&
                string.Equals(item.Name, relationName, StringComparison.OrdinalIgnoreCase)))
                continue;

            aliases[relationName] = relationName;
            var alias = match.Groups["alias"].Success ? Unquote(match.Groups["alias"].Value) : relationName;
            if (!IsSqlKeyword(alias)) aliases[alias] = relationName;
        }
        return aliases;
    }

    private static string GetCurrentStatement(string text, int caretOffset)
    {
        var start = caretOffset == 0 ? -1 : text.LastIndexOf(';', caretOffset - 1);
        var end = text.IndexOf(';', caretOffset);
        if (start < 0) start = 0; else start++;
        if (end < 0) end = text.Length;
        return text[start..end];
    }

    private static string RemoveCommentsAndStrings(string sql)
    {
        static string Mask(Match match) => new(' ', match.Length);
        sql = Regex.Replace(sql, @"/\*.*?\*/|--[^\r\n]*", Mask,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return Regex.Replace(sql, @"'(?:''|[^'])*'", Mask, RegexOptions.CultureInvariant);
    }

    private static bool IsSqlKeyword(string value) => Keywords.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static bool Matches(string candidate, string prefix) =>
        prefix.Length == 0 || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
        : value;
    private static SqlCompletionSuggestion Suggest(string name, string description) =>
        new(name, NeedsQuoting(name) ? "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : name, description);
    private static bool NeedsQuoting(string value) => !Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant) || IsSqlKeyword(value);

    [GeneratedRegex("""\b(?:FROM|JOIN|UPDATE|INTO|REFERENCES)\s+(?<relation>"(?:[^"]|"")*"|[A-Za-z_][\w$]*)(?:\s+(?:AS\s+)?(?<alias>"(?:[^"]|"")*"|[A-Za-z_][\w$]*))?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelationAliasRegex();
}
