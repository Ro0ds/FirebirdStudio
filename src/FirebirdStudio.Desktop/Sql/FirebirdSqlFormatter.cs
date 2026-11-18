using System.Text;

namespace FirebirdStudio.Desktop.Sql;

internal static class FirebirdSqlFormatter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALL", "ALTER", "AND", "AS", "ASC", "BEGIN", "BETWEEN", "BY", "CASE", "CAST", "COMMIT",
        "CONSTRAINT", "CREATE", "CROSS", "DELETE", "DESC", "DISTINCT", "DO", "DROP", "ELSE",
        "END", "EXCEPTION", "EXECUTE", "EXISTS", "FETCH", "FOR", "FOREIGN", "FROM", "FULL",
        "FUNCTION", "GROUP", "HAVING", "IF", "IN", "INNER", "INSERT", "INTO", "IS", "JOIN",
        "LEFT", "LIKE", "NOT", "NULL", "ON", "OR", "ORDER", "OUTER", "PRIMARY", "PROCEDURE",
        "RECREATE", "REFERENCES", "RETURNING", "RETURNS", "RIGHT", "ROLLBACK", "SELECT", "SET",
        "SUSPEND", "TABLE", "THEN", "TRANSACTION", "TRIGGER", "UNION", "UPDATE", "VALUES",
        "VIEW", "WHEN", "WHERE", "WHILE", "WITH"
    };

    private readonly record struct Token(string Text, bool IsWord, bool IsComment = false);

    public static string Format(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source ?? string.Empty;

        var tokens = Tokenize(source);
        var output = new StringBuilder(source.Length + 64);
        var indent = 0;
        var parenthesisDepth = 0;
        var lineHasContent = false;
        var previous = string.Empty;
        var inWhereClause = false;
        var inSetClause = false;

        void TrimLineEnd()
        {
            while (output.Length > 0 && output[^1] == ' ')
                output.Length--;
        }

        void NewLine()
        {
            TrimLineEnd();
            if (lineHasContent)
                output.AppendLine();
            lineHasContent = false;
            previous = string.Empty;
        }

        void EnsureIndent()
        {
            if (lineHasContent)
                return;
            output.Append(' ', Math.Max(0, indent) * 4);
            lineHasContent = true;
        }

        void AppendToken(string value, bool needsSpace = true)
        {
            EnsureIndent();
            if (needsSpace && output.Length > 0 && output[^1] != ' ' && output[^1] != '\n')
                output.Append(' ');
            output.Append(value);
            previous = value;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var raw = token.Text;
            var upper = token.IsWord ? raw.ToUpperInvariant() : raw;
            var next = i + 1 < tokens.Count ? tokens[i + 1].Text.ToUpperInvariant() : string.Empty;

            if (token.IsComment)
            {
                if (lineHasContent)
                    NewLine();
                AppendToken(raw, false);
                NewLine();
                continue;
            }

            if (token.IsWord)
            {
                var isClauseStart = parenthesisDepth == 0 &&
                    (upper is "SELECT" or "FROM" or "WHERE" or "HAVING" or "RETURNING" or "UNION" ||
                     upper == "GROUP" && next == "BY" || upper == "ORDER" && next == "BY");

                var isJoin = parenthesisDepth == 0 &&
                    (upper == "JOIN" || upper is "INNER" or "LEFT" or "RIGHT" or "FULL" or "CROSS" && next is "JOIN" or "OUTER");

                if (isClauseStart || isJoin || parenthesisDepth == 0 && upper == "SET" && previous != "UPDATE")
                {
                    if (lineHasContent)
                        NewLine();
                    if (upper == "WHERE" || upper == "HAVING")
                    {
                        inWhereClause = true;
                        inSetClause = false;
                    }
                    else if (upper == "SET")
                    {
                        inSetClause = true;
                        inWhereClause = false;
                    }
                    else if (upper is "SELECT" or "FROM" or "GROUP" or "ORDER" or "UNION" or "RETURNING")
                    {
                        inWhereClause = false;
                        if (upper != "RETURNING")
                            inSetClause = false;
                    }
                }
                else if (parenthesisDepth == 0 && inWhereClause && upper is "AND" or "OR")
                {
                    NewLine();
                }

                if (upper == "BEGIN")
                {
                    if (lineHasContent)
                        NewLine();
                    AppendToken("BEGIN", false);
                    indent++;
                    NewLine();
                    continue;
                }

                if (upper == "END")
                {
                    if (lineHasContent)
                        NewLine();
                    indent = Math.Max(0, indent - 1);
                    AppendToken("END", false);
                    if (next is not ";" and not "," and not ")" and not "." and not "ELSE")
                        NewLine();
                    continue;
                }

                var formattedWord = Keywords.Contains(raw) ? upper : raw;
                AppendToken(formattedWord);
                if (upper is "THEN" or "DO" or "ELSE")
                    NewLine();
                continue;
            }

            switch (raw)
            {
                case "(":
                    AppendToken(raw, previous is not "" and not ".");
                    parenthesisDepth++;
                    break;
                case ")":
                    TrimLineEnd();
                    AppendToken(raw, false);
                    parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
                    break;
                case ",":
                    TrimLineEnd();
                    output.Append(',');
                    if (parenthesisDepth == 0 && (inSetClause || previous.Length > 0))
                        NewLine();
                    else
                        output.Append(' ');
                    previous = ",";
                    break;
                case ".":
                    TrimLineEnd();
                    output.Append('.');
                    previous = ".";
                    break;
                case ";":
                    TrimLineEnd();
                    output.Append(';');
                    previous = ";";
                    if (parenthesisDepth == 0)
                        NewLine();
                    break;
                default:
                    AppendToken(raw, raw is not "::" and not "->");
                    break;
            }
        }

        TrimLineEnd();
        var result = output.ToString().TrimEnd();
        return result.Length == 0 ? result : result + Environment.NewLine;
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                var start = i;
                i += 2;
                while (i < source.Length && source[i] is not '\r' and not '\n')
                    i++;
                tokens.Add(new Token(source[start..i], false, true));
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }
                    i++;
                }
                tokens.Add(new Token(source[start..i], false, true));
                continue;
            }

            if (c is '\'' or '"')
            {
                var quote = c;
                var start = i++;
                while (i < source.Length)
                {
                    if (source[i++] != quote)
                        continue;
                    if (i < source.Length && source[i] == quote)
                    {
                        i++;
                        continue;
                    }
                    break;
                }
                tokens.Add(new Token(source[start..i], false));
                continue;
            }

            if (char.IsLetterOrDigit(c) || c is '_' or '$' or '#')
            {
                var start = i++;
                while (i < source.Length &&
                       (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '$' or '#'))
                    i++;
                tokens.Add(new Token(source[start..i], true));
                continue;
            }

            if (i + 1 < source.Length && source.Substring(i, 2) is "<=" or ">=" or "<>" or "!=" or "||" or "::" or "->")
            {
                tokens.Add(new Token(source.Substring(i, 2), false));
                i += 2;
                continue;
            }

            tokens.Add(new Token(c.ToString(), false));
            i++;
        }

        return tokens;
    }
}
