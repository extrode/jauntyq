using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public static partial class SqlParser
{
    /// <summary>
    /// True when the token in a relation position is LATERAL: a construct the
    /// grammar does not model, arriving as an ordinary Identifier because the
    /// tokenizer's keyword set does not contain it.
    ///
    /// Spec 015 guarded only the join position, so <c>FROM LATERAL (...)</c>
    /// and the comma form <c>FROM a, LATERAL (...)</c> still fell through to
    /// the table-name branch and recorded a relation literally named
    /// "lateral" — the exact "JNT2001 points at your schema for a table the
    /// parser invented" failure 015 set out to remove. Guarding one position
    /// made the fix depend on where the consumer happened to write it.
    /// </summary>
    private static bool IsLateralKeyword(Token token) =>
        token.Type == TokenType.Identifier &&
        string.Equals(token.Value, "LATERAL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records an unmodeled relation construct and consumes its keyword,
    /// deliberately without adding a TableRef: inventing one is the bug.
    /// </summary>
    private static int RecordUnmodeledRelation(string construct, int pos, QueryModel model)
    {
        if (!model.UnsupportedConstructs.Contains(construct))
            model.UnsupportedConstructs.Add(construct);
        return pos + 1;
    }

    private static int ParseFrom(List<Token> tokens, int pos, QueryModel model)
    {
        // Expect table name, optionally followed by alias, then zero or more
        // old-style comma-separated tables (an implicit join: "FROM a, b").
        // Each comma-joined table is registered exactly like the first —
        // skipping this loop would silently drop every table after the first
        // one from model.Tables, breaking any qualified reference to it.
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            if (IsLateralKeyword(tokens[pos]))
                return RecordUnmodeledRelation("LATERAL", pos, model);

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
        bool sawCrossOrOuter = false;
        while (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword &&
               (tokens[pos].Value == "LEFT" || tokens[pos].Value == "RIGHT" ||
                tokens[pos].Value == "INNER" || tokens[pos].Value == "OUTER" ||
                tokens[pos].Value == "CROSS" || tokens[pos].Value == "FULL" ||
                tokens[pos].Value == "JOIN"))
        {
            if (tokens[pos].Value == "LEFT") sawLeft = true;
            else if (tokens[pos].Value == "RIGHT") sawRight = true;
            else if (tokens[pos].Value == "FULL") sawFull = true;
            if (tokens[pos].Value == "CROSS" || tokens[pos].Value == "OUTER") sawCrossOrOuter = true;
            pos++;
        }
        JoinKind joinKind = sawFull ? JoinKind.Full : sawLeft ? JoinKind.Left : sawRight ? JoinKind.Right : JoinKind.None;

        // Spec 015: LATERAL is not a tokenizer keyword, so it arrives here as
        // an ordinary Identifier in the table-name position -- and the block
        // below duly recorded a table literally named "lateral". The consumer
        // then met JNT2001 "Table 'lateral' does not exist in schema", which
        // sent them to look at their schema for a relation the PARSER had
        // invented. The grammar is what is missing, so say that instead.
        //
        // Recorded as its own construct rather than folded into SUBQUERY: the
        // derived table that follows LATERAL also raises SUBQUERY, and a
        // consumer reading "unsupported subquery" for a join form learns the
        // wrong thing about what to change.
        if (pos < tokens.Count && IsLateralKeyword(tokens[pos]))
            return RecordUnmodeledRelation("LATERAL", pos, model);

        // T-SQL's CROSS APPLY / OUTER APPLY is the same construct under
        // another spelling, and failed the same way: CROSS and OUTER are
        // consumed as join keywords above, leaving APPLY in the table-name
        // position to be recorded as a relation named "apply".
        //
        // Gated on having actually seen CROSS or OUTER rather than matching
        // the bare word: APPLY is reserved in T-SQL but not in the other four
        // dialects, so "JOIN apply ON ..." against a table genuinely named
        // apply must keep parsing. The gate makes the recognition positional,
        // which is what distinguishes the operator from the identifier.
        if (sawCrossOrOuter && pos < tokens.Count && tokens[pos].Type == TokenType.Identifier &&
            string.Equals(tokens[pos].Value, "APPLY", StringComparison.OrdinalIgnoreCase))
        {
            return RecordUnmodeledRelation("APPLY", pos, model);
        }

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
        // pos stays on the "(": the loop below steps over it like a ",",
        // so a separate skip here would be unobservable.

        var right = model.Tables[model.Tables.Count - 1];
        var left = model.Tables[model.Tables.Count - 2];
        string rightKey = !string.IsNullOrEmpty(right.Alias) ? right.Alias : right.TableName;
        string leftKey = !string.IsNullOrEmpty(left.Alias) ? left.Alias : left.TableName;

        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            if (tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")")
                return pos + 1;
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
            pos++; // "(", identifier or ","
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
        // Stryker disable once Equality : lastDot == 0 (name starting with a
        // dot) is unreachable -- name is always an Identifier token's value,
        // and the tokenizer's IsIdentifierStart only accepts a letter or
        // underscore, so this boundary can never actually be hit
        if (lastDot < 0)
            return (string.Empty, name);

        string columnName = name.Substring(lastDot + 1);
        string prefix = name.Substring(0, lastDot);
        int prevDot = prefix.LastIndexOf('.');
        // Stryker disable once Conditional,Equality : Conditional -- when
        // prevDot < 0, prefix.Substring(prevDot + 1) == prefix.Substring(0)
        // == prefix itself, identical to the false branch, so forcing the
        // true branch is a no-op and indistinguishable by any input.
        // Equality -- prevDot == 0 (prefix starting with a dot) is
        // unreachable for the same identifier-grammar reason as lastDot above
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
        // Stryker disable once Conditional,Equality : Conditional -- when
        // lastDot < 0, name.Substring(lastDot + 1) == name.Substring(0) ==
        // name itself, identical to the false branch, so forcing the true
        // branch is a no-op and indistinguishable by any input. Equality --
        // lastDot == 0 (name starting with a dot) is unreachable: name is
        // always an Identifier token's value, and the tokenizer only accepts
        // a letter or underscore as an identifier's first character
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
        "=", "!=", "", "<", ">", "<=", ">="
    };

}
