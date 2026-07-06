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

}
