using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.Generator;

/// <summary>
/// Value-equatable shape of one .sql file: everything the aggregate pass
/// (synthetics, POCO overloads, facade) needs to know about it. A body edit
/// that keeps this equal leaves the aggregate output cached.
/// </summary>
internal sealed class FileSummary : IEquatable<FileSummary>
{
    public string EntityName { get; }
    public string MethodName { get; }

    /// <summary>True when the file suppresses the same-named auto-CRUD synthetic.</summary>
    public bool Claims { get; }

    /// <summary>True when the file produced a source file (parsed and validated clean).</summary>
    public bool Emitted { get; }

    /// <summary>Table whose canonical row POCO this query returns, if any.</summary>
    public string? CanonicalTable { get; }

    public FileSummary(string entityName, string methodName, bool claims, bool emitted, string? canonicalTable)
    {
        EntityName = entityName;
        MethodName = methodName;
        Claims = claims;
        Emitted = emitted;
        CanonicalTable = canonicalTable;
    }

    public bool Equals(FileSummary? other) =>
        other != null
        && Claims == other.Claims
        && Emitted == other.Emitted
        && string.Equals(EntityName, other.EntityName, StringComparison.Ordinal)
        && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal)
        && string.Equals(CanonicalTable, other.CanonicalTable, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as FileSummary);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = EntityName.GetHashCode();
            hash = hash * 31 + MethodName.GetHashCode();
            hash = hash * 31 + (CanonicalTable?.GetHashCode() ?? 0);
            hash = hash * 31 + (Claims ? 2 : 0) + (Emitted ? 1 : 0);
            return hash;
        }
    }
}
