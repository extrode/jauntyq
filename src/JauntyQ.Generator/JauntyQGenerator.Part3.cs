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
        foreach (var param in queryModel.Parameters)
        {
            if (!IdentifierGuard.IsValidIdentifier(param.Name))
            {
                return DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                    $"Parameter '@{param.Name}' in {entityName}.{methodName} is not a valid C# identifier. Rename it to letters, digits, and underscores, not starting with a digit.");
            }
        }
        return null;
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
