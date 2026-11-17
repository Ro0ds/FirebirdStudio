using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace FirebirdStudio.Desktop.Sql;

/// <summary>
/// Lightweight Firebird SQL syntax coloring for reserved words and relation/column aliases.
/// It deliberately avoids adding another editor or grammar dependency.
/// </summary>
internal sealed class FirebirdSqlSyntaxColorizer(Func<bool> isDarkTheme) : DocumentColorizingTransformer
{
    private static readonly string[] ReservedWords =
    [
        "ACTIVE", "ADD", "AFTER", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AT", "BEFORE", "BEGIN",
        "BETWEEN", "BIGINT", "BLOB", "BOOLEAN", "BY", "CASE", "CAST", "CHAR", "CHARACTER", "DECFLOAT",
        "CHECK", "CLOSE", "COLLATE", "COMMIT", "CONNECT", "CONSTRAINT", "CREATE",
        "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
        "CURRENT_TRANSACTION", "CURRENT_USER", "CURSOR", "DATABASE", "DATE", "DAY", "DECIMAL",
        "DECLARE", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DO", "DOUBLE", "DROP", "ELSE", "INT128",
        "END", "ENTRY_POINT", "ESCAPE", "EXECUTE", "EXISTS", "EXCEPTION", "EXTERNAL", "FETCH",
        "FILTER", "FIRST", "FLOAT", "FOR", "FOREIGN", "FROM", "FULL", "FUNCTION", "GENERATOR", "GRANT",
        "GROUP", "HAVING", "IF", "IN", "INDEX", "INNER", "INSERT", "INT", "INTEGER", "INTO", "IS",
        "JOIN", "KEY", "LAST", "LEAVE", "LEFT", "LIKE", "LONG", "LOOP", "MATCHED", "MERGE", "MINUTE",
        "MONTH", "NATIONAL", "NATURAL", "NCHAR", "NO", "NOT", "NULL", "NUMERIC", "OF", "ON",
        "ONLY", "OPEN", "OR", "ORDER", "OUTER", "OUTPUT_TYPE", "PLAN", "POST_EVENT",
        "PRECISION", "PRIMARY", "PROCEDURE", "RDB$DATABASE", "REAL", "RECORD_VERSION", "RECREATE",
        "REFERENCES", "RELEASE", "RETURN", "RETURNING", "RETURNS", "REVOKE", "RIGHT", "ROLLBACK",
        "RESTRICT", "ROLE", "ROW", "ROWS", "SAVEPOINT", "SECOND", "SELECT", "SEQUENCE", "SET", "SMALLINT", "SOME", "SQLCODE",
        "SQLSTATE", "START", "STARTING", "SUSPEND", "TABLE", "THEN", "TIME", "TIMESTAMP", "TO",
        "TRIGGER", "UNION", "UNIQUE", "UPDATE", "USER", "USING", "VALUE", "VALUES", "VARCHAR",
        "TYPE", "VARIABLE", "VIEW", "WHEN", "WHERE", "WHILE", "WITH", "YEAR"
    ];

    private static readonly HashSet<string> ReservedSet = new(ReservedWords, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex KeywordRegex = new(
        @"\b(?:" + string.Join("|", ReservedWords.OrderByDescending(word => word.Length).Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MaskRegex = new(
        @"/\*.*?\*/|--[^\r\n]*|'(?:''|[^'])*'",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QuotedIdentifierRegex = new(
        @"""(?:""""|[^""])*""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly IBrush DarkKeywordBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush LightKeywordBrush = new SolidColorBrush(Color.Parse("#000080"));
    private static readonly IBrush DarkAliasBrush = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush LightAliasBrush = new SolidColorBrush(Color.Parse("#006B55"));
    private static readonly IBrush DarkObjectBrush = new SolidColorBrush(Color.Parse("#D7BA7D"));
    private static readonly IBrush LightObjectBrush = new SolidColorBrush(Color.Parse("#795E26"));
    private static readonly Regex AliasAfterAsRegex = new(
        """\bAS\s+(?<alias>"(?:[^"]|"")*"|[A-Za-z_][\w$]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RelationRegex = new(
        """\b(?:FROM|JOIN|UPDATE|INTO)\s+(?<table>"(?:""|[^"])*"|[A-Za-z_][\w$]*)(?:\s+(?:AS\s+)?(?<alias>"(?:""|[^"])*"|[A-Za-z_][\w$]*))?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AliasUsageRegex = new(
        """(?<alias>"(?:""|[^"])*"|[A-Za-z_][\w$]*)\s*\.""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QualifiedFieldRegex = new(
        """\.\s*(?<field>"(?:""|[^"])*"|[A-Za-z_][\w$]*)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ClauseFieldRegex = new(
        """\b(?:SELECT|WHERE|AND|OR|BY|SET|ON|HAVING|RETURNING)\s+(?<field>"(?:""|[^"])*"|[A-Za-z_][\w$]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private string? _cachedDocumentText;
    private string? _maskedDocumentText;
    private string? _keywordDocumentText;
    private HashSet<(int Start, int Length)> _aliasRanges = [];
    private HashSet<string> _aliasNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<(int Start, int Length)> _objectRanges = [];

    protected override void ColorizeLine(DocumentLine line)
    {
        var documentText = CurrentContext.Document.Text;
        if (!string.Equals(documentText, _cachedDocumentText, StringComparison.Ordinal))
            RefreshCache(documentText);

        var lineText = _keywordDocumentText!.Substring(line.Offset, line.Length);
        var keywordBrush = isDarkTheme() ? DarkKeywordBrush : LightKeywordBrush;
        var aliasBrush = isDarkTheme() ? DarkAliasBrush : LightAliasBrush;
        var objectBrush = isDarkTheme() ? DarkObjectBrush : LightObjectBrush;

        foreach (Match match in KeywordRegex.Matches(lineText))
        {
            var start = line.Offset + match.Index;
            var end = start + match.Length;
            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(keywordBrush);
                var typeface = element.TextRunProperties.Typeface;
                element.TextRunProperties.SetTypeface(new Typeface(
                    typeface.FontFamily, typeface.Style, FontWeight.Bold, typeface.Stretch));
            });
        }

        foreach (var objectRange in _objectRanges)
        {
            if (objectRange.Start < line.Offset || objectRange.Start + objectRange.Length > line.Offset + line.Length)
                continue;

            ChangeLinePart(objectRange.Start, objectRange.Start + objectRange.Length,
                element => element.TextRunProperties.SetForegroundBrush(objectBrush));
        }

        foreach (var alias in _aliasRanges)
        {
            if (alias.Start < line.Offset || alias.Start + alias.Length > line.Offset + line.Length)
                continue;

            ChangeLinePart(alias.Start, alias.Start + alias.Length,
                element => element.TextRunProperties.SetForegroundBrush(aliasBrush));
        }
    }

    private void RefreshCache(string documentText)
    {
        _cachedDocumentText = documentText;
        _maskedDocumentText = MaskRegex.Replace(documentText, match => new string(' ', match.Length));
        _keywordDocumentText = QuotedIdentifierRegex.Replace(
            _maskedDocumentText, match => new string(' ', match.Length));
        _aliasRanges = [];
        _aliasNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _objectRanges = [];

        AddAliases(AliasAfterAsRegex.Matches(_maskedDocumentText));
        foreach (Match relation in RelationRegex.Matches(_maskedDocumentText))
        {
            AddObjectRange(relation.Groups["table"]);
            AddAlias(relation.Groups["alias"]);
        }

        foreach (Match usage in AliasUsageRegex.Matches(_maskedDocumentText))
        {
            var alias = usage.Groups["alias"];
            if (_aliasNames.Contains(UnquoteIdentifier(alias.Value)))
                _aliasRanges.Add((alias.Index, alias.Length));
        }

        foreach (Match field in QualifiedFieldRegex.Matches(_maskedDocumentText))
            AddObjectRange(field.Groups["field"]);
        foreach (Match field in ClauseFieldRegex.Matches(_maskedDocumentText))
            AddObjectRange(field.Groups["field"]);
    }

    private void AddAliases(MatchCollection matches)
    {
        foreach (Match match in matches)
            AddAlias(match.Groups["alias"]);
    }

    private void AddAlias(Group group)
    {
        if (!group.Success || IsInsideCastExpression(group.Index))
            return;

        var alias = UnquoteIdentifier(group.Value);
        if (ReservedSet.Contains(alias))
            return;

        _aliasNames.Add(alias);
        _aliasRanges.Add((group.Index, group.Length));
    }

    private void AddObjectRange(Group group)
    {
        if (!group.Success || ReservedSet.Contains(UnquoteIdentifier(group.Value)))
            return;

        _objectRanges.Add((group.Index, group.Length));
    }

    private static string UnquoteIdentifier(string identifier) =>
        identifier.Length >= 2 && identifier[0] == '"' && identifier[^1] == '"'
            ? identifier[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : identifier;

    private bool IsInsideCastExpression(int offset)
    {
        var openParentheses = new Stack<int>();
        for (var index = 0; index < offset; index++)
        {
            if (_maskedDocumentText![index] == '(')
                openParentheses.Push(index);
            else if (_maskedDocumentText[index] == ')' && openParentheses.Count > 0)
                openParentheses.Pop();
        }

        return openParentheses.Any(openOffset =>
            Regex.IsMatch(_maskedDocumentText![..openOffset], @"\bCAST\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
