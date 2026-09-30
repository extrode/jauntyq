using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

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
            string tableName = StripQualifier(tokens[pos].Value);
            model.TargetTable = tableName;
            model.Tables.Add(new TableRef { TableName = tableName, Alias = string.Empty });
            pos++;
        }

        // Column list: ( col1, col2, ... )
        var insertColumns = new List<string>();
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == "(")
        {
            // Stryker disable once Statement : the loop below already skips any non-Identifier token (including this "(" itself) via its own pos++, so this explicit skip is a redundant no-op with identical final state either way
            pos++; // skip (
            while (pos < tokens.Count && !(tokens[pos].Type == TokenType.Symbol && tokens[pos].Value == ")"))
            {
                if (tokens[pos].Type == TokenType.Identifier)
                    insertColumns.Add(tokens[pos].Value);
                pos++;
            }
            // Skip ")". When the list was unterminated the loop stopped at
            // tokens.Count and this steps one past it, which every later
            // "pos < tokens.Count" guard rejects exactly as it rejects Count.
            pos++;
        }

        // INSERT ... SELECT: the source rows come from a SELECT rather than a
        // VALUES tuple. Parse the SELECT with the existing machinery so its
        // tables/joins/where validate, then bind lone-@param select items
        // positionally to the INSERT column list (mirroring VALUES slots).
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Keyword && tokens[pos].Value == "SELECT")
        {
            ParseInsertSelect(tokens, pos, insertColumns, model);
            // WHERE-clause bindings in the SELECT source (e.g. id = @userId) so
            // those parameters get a type. Slot-bound write params already set
            // their column; first-binding-wins leaves them intact.
            ExtractParameterBindings(tokens, model);
            ExtractReturning(tokens, model);
            return;
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
                    // Stryker disable once Statement : BindInsertSlot only inspects slot.Count and slot[0]/[1]'s own Type/Value for its Count==1 (Parameter/Literal) and Count==2 (negative-number) shapes, neither of which a bare "(" can ever form part of, so whether it's physically present in a multi-token slot is unobservable
                    slot.Add(t);
                }
                else if (t.Type == TokenType.Symbol && t.Value == ")")
                {
                    if (depth == 0)
                    {
                        BindInsertSlot(slot, insertColumns, colIndex, model);
                        // Stryker disable once Statement : without this break the loop just keeps absorbing trailing tokens (e.g. RETURNING) into further no-op slots -- BindInsertSlot's slot.Count==1-Parameter/Literal guard never matches multi-token leftovers, and ExtractReturning re-scans the full token list independently of this loop's pos, so no observable state differs either way
                        break;
                    }
                    depth--;
                    // Stryker disable once Statement : same argument as the "(" branch above -- a bare ")" can never form part of BindInsertSlot's Count==1 or Count==2 shape checks, so its presence in a multi-token slot is unobservable
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

        // RETURNING on a VALUES insert.
        ExtractReturning(tokens, model);
    }

    /// <summary>
    /// INSERT INTO t (cols) SELECT ... FROM &lt;table-or-cte&gt; [JOIN] [WHERE].
    /// The SELECT projection is captured into a scratch model so its FROM/JOIN
    /// tables land in the INSERT model for validation. Each SELECT-list item
    /// binds positionally to the INSERT column list: a lone @param binds like a
    /// VALUES slot; a multi-token expression or plain column consumes its
    /// position without binding.
    /// </summary>
    private static void ParseInsertSelect(List<Token> tokens, int selectPos, List<string> insertColumns, QueryModel model)
    {
        // Stryker disable once Initializer : selectModel is discarded after this method except for its Tables/Joins, which are merged below -- Name only ever affects nested subquery/CTE naming inside selectModel's own (never-merged) Subqueries/Ctes, so it has no observable effect through the parser's public API
        var selectModel = new QueryModel { Name = model.Name };
        int pos = ParseSelect(tokens, selectPos + 1, selectModel);

        // Continue with FROM / JOIN so the source tables are validated. These
        // tables belong to the SELECT source, distinct from the INSERT target,
        // so they go on the INSERT model's table list too (CTEs are virtual).
        while (pos < tokens.Count && tokens[pos].Type != TokenType.End)
        {
            var token = tokens[pos];
            if (token.Type == TokenType.Keyword && token.Value == "RETURNING")
                break;
            if (token.Type == TokenType.Keyword)
            {
                switch (token.Value)
                {
                    case "FROM":
                        pos = ParseFrom(tokens, pos + 1, selectModel);
                        break;
                    case "JOIN":
                    case "INNER":
                    case "LEFT":
                    case "RIGHT":
                    case "CROSS":
                    case "FULL":
                        pos = ParseJoin(tokens, pos, selectModel);
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

        // Bind lone-@param select items positionally to the INSERT columns by
        // walking the SELECT-list slots directly from the token stream.
        BindInsertSelectParams(tokens, selectPos + 1, insertColumns, model);

        // Merge the SELECT source tables/joins into the INSERT model for
        // validation (per-statement validators run on this single model).
        foreach (var t in selectModel.Tables)
            model.Tables.Add(t);
        foreach (var j in selectModel.Joins)
            model.Joins.Add(j);
    }

    /// <summary>
    /// Walks the SELECT-list slots of an INSERT...SELECT at paren-depth 0 and
    /// binds any lone-@param slot to the INSERT column at the same position.
    /// Multi-token slots consume their position without binding.
    /// </summary>
    private static void BindInsertSelectParams(List<Token> tokens, int pos, List<string> insertColumns, QueryModel model)
    {
        int colIndex = 0;
        int depth = 0;
        var slot = new List<Token>();
        void Flush()
        {
            if (slot.Count == 1 && slot[0].Type == TokenType.Parameter && colIndex < insertColumns.Count)
            {
                var paramRef = model.Parameters.Find(p => p.Name == slot[0].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundColumnName = insertColumns[colIndex];
                    paramRef.IsWriteTarget = true;
                }
            }
            slot.Clear();
        }

        for (; pos < tokens.Count && tokens[pos].Type != TokenType.End; pos++)
        {
            var t = tokens[pos];
            if (depth == 0 && t.Type == TokenType.Keyword && IsClauseKeyword(t.Value))
                break; // reached FROM/etc — end of select list
            if (t.Type == TokenType.Symbol && t.Value == "(") { depth++; slot.Add(t); }
            else if (t.Type == TokenType.Symbol && t.Value == ")") { depth--; slot.Add(t); }
            else if (depth == 0 && t.Type == TokenType.Symbol && t.Value == ",")
            {
                Flush();
                colIndex++;
            }
            else
            {
                slot.Add(t);
            }
        }
        Flush();
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
            var paramRef = model.Parameters.Find(p => p.Name == slot[0].Value);
            if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
            {
                paramRef.BoundColumnName = insertColumns[colIndex];
                paramRef.IsWriteTarget = true;
            }
        }
        else if (slot.Count == 1 && (slot[0].Type == TokenType.Literal || slot[0].Type == TokenType.Number))
        {
            model.Literals.Add(new LiteralBinding
            {
                Kind = slot[0].Type == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                Value = slot[0].Value,
                BoundColumnName = insertColumns[colIndex]
            });
        }
        // Negative numeric literal: '-' Number
        else if (slot.Count == 2 &&
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
            string tableName = StripQualifier(tokens[pos].Value);
            model.TargetTable = tableName;
            model.Tables.Add(new TableRef { TableName = tableName, Alias = string.Empty });
        }

        // Parameter bindings handled by ExtractParameterBindings (col = @param pattern)
        ExtractParameterBindings(tokens, model);

        // Parameters assigned in the SET clause (before WHERE) are write
        // targets; everything after WHERE is a comparison. A SET-list item
        // may itself be a scalar subquery carrying its own WHERE (e.g. a
        // correlated "copy this value from another row" column) -- that
        // nested WHERE is at paren depth > 0 and must not be mistaken for
        // the UPDATE's own top-level WHERE, or every SET-clause parameter
        // after it would be silently left un-flagged as a write target
        // (and, worse, ExtractParameterBindings' generic "identifier <op>
        // @param" scan above would already have bound it as if it were a
        // WHERE-side comparison parameter instead). Depth-gate both the
        // clause-boundary keywords and the assignment-shape match so a
        // parameter used *inside* the subquery's own predicate is never
        // mistaken for an outer SET assignment either.
        bool inSet = false;
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == "(")
            {
                depth++;
            }
            else if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")")
            {
                if (depth > 0) depth--;
            }
            else if (tokens[i].Type == TokenType.Keyword && tokens[i].Value == "SET" && depth == 0)
            {
                inSet = true;
            }
            else if (tokens[i].Type == TokenType.Keyword && tokens[i].Value == "WHERE" && depth == 0)
            {
                break;
            }
            // No i >= 2 guard needed: inSet means SET sat at some index s < i,
            // so tokens[i - 1] is in range, and tokens[i - 2] is only read once
            // tokens[i - 1] matched "=" -- which SET is not -- so i - 2 >= s.
            else if (inSet && depth == 0 &&
                tokens[i].Type == TokenType.Parameter &&
                tokens[i - 1].Type == TokenType.Symbol && tokens[i - 1].Value == "=" &&
                tokens[i - 2].Type == TokenType.Identifier)
            {
                var paramRef = model.Parameters.Find(p => p.Name == tokens[i].Value);
                if (paramRef != null)
                    paramRef.IsWriteTarget = true;
            }
        }

        ExtractReturning(tokens, model);
    }

}
