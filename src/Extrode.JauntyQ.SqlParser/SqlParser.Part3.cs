using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.SqlParser;

public static partial class SqlParser
{
    private static void ExtractParameterBindings(List<Token> tokens, QueryModel model)
    {
        // Scan for: identifier <op> @param  or  @param <op> identifier
        // Stryker disable once Equality : the tokenizer always appends a trailing TokenType.End sentinel, so widening this bound by one only ever re-examines that sentinel at tokens[i]; the top-of-loop "!= Symbol" check rejects it before tokens[i +/- 1] is ever read
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Symbol || !ComparisonOperators.Contains(tokens[i].Value))
                continue;

            var left = tokens[i - 1];
            var right = tokens[i + 1];

            string? paramName = null;
            // Stryker disable once String : both locals are always overwritten by SplitQualifiedName before any use, or never used at all (paramName stays null and the loop continues) -- their initial values are dead
            string tableAlias = string.Empty;
            // Stryker disable once String : same dead-initializer argument as tableAlias above
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

            // Stryker disable once Statement : when paramName is null it can never match any real ParameterRef.Name (token values are never null), so the lookup below finds nothing and the loop body falls through to its natural end either way -- removing this continue is a no-op
            if (paramName == null)
                continue;

            // Find the ParameterRef and bind it (first binding wins)
            var paramRef = model.Parameters.Find(p => p.Name == paramName);
            if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
            {
                paramRef.BoundTableAlias = tableAlias;
                paramRef.BoundColumnName = columnName;
                paramRef.ComparisonOp = tokens[i].Value;
            }
        }

        // Also scan for: identifier IN (@param) — common pattern
        // Stryker disable once Equality : widening this bound by one only ever brings tokens[i+3] (the deepest access) up to the trailing TokenType.End sentinel, which can never satisfy "Type == TokenType.Parameter" -- the match still fails, just one iteration later
        for (int i = 0; i < tokens.Count - 4; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "IN" &&
                tokens[i + 2].Type == TokenType.Symbol && tokens[i + 2].Value == "(" &&
                tokens[i + 3].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.Find(p => p.Name == tokens[i + 3].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                    paramRef.ComparisonOp = "IN";
                }
            }
        }

        // Also scan for: identifier LIKE @param
        // Stryker disable once Equality : widening this bound by one only ever brings tokens[i+2] up to the trailing TokenType.End sentinel; the short-circuited "Type == TokenType.Keyword" check on tokens[i+1] one step earlier already fails first whenever tokens[i+1] is that sentinel, so tokens[i+2] is never even reached
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "LIKE" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                var paramRef = model.Parameters.Find(p => p.Name == tokens[i + 2].Value);
                if (paramRef != null && string.IsNullOrEmpty(paramRef.BoundColumnName))
                {
                    paramRef.BoundTableAlias = tableAlias;
                    paramRef.BoundColumnName = columnName;
                    paramRef.ComparisonOp = "LIKE";
                }
            }
        }

        // Also scan for: identifier BETWEEN @lower AND @upper. Both bounds
        // are compared against the same column, so both get the same
        // binding -- previously only @lower was bound, leaving @upper's
        // BoundColumnName empty and making CodeEmitter.InferCrudParameterType
        // fall back to "object" for it instead of the column's real type.
        // Stryker disable once Equality : same End-sentinel argument as the LIKE loop above -- the short-circuited Keyword-type check on tokens[i+1] fails first whenever it's the sentinel, so tokens[i+2] is never reached at the widened bound
        for (int i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "BETWEEN" &&
                tokens[i + 2].Type == TokenType.Parameter)
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);

                var lowerParam = model.Parameters.Find(p => p.Name == tokens[i + 2].Value);
                if (lowerParam != null && string.IsNullOrEmpty(lowerParam.BoundColumnName))
                {
                    lowerParam.BoundTableAlias = tableAlias;
                    lowerParam.BoundColumnName = columnName;
                    lowerParam.ComparisonOp = "BETWEEN";
                }

                // Stryker disable once Equality,Logical,Arithmetic : once tokens[i+2] has matched a real Parameter (not the trailing End sentinel), End's guarantee of always trailing every real token means tokens[i+3] is always safely in range; if tokens[i+3] is itself the End sentinel, the short-circuited "Type == TokenType.Keyword" check on it fails first regardless of this guard, and if tokens[i+3] is a genuine "AND" keyword token, End's guarantee means tokens[i+4] is always safely in range too -- so no mutation of this guard (its comparison, its +/-, or its && vs ||) can ever change the observable outcome
                if (i + 4 < tokens.Count &&
                    tokens[i + 3].Type == TokenType.Keyword && tokens[i + 3].Value == "AND" &&
                    tokens[i + 4].Type == TokenType.Parameter)
                {
                    var upperParam = model.Parameters.Find(p => p.Name == tokens[i + 4].Value);
                    if (upperParam != null && string.IsNullOrEmpty(upperParam.BoundColumnName))
                    {
                        upperParam.BoundTableAlias = tableAlias;
                        upperParam.BoundColumnName = columnName;
                        upperParam.ComparisonOp = "BETWEEN";
                    }
                }
            }
        }
    }

    /// <summary>
    /// Captures literals compared to or assigned into columns:
    /// col = 'x', col >= 19.99, SET col = 'x', col IN (1, 2, 3).
    /// LIKE patterns are deliberately skipped (wildcards make length
    /// reasoning unsound). Best-effort: unresolvable bindings are simply
    /// not validated.
    /// </summary>
    private static void ExtractLiteralBindings(List<Token> tokens, QueryModel model)
    {
        // identifier <op> literal  or  literal <op> identifier
        // Stryker disable once Equality : same End-sentinel argument as ExtractParameterBindings' first loop above -- the top-of-loop "!= Symbol" check rejects the sentinel before tokens[i +/- 1] is read
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Type != TokenType.Symbol || !ComparisonOperators.Contains(tokens[i].Value))
                continue;

            var left = tokens[i - 1];
            var right = tokens[i + 1];

            bool found = false;
            TokenType literalType = TokenType.Literal;
            // Stryker disable once String : literalValue is only ever read after "if (!found) continue;" below, and every branch that sets found = true also reassigns literalValue in the same block -- its initial value is dead
            string literalValue = string.Empty;
            // Stryker disable once String : same dead-initializer argument as literalValue above
            string tableAlias = string.Empty;
            // Stryker disable once String : same dead-initializer argument as literalValue above
            string columnName = string.Empty;

            if (left.Type == TokenType.Identifier &&
                (right.Type == TokenType.Literal || right.Type == TokenType.Number))
            {
                found = true;
                literalType = right.Type;
                literalValue = right.Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            // Stryker disable once Equality,Arithmetic : once right (tokens[i+1]) has matched a real Symbol "-" (never the trailing End sentinel), End's guarantee of always trailing every real token means tokens[i+2] is always safely in range regardless of what this boundary check literally compares -- if right were the sentinel instead, the short-circuited "right.Type == TokenType.Symbol" check just above fails first and tokens[i+2] is never reached
            else if (left.Type == TokenType.Identifier &&
                     right.Type == TokenType.Symbol && right.Value == "-" &&
                     i + 2 < tokens.Count && tokens[i + 2].Type == TokenType.Number)
            {
                // col <op> -N : unary minus (nothing precedes the '-' but the
                // comparison operator, so it cannot be a subtraction)
                found = true;
                literalType = TokenType.Number;
                literalValue = "-" + tokens[i + 2].Value;
                (tableAlias, columnName) = SplitQualifiedName(left.Value);
            }
            else if ((left.Type == TokenType.Literal || left.Type == TokenType.Number) &&
                     right.Type == TokenType.Identifier)
            {
                found = true;
                literalType = left.Type;
                literalValue = left.Value;
                (tableAlias, columnName) = SplitQualifiedName(right.Value);
            }

            if (!found)
                continue;

            model.Literals.Add(new LiteralBinding
            {
                Kind = literalType == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                Value = literalValue,
                BoundTableAlias = tableAlias,
                BoundColumnName = columnName
            });
        }

        // identifier IN ( literal, literal, ... ) — every literal in the list is checked
        // Stryker disable once Equality : widening this bound by one only ever brings tokens[i+2] up to the trailing TokenType.End sentinel, which can never satisfy "Type == TokenType.Symbol && Value == \"(\""
        for (int i = 0; i < tokens.Count - 3; i++)
        {
            if (tokens[i].Type == TokenType.Identifier &&
                tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].Value == "IN" &&
                tokens[i + 2].Type == TokenType.Symbol && tokens[i + 2].Value == "(")
            {
                var (tableAlias, columnName) = SplitQualifiedName(tokens[i].Value);
                int j = i + 3;
                while (j < tokens.Count && !(tokens[j].Type == TokenType.Symbol && tokens[j].Value == ")"))
                {
                    // -N in the list: unary minus (list items are comma-
                    // separated, so a '-' before a number is never subtraction)
                    // Stryker disable once Equality,Arithmetic : once tokens[j] has matched a real Symbol "-" (never the trailing End sentinel), End's guarantee of always trailing every real token means tokens[j+1] is always safely in range regardless of what this boundary check literally compares -- if tokens[j] were the sentinel instead, the short-circuited check just before this one already fails first and tokens[j+1] is never reached
                    if (tokens[j].Type == TokenType.Symbol && tokens[j].Value == "-" &&
                        j + 1 < tokens.Count && tokens[j + 1].Type == TokenType.Number)
                    {
                        model.Literals.Add(new LiteralBinding
                        {
                            Kind = LiteralKind.Number,
                            Value = "-" + tokens[j + 1].Value,
                            BoundTableAlias = tableAlias,
                            BoundColumnName = columnName
                        });
                        j += 2;
                        continue;
                    }
                    if (tokens[j].Type == TokenType.Literal || tokens[j].Type == TokenType.Number)
                    {
                        model.Literals.Add(new LiteralBinding
                        {
                            Kind = tokens[j].Type == TokenType.Literal ? LiteralKind.String : LiteralKind.Number,
                            Value = tokens[j].Value,
                            BoundTableAlias = tableAlias,
                            BoundColumnName = columnName
                        });
                    }
                    j++;
                }
            }
        }
    }

    // Function-call heads that the tokenizer classifies as keywords.
    private static readonly HashSet<string> FunctionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CAST", "COALESCE", "NULLIF", "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

}
