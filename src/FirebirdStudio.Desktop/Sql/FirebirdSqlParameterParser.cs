using System.Text;

namespace FirebirdStudio.Desktop.Sql;

internal static class FirebirdSqlParameterParser
{
    public static IReadOnlyList<string> FindNames(string sql)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Scan(sql, (index, length, name) =>
        {
            if (seen.Add(name)) names.Add(name);
        });
        return names;
    }

    public static string NormalizePlaceholders(string sql)
    {
        var replacements = new Dictionary<int, int>();
        Scan(sql, (index, length, _) => replacements[index] = length);
        if (replacements.Count == 0) return sql;

        var result = new StringBuilder(sql.Length + replacements.Count);
        for (var index = 0; index < sql.Length; index++)
        {
            if (replacements.TryGetValue(index, out var length))
            {
                result.Append('@');
                result.Append(sql, index + 1, length - 1);
                index += length - 1;
            }
            else result.Append(sql[index]);
        }
        return result.ToString();
    }

    private static void Scan(string sql, Action<int, int, string> found)
    {
        var single = false;
        var quotedIdentifier = false;
        var lineComment = false;
        var blockComment = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';
            if (lineComment)
            {
                if (current is '\r' or '\n') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                if (current == '*' && next == '/') { blockComment = false; index++; }
                continue;
            }
            if (single)
            {
                if (current == '\'' && next == '\'') index++;
                else if (current == '\'') single = false;
                continue;
            }
            if (quotedIdentifier)
            {
                if (current == '"' && next == '"') index++;
                else if (current == '"') quotedIdentifier = false;
                continue;
            }
            if (current == '-' && next == '-') { lineComment = true; index++; continue; }
            if (current == '/' && next == '*') { blockComment = true; index++; continue; }
            if (current == '\'') { single = true; continue; }
            if (current == '"') { quotedIdentifier = true; continue; }
            if (current != ':' || next == ':' || !IsIdentifierStart(next)) continue;
            if (index > 0 && (IsIdentifierPart(sql[index - 1]) || sql[index - 1] == ':')) continue;

            var end = index + 2;
            while (end < sql.Length && IsIdentifierPart(sql[end])) end++;
            found(index, end - index, sql[(index + 1)..end]);
            index = end - 1;
        }
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$';
}
