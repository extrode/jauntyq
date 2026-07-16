using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;
public static partial class SqlParser
{
    private static int ParseFrom(List<Token> tokens, int pos, QueryModel model)
    {
        // Expect table name, optionally followed by alias, then zero or more
        // old-style comma-separated tables (an implicit join: "FROM a, b").
        // Each comma-joined table is registered exactly like the first —
        // skipping this loop would silently drop every table after the first
        // one from model.Tables, breaking any qualified reference to it.
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            string tableName = StripQualifier(tokens[pos].Value);
            string alias = string.Empty;
            pos++;

            // Check for alias
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
            {
                alias = tokens[pos].Value;
                pos++;
            }
            else if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "AS")
            {
                pos++; // skip AS
                if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
                {
                    alias = tokens[pos].Value;
                    pos++;
                }
            }

            model.Tables.Add(new TableRef { TableName = tableName, Alias = alias });

            if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ",")
            {
                pos++; // skip comma, loop parses the next table
                continue;
            }
            break;
        }

        return pos;
    }

    private static int ParseJoin(List<Token> tokens, int pos, QueryModel model)
    {
        // Skip join type keywords (LEFT, RIGHT, INNER, OUTER, CROSS, FULL),
        // noting LEFT/RIGHT/FULL so the joined table's nullability can be
        // modeled — an outer join can produce this table's columns as NULL
        // even when the schema declares them NOT NULL.
        bool sawLeft = false, sawRight = false, sawFull = false;
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword &&
               (tokens[pos].Value == "LEFT" || tokens[pos].Value == "RIGHT" ||
                tokens[pos].Value == "INNER" || tokens[pos].Value == "OUTER" ||
                tokens[pos].Value == "CROSS" || tokens[pos].Value == "FULL" ||
                tokens[pos].Value == "JOIN"))
        {
            if (tokens[pos].Value == "LEFT") sawLeft = true;
            else if (tokens[pos].Value == "RIGHT") sawRight = true;
            else if (tokens[pos].Value == "FULL") sawFull = true;
            pos++;
        }
        JoinKind joinKind = sawFull ? JoinKind.Full : sawLeft ? JoinKind.Left : sawRight ? JoinKind.Right : JoinKind.None;

        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            string tableName = StripQualifier(tokens[pos].Value);
            string alias = string.Empty;
            pos++;

            // Check for alias. "USING" is not a tokenizer keyword (see
            // SqlTokenizer's Keywords set), so it tokenizes as a plain
            // Identifier just like a real alias -- without excluding it here,
            // "JOIN customers USING (customer_id)" (no real alias given)
            // swallowed the literal word "USING" as this table's alias,
            // corrupting any later unqualified/bare-name reference to it,
            // and left the "(customer_id)" column list to be silently
            // skipped token-by-token by ParseStatement's main loop with no
            // join key ever recorded.
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier &&
                !string.Equals(tokens[pos].Value, "USING", StringComparison.OrdinalIgnoreCase))
            {
                alias = tokens[pos].Value;
                pos++;
            }
            else if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "AS")
            {
                pos++; // skip AS
                if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
                {
                    alias = tokens[pos].Value;
                    pos++;
                }
            }

            model.Tables.Add(new TableRef { TableName = tableName, Alias = alias, Join = joinKind });

            // Parse ON condition
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "ON")
            {
                pos++; // skip ON
                pos = ParseJoinCondition(tokens, pos, model);
            }
            else if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier &&
                     string.Equals(tokens[pos].Value, "USING", StringComparison.OrdinalIgnoreCase))
            {
                pos++; // skip USING
                pos = ParseUsingClause(tokens, pos, model);
            }
        }

        return pos;
    }

    /// <summary>
    /// <c>USING (col1 [, col2 ...])</c> is sugar for <c>ON left.col1 = right.col1
    /// [AND left.col2 = right.col2 ...]</c>, matching same-named columns between
    /// the just-joined table (the last entry in model.Tables) and the table
    /// immediately preceding it in the FROM/JOIN chain. <paramref name="pos"/>
    /// points just past the USING keyword.
    /// </summary>
    private static int ParseUsingClause(List<Token> tokens, int pos, QueryModel model)
    {
        if (model.Tables.Count < 2 || !(pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "("))
            return pos;
        pos++; // skip (

        var right = model.Tables[model.Tables.Count - 1];
        var left = model.Tables[model.Tables.Count - 2];
        string rightKey = !string.IsNullOrEmpty(right.Alias) ? right.Alias : right.TableName;
        string leftKey = !string.IsNullOrEmpty(left.Alias) ? left.Alias : left.TableName;

        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            if (tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")")
            {
                pos++;
                break;
            }
            if (tokens[pos].Type == TokenType.Identifier)
            {
                string col = StripQualifier(tokens[pos].Value);
                model.Joins.Add(new JoinRef
                {
                    LeftTable = leftKey,
                    LeftColumn = col,
                    RightTable = rightKey,
                    RightColumn = col
                });
            }
            pos++; // identifier or ","
        }

        return pos;
    }

    private static int ParseJoinCondition(List<Token> tokens, int pos, QueryModel model)
    {
        // Expect: left_col = right_col
        if (pos + 2 < tokens.Count &&
            tokens[pos].Type == TokenType.Identifier &&
            tokens[pos + 1].Type == TokenType.Symbol && tokens[pos + 1].Value == "=" &&
            tokens[pos + 2].Type == TokenType.Identifier)
        {
            var (leftTable, leftColumn) = SplitQualifiedName(tokens[pos].Value);
            var (rightTable, rightColumn) = SplitQualifiedName(tokens[pos + 2].Value);

            model.Joins.Add(new JoinRef
            {
                LeftTable = leftTable,
                LeftColumn = leftColumn,
                RightTable = rightTable,
                RightColumn = rightColumn
            });

            pos += 3;

            // Handle additional AND conditions in the ON clause
            while (pos + 3 < tokens.Count &&
                   tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "AND" &&
                   tokens[pos + 1].Type == TokenType.Identifier &&
                   tokens[pos + 2].Type == TokenType.Symbol && tokens[pos + 2].Value == "=" &&
                   tokens[pos + 3].Type == TokenType.Identifier)
            {
                var (lt, lc) = SplitQualifiedName(tokens[pos + 1].Value);
                var (rt, rc) = SplitQualifiedName(tokens[pos + 3].Value);

                model.Joins.Add(new JoinRef
                {
                    LeftTable = lt,
                    LeftColumn = lc,
                    RightTable = rt,
                    RightColumn = rc
                });

                pos += 4;
            }
        }

        return pos;
    }

    /// <summary>
    /// Splits a possibly-qualified column reference into (tableAlias, columnName).
    /// Only the LAST dot-segment is ever the column name; only the segment
    /// immediately before it can be a table alias/name. A schema/catalog
    /// qualifier further out (e.g. the "dbo" in "dbo.Products.ProductId") is
    /// discarded here for the same reason <see cref="StripQualifier"/> drops
    /// it from <c>TableRef.TableName</c>: the schema model has no notion of
    /// schema/catalog scoping, so only the innermost table-ish segment can
    /// ever match an alias or table name in scope.
    /// </summary>
    private static (string tableAlias, string columnName) SplitQualifiedName(string name)
    {
        int lastDot = name.LastIndexOf('.');
        if (lastDot < 0)
            return (string.Empty, name);

        string columnName = name.Substring(lastDot + 1);
        string prefix = name.Substring(0, lastDot);
        int prevDot = prefix.LastIndexOf('.');
        string tableAlias = prevDot >= 0 ? prefix.Substring(prevDot + 1) : prefix;
        return (tableAlias, columnName);
    }

    /// <summary>
    /// Drops any schema/catalog qualifier from a dotted table reference (e.g.
    /// "dbo.Products" or "server.dbo.Products" -&gt; "Products"), keeping only
    /// the final segment. The tokenizer merges a dotted FROM/JOIN/INSERT
    /// INTO/UPDATE/DELETE-FROM identifier into one Identifier token (dots are
    /// part of an identifier's character class), and <c>DatabaseSchema.Tables</c>
    /// is keyed by bare table name with no schema/catalog concept — so an
    /// unqualified strip here is the only thing that lets idiomatic
    /// schema-qualified SQL (routine on SQL Server/Postgres) resolve against
    /// the schema snapshot instead of failing JNT2001 on the literal
    /// "dbo.Products" string. This only affects the parsed IR used for
    /// validation/type-resolution; the emitted CommandText is always the
    /// original raw SQL text, so the schema qualifier the author wrote is
    /// preserved verbatim at runtime.
    /// </summary>
    private static string StripQualifier(string name)
    {
        int lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name.Substring(lastDot + 1) : name;
    }

    private static bool IsClauseKeyword(string value) =>
        value is "FROM" or "WHERE" or "JOIN" or
        "LEFT" or "RIGHT" or "INNER" or
        "OUTER" or "CROSS" or "FULL" or
        "GROUP" or "ORDER" or "LIMIT" or
        "HAVING" or "UNION" or "RETURNING";

    private static readonly HashSet<string> ComparisonOperators = new()
    {
        "=", "!=", "<>", "<", ">", "<=", ">="
    };

}
