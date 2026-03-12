using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;

public static class SqlParser
{
    public static QueryModel Parse(List<Token> tokens, string queryName)
    {
        var model = new QueryModel { Name = queryName };
        int pos = 0;

        // Collect all parameters first (they can appear anywhere)
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Parameter)
            {
                if (!model.Parameters.Any(p => p.Name == token.Value))
                {
                    model.Parameters.Add(new ParameterRef { Name = token.Value });
                }
            }
        }

        // Detect unsupported constructs
        DetectUnsupportedConstructs(tokens, model);

        // Detect statement type from the first keyword
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Keyword)
            {
                switch (tokens[i].Value)
                {
                    case "INSERT":
                        model.StatementType = StatementType.Insert;
                        ParseInsert(tokens, i + 1, model);
                        return model;
                    case "UPDATE":
                        model.StatementType = StatementType.Update;
                        ParseUpdate(tokens, i + 1, model);
                        return model;
                    case "DELETE":
                        model.StatementType = StatementType.Delete;
                        ParseDelete(tokens, i + 1, model);
                        return model;
                    case "SELECT":
                        break; // fall through to normal SELECT parsing
                }
                break;
            }
        }

        // Extract parameter-to-column bindings (col = @param or @param = col)
        ExtractParameterBindings(tokens, model);

        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "SELECT":
                        pos = ParseSelect(tokens, pos + 1, model);
                        break;
                    case "FROM":
                        pos = ParseFrom(tokens, pos + 1, model);
                        break;
                    case "JOIN":
                    case "INNER":
                    case "LEFT":
                    case "RIGHT":
                    case "CROSS":
                    case "FULL":
                        pos = ParseJoin(tokens, pos, model);
                        break;
                    default:
                        pos++;
                        break;
                }
            }
            else
            {
                pos++;
            }
        }

        return model;
    }

    private static int ParseSelect(List<Token> tokens, int pos, QueryModel model)
    {
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];

            // Stop at FROM, WHERE, or other major clause keywords
            if (token.Type == TokenType.Keyword && IsClauseKeyword(token.Value))
                break;

            // Star select
            if (token.Type == TokenType.Symbol && token.Value == "*")
            {
                model.Columns.Add(new ColumnRef
                {
                    TableAlias = string.Empty,
                    ColumnName = "*",
                    OutputAlias = string.Empty
                });
                pos++;
                continue;
            }

            // Skip commas
            if (token.Type == TokenType.Symbol && token.Value == ",")
            {
                pos++;
                continue;
            }

            // Column reference (possibly qualified)
            if (token.Type == TokenType.Identifier)
            {
                var (tableAlias, columnName) = SplitQualifiedName(token.Value);
                string outputAlias = string.Empty;

                // Check for AS alias
                if (pos + 1 < tokens.Count)
                {
                    if (tokens[pos + 1].Type == TokenType.Keyword && tokens[pos + 1].Value == "AS")
                    {
                        if (pos + 2 < tokens.Count && tokens[pos + 2].Type == TokenType.Identifier)
                        {
                            outputAlias = tokens[pos + 2].Value;
                            pos += 2; // skip AS and alias
                        }
                    }
                    else if (tokens[pos + 1].Type == TokenType.Identifier &&
                             !IsClauseKeyword(tokens[pos + 1].Value))
                    {
                        // Implicit alias (no AS keyword): SELECT col alias, ...
                        // But be careful not to eat the FROM keyword or table name
                        // Only treat as alias if it's not followed by a dot-qualified name
                        // For safety, we only do implicit alias if the next-next token is a comma or clause keyword
                        if (pos + 2 < tokens.Count)
                        {
                            var afterAlias = tokens[pos + 2];
                            if ((afterAlias.Type == TokenType.Symbol && afterAlias.Value == ",") ||
                                (afterAlias.Type == TokenType.Keyword && IsClauseKeyword(afterAlias.Value)) ||
                                afterAlias.Type == TokenType.End)
                            {
                                outputAlias = tokens[pos + 1].Value;
                                pos++;
                            }
                        }
                    }
                }

                model.Columns.Add(new ColumnRef
                {
                    TableAlias = tableAlias,
                    ColumnName = columnName,
                    OutputAlias = outputAlias
                });
                pos++;
                continue;
            }

            pos++;
        }

        return pos;
    }

    private static int ParseFrom(List<Token> tokens, int pos, QueryModel model)
    {
        // Expect table name, optionally followed by alias
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            string tableName = tokens[pos].Value;
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
            string tableName = tokens[pos].Value;
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

    private static (string tableAlias, string columnName) SplitQualifiedName(string name)
    {
        int dotIndex = name.IndexOf('.');
        if (dotIndex >= 0)
        {
            return (name.Substring(0, dotIndex), name.Substring(dotIndex + 1));
        }
        return (string.Empty, name);
    }

    private static bool IsClauseKeyword(string value) =>
        value is "FROM" or "WHERE" or "JOIN" or
        "LEFT" or "RIGHT" or "INNER" or
        "OUTER" or "CROSS" or "FULL" or
        "GROUP" or "ORDER" or "LIMIT" or
        "HAVING" or "UNION";

    private static readonly HashSet<string> ComparisonOperators = new()
    {
        "=", "!=", "<>", "<", ">", "<=", ">="
    };

    private static void ExtractParameterBindings(List<Token> tokens, QueryModel model)
    {
        // Scan for: identifier <op> @param  or  @param <op> identifier
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Symbol || !ComparisonOperators.Contains(tokens[i].Value))
                continue;

            var left = tokens[i - 1];
            var right = tokens[i + 1];

            string? paramName = null;
            string tableAlias = string.Empty;
            string columnName = string.Empty;

            if (left.Type == TokenType.Identifier && right.Type == TokenType.Parameter)
            {
                paramName = right.Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            else if (left.Type == TokenType.Parameter && right.Type == TokenType.Identifier)
            {
                paramName = left.Value;
                (tableAlias, columnName) = SplitQualifiedName(right.Value);
            }

            if (paramName == null)
                continue;

            // Find the ParameterRef and bind it (first binding wins)
            var paramRef = model.Parameters.FirstOrDefault(p => p.Name == paramName);
            if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
            {
                paramRef.BoundTableAlias = tableAlias;
                paramRef.BoundColumnName = columnName;
            }
        }

        // Also scan for: identifier IN (@param) — common pattern
        for (int i = 0; i < tokens.Count - 4; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "IN" &&
                tokens[i + 2].Type == TokenType.Symbol && tokens[i + 2].Value == "(" &&
                tokens[i + 3].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 3].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                }
            }
        }

        // Also scan for: identifier LIKE @param
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "LIKE" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 2].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                }
            }
        }

        // Also scan for: identifier BETWEEN @param AND ...
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "BETWEEN" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i + 2].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                }
            }
        }
    }

    /// <summary>
    /// INSERT INTO Table (col1, col2) VALUES (@p1, @p2)
    /// </summary>
    private static void ParseInsert(List<Token> tokens, int pos, QueryModel model)
    {
        // Skip optional INTO
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "INTO")
            pos++;

        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            model.TargetTable = tokens[pos].Value;
            model.Tables.Add(new TableRef { TableName = tokens[pos].Value, Alias = string.Empty });
            pos++;
        }

        // Column list: ( col1, col2, ... )
        var insertColumns = new List<string>();
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
        {
            pos++; // skip (
            while (pos < tokens.Count && !(tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")"))
            {
                if (tokens[pos].Type == TokenType.Identifier)
                    insertColumns.Add(tokens[pos].Value);
                pos++;
            }
            if (pos < tokens.Count) pos++; // skip )
        }

        // Skip VALUES keyword
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "VALUES")
            pos++;

        // Values list: ( @p1, @p2, ... ) — bind positionally to columns
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
        {
            pos++; // skip (
            int colIndex = 0;
            while (pos < tokens.Count && !(tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")"))
            {
                if (tokens[pos].Type == TokenType.Parameter && colIndex < insertColumns.Count)
                {
                    var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[pos].Value);
                    if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                    {
                        paramRef.BoundColumnName = insertColumns[colIndex];
                    }
                    colIndex++;
                }
                pos++;
            }
        }
    }

    /// <summary>
    /// UPDATE Table SET col1 = @p1, col2 = @p2 WHERE ...
    /// </summary>
    private static void ParseUpdate(List<Token> tokens, int pos, QueryModel model)
    {
        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            model.TargetTable = tokens[pos].Value;
            model.Tables.Add(new TableRef { TableName = tokens[pos].Value, Alias = string.Empty });
            pos++;
        }

        // Parameter bindings handled by ExtractParameterBindings (col = @param pattern)
        ExtractParameterBindings(tokens, model);
    }

    /// <summary>
    /// DELETE FROM Table WHERE ... or DELETE Table WHERE ...
    /// </summary>
    private static void ParseDelete(List<Token> tokens, int pos, QueryModel model)
    {
        // Skip optional FROM
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "FROM")
            pos++;

        // Table name
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            model.TargetTable = tokens[pos].Value;
            model.Tables.Add(new TableRef { TableName = tokens[pos].Value, Alias = string.Empty });
        }

        // Parameter bindings handled by ExtractParameterBindings (col = @param in WHERE)
        ExtractParameterBindings(tokens, model);
    }

    private static void DetectUnsupportedConstructs(List<Token> tokens, QueryModel model)
    {
        bool seenSelect = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "SELECT":
                        if (seenSelect)
                        {
                            // Subquery (SELECT inside SELECT)
                            if (!model.UnsupportedConstructs.Contains("SUBQUERY"))
                                model.UnsupportedConstructs.Add("SUBQUERY");
                        }
                        seenSelect = true;
                        break;
                    case "UNION":
                        if (!model.UnsupportedConstructs.Contains("UNION"))
                            model.UnsupportedConstructs.Add("UNION");
                        break;
                }
            }
        }

        // CTE detection: WITH keyword at position 0 (before SELECT)
        if (tokens.Count > 0 && tokens[0].Type == TokenType.Keyword && tokens[0].Value == "WITH")
        {
            if (!model.UnsupportedConstructs.Contains("CTE"))
                model.UnsupportedConstructs.Add("CTE");
        }
    }
}
