using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;

public partial class JauntyQGenerator : IIncrementalGenerator
{

    /// <summary>
    /// JNT2004 (H2): SQL @parameter names are emitted verbatim as C# parameter
    /// identifiers. The tokenizer already restricts them to [A-Za-z0-9_], but a
    /// leading digit (e.g. @1x) or a C# keyword still produces broken/ambiguous
    /// code. Returns a diagnostic for the first illegal name, else null.
    /// (Keywords are safe because emission '@'-escapes them, so only the
    /// bare-identifier shape is enforced here.)
    /// </summary>
    private static DiagnosticInfo? ValidateParameterNames(QueryModel queryModel, string entityName, string methodName)
    {
        var seen = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in queryModel.Parameters)
        {
            if (!IdentifierGuard.IsValidIdentifier(param.Name))
            {
                return DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                    $"Parameter '@{param.Name}' in {entityName}.{methodName} is not a valid C# identifier. Rename it to letters, digits, and underscores, not starting with a digit.");
            }

            // JNT2012 (AUD-R75-02): on this path EmittedParam.CSharpName is the
            // literal SQL parameter name -- no PascalCase/camelCase folding --
            // so a "__"-prefixed name reaches the emitted identifiers directly
            // and can collide with the generator's own bookkeeping locals. The
            // -- @call path is structurally immune (ToPascalCase never
            // re-emits an underscore) and so is not checked here.
            if (param.Name.StartsWith("__", StringComparison.Ordinal))
            {
                return DiagnosticInfo.From(JauntyDiagnostics.JNT2012,
                    $"Parameter '@{param.Name}' in {entityName}.{methodName} uses the reserved '__' prefix. " +
                    $"JauntyQ emits its own bookkeeping identifiers (__conn, __cmd, __reader, __weOpened and others) in that namespace, " +
                    $"so a '__'-prefixed parameter can silently collide with generated code. Rename it without the leading double underscore.");
            }

            // JNT2013 (AUD-R75-01): two parameters emitting the same C#
            // identifier produce a duplicate formal (CS0100). Unfolded here,
            // so this catches only a genuine repeat spelling; the folding case
            // lives on the -- @call path (JauntyQGenerator.Part2.cs).
            if (seen.TryGetValue(param.Name, out string? firstSpelling))
            {
                return DiagnosticInfo.From(JauntyDiagnostics.JNT2013,
                    $"Parameters '@{firstSpelling}' and '@{param.Name}' in {entityName}.{methodName} both emit the C# parameter '{param.Name}'. " +
                    $"Rename one of them.");
            }
            seen[param.Name] = param.Name;
        }
        return null;
    }

    /// <summary>
    /// JNT2013 (AUD-R75-01): returns the first C# parameter identifier that two
    /// distinct stored-procedure parameters both fold to, else null.
    ///
    /// Folds with the exact expression the emitter uses at
    /// <c>CodeEmitter.Part11.cs:60,76,119,183,235</c> so the check and the
    /// emission cannot drift apart. Two shapes reach this:
    /// <c>order_number</c>/<c>OrderNumber</c> (ToPascalCase strips separators),
    /// and ToPascalCase's degenerate <c>if (sb.Length == 0) return "_";</c>
    /// fallback, where any two names with no alphanumeric content both fold to
    /// bare <c>_</c>.
    /// </summary>
    private static string? FindDuplicateParameterName(
        System.Collections.Generic.IEnumerable<ProcedureParam> parameters,
        out string? firstRawName,
        out string? secondRawName)
    {
        var seen = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in parameters)
        {
            string folded = IdentifierGuard.Escape(CodeEmitter.ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            if (seen.TryGetValue(folded, out string? firstRaw))
            {
                firstRawName = firstRaw;
                secondRawName = p.Name;
                return folded;
            }
            seen[folded] = p.Name;
        }
        firstRawName = null;
        secondRawName = null;
        return null;
    }

    /// <summary>
    /// Validates the type resolution of expression projection items (Feature A):
    ///   JNT3005 — an expression whose type could not be inferred and has no
    ///             matching -- @type directive.
    ///   JNT3006 — a -- @type directive whose alias names no expression item in
    ///             this projection list.
    /// <paramref name="sourceColumns"/> is the parsed SELECT/RETURNING list;
    /// <paramref name="projection"/> is the built projection carrying resolution
    /// state. Returns a (possibly empty) list of diagnostics.
    /// </summary>
    private static System.Collections.Generic.List<DiagnosticInfo> ValidateExpressionTypes(
        System.Collections.Generic.List<ColumnRef> sourceColumns,
        ProjectionModel projection,
        Directives.DirectiveModel? directives)
    {
        var result = new System.Collections.Generic.List<DiagnosticInfo>();

        // JNT3005: unresolved expression types.
        foreach (var pcol in projection.Columns)
        {
            if (!string.IsNullOrEmpty(pcol.UnresolvedExpressionAlias))
            {
                result.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3005,
                    $"Expression '{pcol.UnresolvedExpressionAlias}' has no inferable type. Declare it with: -- @type {pcol.UnresolvedExpressionAlias} <dbtype>."));
            }
        }

        // JNT3006: a -- @type alias that matches no expression item here.
        if (directives?.TypeDirectives != null)
        {
            var exprAliases = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Plain (non-expression) output names are collected too, purely to
            // tell the two failure modes apart in the message. The alias
            // resolver scans this SELECT's own projection list only, so a value
            // computed inside a CTE and re-projected by the outer SELECT
            // arrives here as a PLAIN column -- the directive is unmatched, but
            // nothing is misspelled, and "check the alias spelling" sent the
            // author hunting a typo that does not exist.
            var plainNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in sourceColumns)
            {
                if (col.IsExpression)
                {
                    if (!string.IsNullOrEmpty(col.OutputAlias))
                        exprAliases.Add(col.OutputAlias);
                    continue;
                }
                string name = !string.IsNullOrEmpty(col.OutputAlias) ? col.OutputAlias : col.ColumnName;
                if (!string.IsNullOrEmpty(name) && name != "*")
                    plainNames.Add(name);
            }

            foreach (var td in directives.TypeDirectives)
            {
                if (exprAliases.Contains(td.Alias))
                    continue;

                result.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3006,
                    plainNames.Contains(td.Alias)
                        ? $"-- @type names alias '{td.Alias}', which this SELECT projects as a plain column, not an expression. " +
                          $"-- @type only types expression items in this statement's own projection list, so it cannot reach " +
                          $"an expression computed inside a CTE (or any other subquery) and re-projected here. Write the " +
                          $"expression in this SELECT's projection list, where the directive can see it."
                        : $"-- @type names alias '{td.Alias}', but no expression projection item uses 'AS {td.Alias}'. Check the alias spelling."));
            }
        }

        return result;
    }

    /// <summary>
    /// Case-insensitive lookup of a stored procedure in the snapshot (the
    /// dictionary key casing may differ from the -- @call name as written).
    /// </summary>
    private static bool TryResolveProcedure(DatabaseSchema schema, string name, out ProcedureSchema? procedure)
    {
        if (schema.Procedures.TryGetValue(name, out procedure))
            return true;
        foreach (var kvp in schema.Procedures)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                procedure = kvp.Value;
                return true;
            }
        }
        procedure = null;
        return false;
    }

    /// <summary>
    /// Normalized token stream: keywords/identifiers case-folded, whitespace
    /// and comments gone. Two queries with equal fingerprints do identical
    /// work regardless of formatting (JNT8005).
    /// </summary>
    private static string ComputeFingerprint(System.Collections.Generic.List<Token> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.End)
                break;
            if (sb.Length > 0)
                sb.Append(' ');
            switch (token.Type)
            {
                case TokenType.Keyword:
                    sb.Append(token.Value.ToUpperInvariant());
                    break;
                case TokenType.Identifier:
                    sb.Append(token.Value.ToLowerInvariant());
                    break;
                case TokenType.Parameter:
                    sb.Append('@').Append(token.Value.ToLowerInvariant());
                    break;
                case TokenType.Literal:
                    sb.Append('\'').Append(token.Value).Append('\'');
                    break;
                default:
                    sb.Append(token.Value);
                    break;
            }
        }
        return sb.ToString();
    }

}
