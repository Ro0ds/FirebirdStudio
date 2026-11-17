using System.Text;

namespace FirebirdStudio.Desktop.Sql;

internal static class FirebirdSqlFormatter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AS", "BEGIN", "END", "IF", "THEN", "ELSE", "FOR", "WHILE", "DO", "SUSPEND", "EXIT",
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND", "OR",
        "NOT", "NULL", "IS", "IN", "EXISTS", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "RETURNING", "ORDER", "BY", "GROUP", "HAVING", "UNION", "ALL", "DISTINCT", "CASE", "WHEN",
        "CREATE", "ALTER", "DROP", "RECREATE", "PROCEDURE", "TRIGGER", "TABLE", "VIEW", "DOMAIN",
        "RETURNS", "DECLARE", "VARIABLE", "EXCEPTION", "POST_EVENT", "EXECUTE", "STATEMENT", "PROCEDURE",
        "COMMIT", "ROLLBACK", "START", "TRANSACTION", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT"
    };

    public static string Format(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return source ?? string.Empty;
        var output = new StringBuilder(source.Length + 64);
        var indent = 0;
        var pendingSpace = false;
        var single = false;
        var quoted = false;
        var lineComment = false;
        var blockComment = false;

        void NewLine()
        {
            while (output.Length > 0 && output[^1] == ' ') output.Length--;
            if (output.Length > 0 && output[^1] != '\n') output.AppendLine();
            output.Append(' ', Math.Max(0, indent) * 4);
            pendingSpace = false;
        }
        void Space()
        {
            if (output.Length > 0 && output[^1] != ' ' && output[^1] != '\n') output.Append(' ');
        }

        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            var n = i + 1 < source.Length ? source[i + 1] : '\0';
            if (lineComment)
            {
                output.Append(c); i++;
                if (c is '\r' or '\n') { lineComment = false; NewLine(); }
                continue;
            }
            if (blockComment)
            {
                output.Append(c); i++;
                if (c == '*' && n == '/') { output.Append('/'); i++; blockComment = false; }
                continue;
            }
            if (single || quoted)
            {
                output.Append(c); i++;
                if (single && c == '\'' && n == '\'') { output.Append(n); i++; }
                else if (quoted && c == '"' && n == '"') { output.Append(n); i++; }
                else if (single && c == '\'') single = false;
                else if (quoted && c == '"') quoted = false;
                continue;
            }
            if (c == '-' && n == '-') { Space(); output.Append("--"); i += 2; lineComment = true; continue; }
            if (c == '/' && n == '*') { Space(); output.Append("/*"); i += 2; blockComment = true; continue; }
            if (c == '\'') { if (pendingSpace) Space(); output.Append(c); i++; single = true; pendingSpace = false; continue; }
            if (c == '"') { if (pendingSpace) Space(); output.Append(c); i++; quoted = true; pendingSpace = false; continue; }
            if (char.IsWhiteSpace(c)) { pendingSpace = true; i++; continue; }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '$' or '#')) i++;
                var word = source[start..i];
                var upper = Keywords.Contains(word) ? word.ToUpperInvariant() : word;
                if (upper is "END" or "ELSE" or "WHEN")
                {
                    if (upper == "END") indent = Math.Max(0, indent - 1);
                    NewLine();
                    output.Append(upper);
                    pendingSpace = true;
                }
                else
                {
                    if (pendingSpace) Space();
                    if (upper == "BEGIN" && output.Length > 0 && output[^1] != '\n') NewLine();
                    output.Append(upper);
                    pendingSpace = true;
                    if (upper == "BEGIN")
                    {
                        indent++;
                        NewLine();
                    }
                    else if (upper == "CASE")
                    {
                        indent++;
                    }
                    if (upper is "THEN" or "DO") NewLine();
                }
                continue;
            }

            if (c == ';')
            {
                while (output.Length > 0 && output[^1] == ' ') output.Length--;
                output.Append(';'); i++; NewLine(); continue;
            }
            if (c == ',')
            {
                while (output.Length > 0 && output[^1] == ' ') output.Length--;
                output.Append(", "); i++; pendingSpace = false; continue;
            }
            if (c == '(') { if (pendingSpace) Space(); output.Append('('); i++; pendingSpace = false; continue; }
            if (c == ')') { while (output.Length > 0 && output[^1] == ' ') output.Length--; output.Append(')'); i++; pendingSpace = true; continue; }
            if (c == '.') { output.Append('.'); i++; pendingSpace = false; continue; }
            if (pendingSpace) Space();
            output.Append(c); i++; pendingSpace = false;
        }

        var formatted = output.ToString().Trim();
        return formatted.Length == 0 ? formatted : formatted + Environment.NewLine;
    }
}
