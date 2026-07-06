using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.SqlParser;
public static partial class SqlParser
{
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

        // Values list: ( @p1, 'literal', ... ) — split into top-level
        // comma-separated slots so parameters AND literals advance the
        // column index together; each slot binds positionally to its column.
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
        {
            pos++; // skip (
            int colIndex = 0;
            int depth = 0;
            var slot = new List<Token>();
            while (pos < tokens.Count)
            {
                var t = tokens[pos];
                if (t.Type == TokenType.Symbol && t.Value == "(")
                {
                    depth++;
                    slot.Add(t);
                }
                else if (t.Type == TokenType.Symbol && t.Value == ")")
                {
                    if (depth == 0)
                    {
                        BindInsertSlot(slot, insertColumns, colIndex, model);
                        break;
                    }
                    depth--;
                    slot.Add(t);
                }
                else if (t.Type == TokenType.Symbol && t.Value == "," && depth == 0)
                {
                    BindInsertSlot(slot, insertColumns, colIndex, model);
                    colIndex++;
                    slot.Clear();
                }
                else
                {
                    slot.Add(t);
                }
                pos++;
            }
        }
    }

    /// <summary>
    /// Binds one INSERT VALUES slot to its column: a lone @param binds the
    /// parameter, a lone literal (or -number) records a LiteralBinding for
    /// value-safety validation. Multi-token expressions are skipped but
    /// still consume their column position.
    /// </summary>
    private static void BindInsertSlot(List<Token> slot, List<string> insertColumns, int colIndex, QueryModel model)
    {
        if (colIndex >= insertColumns.Count)
            return;

        if (slot.Count == 1 && slot[0].Type == TokenType.Parameter)
        {
            var paramRef = model.Parameters.FirstOrDefault(p => p.Name == slot[0].Value);
            if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
            {
                paramRef.BoundColumnName = insertColumns[colIndex];
                paramRef.IsWriteTarget = true;
            }
            return;
        }

        if (slot.Count == 1 && (slot[0].Type == TokenType.Literal || slot[0].Type == TokenType.Number))
        {
            model.Literals.Add(new LiteralBinding
            {
                Kind = slot[0].Type == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                Value = slot[0].Value,
                BoundColumnName = insertColumns[colIndex]
            });
            return;
        }

        // Negative numeric literal: '-' Number
        if (slot.Count == 2 &&
            slot[0].Type == TokenType.Symbol && slot[0].Value == "-" &&
            slot[1].Type == TokenType.Number)
        {
            model.Literals.Add(new LiteralBinding
            {
                Kind = LiteralKind.Number,
                Value = "-" + slot[1].Value,
                BoundColumnName = insertColumns[colIndex]
            });
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

        // Parameters assigned in the SET clause (before WHERE) are write
        // targets; everything after WHERE is a comparison.
        bool inSet = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Keyword)
            {
                if (tokens[i].Value == "SET")
                {
                    inSet = true;
                    continue;
                }
                if (tokens[i].Value == "WHERE")
                    break;
            }
            if (inSet && i >= 2 &&
                tokens[i].Type == TokenType.Parameter &&
                tokens[i - 1].Type == TokenType.Symbol && tokens[i - 1].Value == "=" &&
                tokens[i - 2].Type == TokenType.Identifier)
            {
                var paramRef = model.Parameters.FirstOrDefault(p => p.Name == tokens[i].Value);
                if (paramRef != null)
                    paramRef.IsWriteTarget = true;
            }
        }
    }

}
