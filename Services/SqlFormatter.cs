using System.Text;

namespace SchemaCompare.Services;

/// <summary>
/// Deterministic T-SQL beautifier. Tokenizes first, so keywords inside string
/// literals, bracketed identifiers and comments are never touched, and no text
/// is ever added, removed or reordered — only case, whitespace and line breaks.
/// Deliberately conservative: it will not rewrite a query it does not understand.
/// </summary>
public static class SqlFormatter
{
    private const int Step = 2;

    /// <summary>Words that start their own line at the current block indent.</summary>
    private static readonly HashSet<string> LineStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT",
        "INTERSECT", "INSERT", "UPDATE", "DELETE", "SET", "VALUES", "MERGE", "USING",
        "DECLARE", "IF", "ELSE", "WHILE", "RETURN", "PRINT", "USE", "EXEC", "EXECUTE",
        "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE", "BEGIN", "END",
        "COMMIT", "ROLLBACK", "WITH", "OPTION", "GO", "AND", "OR", "CROSS", "INNER",
        "LEFT", "RIGHT", "FULL", "JOIN", "APPLY", "EXISTS", "CASE"
    };

    /// <summary>Words upper-cased when they are T-SQL keywords, not identifiers.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADD", "ALL", "ALTER", "AND", "ANY", "APPLY", "AS", "ASC", "AUTHORIZATION",
        "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY", "CASCADE", "CASE",
        "CHECKPOINT", "CHECK", "CLOSE", "CLUSTERED", "COALESCE", "COLLATE", "COLUMN",
        "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTAINSTABLE", "CONTINUE",
        "CONVERT", "CREATE", "CROSS", "CURRENT", "CURSOR", "DATABASE", "DBCC",
        "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK",
        "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL",
        "ESCAPE", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH",
        "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM",
        "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY",
        "IDENTITYCOL", "IDENTITY_INSERT", "IF", "IN", "INDEX", "INNER", "INSERT",
        "INTERSECT", "INTO", "IS", "ISNULL", "JOIN", "KEY", "KILL", "LEFT", "LIKE",
        "LINENO", "LOAD", "MERGE", "NATIONAL", "NOCHECK", "NOCOUNT", "NONCLUSTERED", "NOT",
        "NULLIF", "NULL", "OF", "OFF", "OFFSETS", "ON", "OPEN", "OPENDATASOURCE",
        "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OR", "ORDER", "OUTER",
        "OVER", "PERCENT", "PIVOT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC",
        "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE",
        "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT",
        "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE",
        "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE", "SESSION",
        "SET", "SETS", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TABLE",
        "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP", "TRAN", "TRANSACTION",
        "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL", "TRY", "CATCH", "UNION", "UNIQUE",
        "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER", "VALUES", "VARYING",
        "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WITHIN", "WRITETEXT",
        "XACT_ABORT"
    };

    /// <summary>Reserved words that must stay upper-case even inside a multi-word clause.</summary>
    private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "STDEV", "VAR", "COUNT_BIG"
    };

    public static string Format(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql ?? "";
        var tokens = Tokenize(sql);
        var sb = new StringBuilder();
        var indent = 0;          // block indent (BEGIN…END nesting)
        var parenDepth = 0;
        var atLineStart = true;
        var statementSeen = false;
        var afterLeadingSemi = false;

        void NewLine(int extra = 0)
        {
            if (sb.Length == 0) return;
            var spaces = (indent + extra) * Step;
            if (atLineStart)
            {
                // The source already broke this line. Its inherited indentation is
                // replaced rather than appended to, so a second pass lands the same way.
                var cut = sb.Length;
                while (cut > 0 && sb[cut - 1] == ' ') cut--;
                sb.Length = cut;
                sb.Append(' ', spaces);
                return;
            }
            sb.Append('\n');
            sb.Append(' ', spaces);
            atLineStart = true;
        }

        void Write(string text)
        {
            sb.Append(text);
            atLineStart = false;
            statementSeen = true;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var (kind, text) = tokens[i];
            var glueNext = afterLeadingSemi;
            if (kind != TokKind.Space) afterLeadingSemi = false;
            switch (kind)
            {
                case TokKind.Space:
                    if (!atLineStart) sb.Append(' ');
                    break;

                case TokKind.Break:
                    if (!atLineStart)
                    {
                        sb.Append('\n');
                        sb.Append(' ', (indent + parenDepth) * Step);
                        atLineStart = true;
                    }
                    break;

                case TokKind.Comment:
                    NewLine(parenDepth);
                    Write(text);
                    break;

                case TokKind.Word:
                    var upper = Normalize(text, i, tokens);
                    if (LineStarters.Contains(upper) && !IsPartOfName(tokens, i))
                    {
                        var beginsBatch = upper.Equals("GO", StringComparison.OrdinalIgnoreCase)
                                          && AloneOnLine(tokens, i);
                        // "LEFT JOIN" is one clause: only its first word starts a line.
                        var continuesClause = IsJoinWord(upper)
                                              && PreviousWord(tokens, i) is { } before
                                              && IsJoinWord(before);
                        if (upper.Equals("END", StringComparison.OrdinalIgnoreCase) && indent > 0) indent--;
                        if (!continuesClause && !glueNext && (!atLineStart || beginsBatch || statementSeen))
                            NewLine(upper is "AND" or "OR" ? parenDepth + 1 : parenDepth);
                        if (beginsBatch)
                        {
                            indent = 0;
                            // GO is not a T-SQL keyword, so it is absent from Keywords:
                            // only the standalone batch separator gets the canonical casing.
                            upper = "GO";
                        }
                        Write(upper);
                        if (OpensBlock(upper)) indent++;
                        break;
                    }
                    Write(upper);
                    break;

                case TokKind.Punct:
                    if (text == "(")
                    {
                        Write(text);
                        parenDepth++;
                        break;
                    }
                    if (text == ")")
                    {
                        parenDepth = Math.Max(0, parenDepth - 1);
                        Write(text);
                        break;
                    }
                    if (text == ",")
                    {
                        Write(text);
                        if (parenDepth == 0) NewLine(1);
                        break;
                    }
                    if (text == ";")
                    {
                        var openedLine = atLineStart;
                        Write(text);
                        // A leading ';' is the "previous statement ended" idiom in
                        // ";WITH x AS (...)": keep it glued to what follows.
                        afterLeadingSemi = openedLine;
                        // No point breaking: the next token starts its own line anyway.
                        if (!StartsNextLine(tokens, i)) NewLine(0);
                        break;
                    }
                    Write(text);
                    break;

                default:
                    Write(text);
                    break;
            }
        }

        return CleanTrailingWhitespace(sb.ToString()).Trim() + "\n";
    }

    /// <summary>Keywords upper-case, identifiers untouched, aggregates per the
    /// documented casing. A word directly after a '.' is a name, not a keyword.</summary>
    private static string Normalize(string word, int index, List<Tok> tokens)
    {
        if (index > 0 && tokens[index - 1].Kind == TokKind.Punct && tokens[index - 1].Text == ".")
            return word;
        if (Keywords.Contains(word) || Aggregates.Contains(word)) return word.ToUpperInvariant();
        return word;
    }

    /// <summary>True for a word that is really part of a dotted name or a quoted
    /// object, so "FROM" inside "dbo.FROM" is not treated as a clause.</summary>
    private static bool IsPartOfName(List<Tok> tokens, int index) =>
        index > 0 && tokens[index - 1].Kind == TokKind.Punct && tokens[index - 1].Text == ".";

    /// <summary>BEGIN (any flavour) and CASE open an indented block; END closes one.</summary>
    private static bool OpensBlock(string word) =>
        word.Equals("CASE", StringComparison.OrdinalIgnoreCase)
        || word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> JoinWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CROSS", "INNER", "LEFT", "RIGHT", "FULL", "JOIN", "OUTER", "APPLY", "ON"
    };

    private static bool IsJoinWord(string word) => JoinWords.Contains(word);

    /// <summary>True when the token after this one already forces a new line.</summary>
    private static bool StartsNextLine(List<Tok> tokens, int index)
    {
        for (var j = index + 1; j < tokens.Count; j++)
        {
            var t = tokens[j];
            if (t.Kind == TokKind.Space) continue;
            return t.Kind == TokKind.Break
                   || (t.Kind == TokKind.Word && LineStarters.Contains(t.Text) && !IsPartOfName(tokens, j));
        }
        return true; // end of script
    }

    /// <summary>The word immediately before this token, skipping whitespace.</summary>
    private static string? PreviousWord(List<Tok> tokens, int index)
    {
        for (var j = index - 1; j >= 0; j--)
        {
            if (tokens[j].Kind == TokKind.Space) continue;
            return tokens[j].Kind == TokKind.Word ? tokens[j].Text : null;
        }
        return null;
    }

    private static bool AloneOnLine(List<Tok> tokens, int index)
    {
        var back = index - 1;
        while (back >= 0 && tokens[back].Kind == TokKind.Space) back--;
        if (back >= 0 && tokens[back].Kind != TokKind.Break && tokens[back].Kind != TokKind.Comment)
            return false;
        for (var j = index + 1; j < tokens.Count; j++)
        {
            if (tokens[j].Kind == TokKind.Space) continue;
            return tokens[j].Kind is TokKind.Break or TokKind.Comment;
        }
        return true;
    }

    private static string CleanTrailingWhitespace(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        return string.Join("\n", lines);
    }

    private enum TokKind { Word, String, Bracket, Number, Punct, Space, Comment, Break }

    private readonly record struct Tok(TokKind Kind, string Text);

    /// <summary>Split into tokens, keeping literals and comments verbatim and
    /// turning each source line end into a single Break token.</summary>
    private static List<Tok> Tokenize(string sql)
    {
        var tokens = new List<Tok>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c == '\r' || c == '\n')
            {
                while (i < sql.Length && (sql[i] == '\r' || sql[i] == '\n')) i++;
                tokens.Add(new Tok(TokKind.Break, "\n"));
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                while (i < sql.Length && (sql[i] == ' ' || sql[i] == '\t')) i++;
                tokens.Add(new Tok(TokKind.Space, " "));
                continue;
            }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                tokens.Add(new Tok(TokKind.Comment, sql[i..end].TrimEnd()));
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? sql.Length : close + 2;
                tokens.Add(new Tok(TokKind.Comment, sql[i..end]));
                i = end;
                continue;
            }
            if (c == '\'' || c == '"')
            {
                tokens.Add(new Tok(TokKind.String, ReadQuoted(sql, ref i, c)));
                continue;
            }
            if (c == 'N' && i + 1 < sql.Length && sql[i + 1] == '\'')
            {
                i++;
                var literal = "N" + ReadQuoted(sql, ref i, '\'');
                tokens.Add(new Tok(TokKind.String, literal));
                continue;
            }
            if (c == '[')
            {
                tokens.Add(new Tok(TokKind.Bracket, ReadBracketed(sql, ref i)));
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
            {
                var start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '#' or '$')) i++;
                tokens.Add(new Tok(TokKind.Word, sql[start..i]));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < sql.Length && char.IsDigit(sql[i + 1])))
            {
                var start = i;
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.' || sql[i] == 'e' || sql[i] == 'E'
                       || ((sql[i] == '+' || sql[i] == '-') && i > start && (sql[i - 1] == 'e' || sql[i - 1] == 'E')))) i++;
                tokens.Add(new Tok(TokKind.Number, sql[start..i]));
                continue;
            }
            tokens.Add(new Tok(TokKind.Punct, c.ToString()));
            i++;
        }
        return tokens;
    }

    private static string ReadQuoted(string sql, ref int i, char quote)
    {
        var start = i;
        i++; // opening quote
        while (i < sql.Length)
        {
            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote) { i += 2; continue; }
                return sql[start..++i];
            }
            i++;
        }
        return sql[start..]; // unterminated literal: keep it exactly as written
    }

    private static string ReadBracketed(string sql, ref int i)
    {
        var start = i;
        i++;
        while (i < sql.Length)
        {
            if (sql[i] == ']')
            {
                if (i + 1 < sql.Length && sql[i + 1] == ']') { i += 2; continue; }
                return sql[start..++i];
            }
            i++;
        }
        return sql[start..];
    }
}
