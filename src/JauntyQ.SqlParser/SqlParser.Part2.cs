using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;
public static partial class SqlParser
{
    private static int ParseFrom(List<Token> tokens, int pos, QueryModel model)
    {
        // Expect table name, optionally followed by alias
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
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
        }

        return pos;
    }

    private static int ParseJoin(List<Token> tokens, int pos, QueryModel model)
    {
        // Skip join type keywords (LEFT, RIGHT, INNER, OUTER, CROSS, FULL)
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword &&
               (tokens[pos].Value == "LEFT" || tokens[pos].Value == "RIGHT" ||
                tokens[pos].Value == "INNER" || tokens[pos].Value == "OUTER" ||
                tokens[pos].Value == "CROSS" || tokens[pos].Value == "FULL" ||
                tokens[pos].Value == "JOIN"))
        {
            pos++;
        }

        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
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

            // Parse ON condition
            if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "ON")
            {
                pos++; // skip ON
                pos = ParseJoinCondition(tokens, pos, model);
            }
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
