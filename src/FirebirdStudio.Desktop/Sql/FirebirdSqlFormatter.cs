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
        var depth = 0;
        var lineHasContent = false;
        var previous = string.Empty;
        var whereClause = false;
        var setClause = false;
        var joinContinuation = false;
        var blockDepth = 0;
        var caseDepth = 0;

        void TrimEndSpaces()
        {
            while (output.Length > 0 && output[^1] == ' ')
                output.Length--;
        }

        void NewLine()
        {
            TrimEndSpaces();
            if (lineHasContent)
                output.AppendLine();
            lineHasContent = false;
            previous = string.Empty;
        }

        void Append(string value, bool space = true)
        {
            if (!lineHasContent)
            {
                output.Append(' ', Math.Max(0, indent) * 4);
                lineHasContent = true;
            }
            else if (space && output.Length > 0 && output[^1] != ' ' && output[^1] != '\n')
            {
                output.Append(' ');
            }

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
                Append(raw, false);
                NewLine();
                continue;
            }

            if (token.IsWord)
            {
                var clause = depth == 0 &&
                    (upper is "SELECT" or "FROM" or "WHERE" or "HAVING" or "RETURNING" or "UNION" ||
                     upper == "GROUP" && next == "BY" || upper == "ORDER" && next == "BY");
                var joinModifier = upper is "INNER" or "LEFT" or "RIGHT" or "FULL" or "CROSS";
                var join = depth == 0 && (upper == "JOIN" && !joinContinuation ||
                    joinModifier && (next == "JOIN" || next == "OUTER"));
                if (upper == "JOIN" && joinContinuation)
                    joinContinuation = false;

                if (clause || join || depth == 0 && upper == "SET")
                {
                    if (lineHasContent)
                        NewLine();

                    if (upper is "FROM" or "WHERE" or "HAVING" or "GROUP" or "ORDER" or "UNION" or "RETURNING")
                        indent = Math.Max(0, indent - 1);

                    if (joinModifier && (next == "JOIN" || next == "OUTER"))
                        joinContinuation = true;
                    else if (upper == "JOIN")
                        joinContinuation = false;

                    if (upper is "WHERE" or "HAVING")
                    {
                        whereClause = true;
                        setClause = false;
                    }
                    else if (upper == "SET")
                    {
                        setClause = true;
                        whereClause = false;
                    }
                    else if (upper is "SELECT" or "FROM" or "GROUP" or "ORDER" or "UNION" or "RETURNING")
                    {
                        whereClause = false;
                        setClause = false;
                    }
                }
                else if (depth == 0 && whereClause && (upper is "AND" or "OR"))
                {
                    NewLine();
                }

                if (upper == "BEGIN")
                {
                    if (lineHasContent)
                        NewLine();
                    Append("BEGIN", false);
                    blockDepth++;
                    indent++;
                    NewLine();
                    continue;
                }

                if (upper == "CASE")
                {
                    Append("CASE");
                    caseDepth++;
                    indent++;
                    continue;
                }

                if (upper == "END")
                {
                    if (lineHasContent)
                        NewLine();
                    indent = Math.Max(0, indent - 1);
                    if (caseDepth > 0)
                        caseDepth--;
                    else if (blockDepth > 0)
                        blockDepth--;
                    Append("END", false);
                    if (next is not ";" and not "," and not ")" and not "." and not "ELSE")
                        NewLine();
                    continue;
                }

                Append(Keywords.Contains(raw) ? upper : raw);
                if (upper is "SELECT" or "SET")
                    indent++;
                if (upper is "THEN" or "DO" or "ELSE")
                    NewLine();
                continue;
            }

            switch (raw)
            {
                case "(":
                    Append(raw, previous is "FROM" or "IN" or "VALUES" or "AS" or "EXISTS" or "IF" or "WHILE" or "FOR");
                    depth++;
                    break;
                case ")":
                    TrimEndSpaces();
                    Append(raw, false);
                    depth = Math.Max(0, depth - 1);
                    break;
                case ",":
                    TrimEndSpaces();
                    output.Append(',');
                    if (depth == 0 && (setClause || previous.Length > 0))
                        NewLine();
                    else
                        output.Append(' ');
                    previous = ",";
                    break;
                case ".":
                    TrimEndSpaces();
                    output.Append('.');
                    previous = ".";
                    break;
                case ":":
                    Append(":", true);
                    break;
                case ";":
                    TrimEndSpaces();
                    output.Append(';');
                    previous = ";";
                    if (depth == 0)
                    {
                        NewLine();
                        indent = blockDepth;
                        whereClause = false;
                        setClause = false;
                    }
                    break;
                default:
                    Append(raw, raw is not "::" and not "->" && previous is not "(" and not ":");
                    break;
            }
        }

        TrimEndSpaces();
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
