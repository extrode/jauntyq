using System;
using System.Collections.Generic;
using Extrode.JauntyQ.Schema;

namespace Extrode.JauntyQ.Schema.Extraction;

/// <summary>
/// Resolves columns, function parameters and function returns whose declared
/// type is a captured alias or DOMAIN down to the primitive underneath
/// (spec 014, FR-004).
///
/// Shared rather than written once per extractor because the rule is identical
/// in all three dialects that have user types: the CAPTURE of a user type is
/// dialect-specific (pg_type vs sys.types), the resolution of a reference to
/// one is not. Triplicating it is how the three dialects drift apart, which is
/// the defect ColumnFacetExtractorParityTests exists to catch.
///
/// Composites and table types are deliberately left alone. They have members
/// rather than an underlying scalar, so there is nothing to resolve them TO;
/// they stay on their declared type and the generator refuses them by name
/// (JNT2025) instead of mapping them to object.
/// </summary>
public static class UserTypeResolution
{
    /// <summary>
    /// Rewrites every reference to a resolvable user type in place. Idempotent:
    /// a column already carrying <see cref="ColumnSchema.ResolvedFromUserType"/>
    /// is skipped, so calling this twice cannot resolve a resolved type again
    /// (which would matter if a DOMAIN were ever named after a primitive).
    /// </summary>
    public static void Apply(DatabaseSchema schema)
    {
        if (schema.UserTypes.Count == 0)
            return;

        var resolvable = new Dictionary<string, UserTypeSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var ut in schema.UserTypes.Values)
        {
            // Only Alias and Domain carry an underlying scalar. A composite or
            // table type reaching here with an UnderlyingDbType set would be a
            // capture bug, and resolving it would silently generate a scalar
            // property for a multi-column type.
            if (ut.Kind is UserTypeKind.Alias or UserTypeKind.Domain &&
                !string.IsNullOrEmpty(ut.UnderlyingDbType))
            {
                resolvable[ut.Name] = ut;
            }
        }

        if (resolvable.Count == 0)
            return;

        foreach (var table in schema.Tables.Values)
        {
            foreach (var column in table.Columns.Values)
            {
                if (column.ResolvedFromUserType != null)
                    continue;
                if (!resolvable.TryGetValue(column.DbType, out var ut))
                    continue;

                column.ResolvedFromUserType = ut.Name;
                column.DbType = ut.UnderlyingDbType!;
                // Facets come from the DOMAIN's declaration only where the
                // column does not already carry its own. A column cannot
                // narrow a domain's length, so in practice the column's value
                // is either absent or equal -- but preferring the column's
                // keeps this from overwriting a real captured facet with a
                // less specific one.
                column.MaxLength ??= ut.MaxLength;
                column.Precision ??= ut.Precision;
                column.Scale ??= ut.Scale;
            }
        }

        foreach (var fn in schema.Functions.Values)
        {
            foreach (var p in fn.Params)
            {
                if (p.ResolvedFromUserType != null)
                    continue;
                if (!resolvable.TryGetValue(p.DbType, out var ut))
                    continue;

                p.ResolvedFromUserType = ut.Name;
                p.DbType = ut.UnderlyingDbType!;
                p.MaxLength ??= ut.MaxLength;
                p.Precision ??= ut.Precision;
                p.Scale ??= ut.Scale;
            }

            if (fn.Return.ResolvedFromUserType == null &&
                resolvable.TryGetValue(fn.Return.DbType, out var rt))
            {
                fn.Return.ResolvedFromUserType = rt.Name;
                fn.Return.DbType = rt.UnderlyingDbType!;
                fn.Return.MaxLength ??= rt.MaxLength;
                fn.Return.Precision ??= rt.Precision;
                fn.Return.Scale ??= rt.Scale;
            }
        }
    }

    /// <summary>
    /// The snapshot key for a captured function: the bare name when it takes no
    /// arguments, otherwise <c>name(type,type)</c>.
    ///
    /// Deterministic and unique per overload, which is what lets both sides of
    /// a PostgreSQL overload survive capture so the generator can report
    /// JNT2023 over them — a name-keyed dictionary would keep whichever the
    /// catalog returned last, resolving the collision silently and in catalog
    /// order. SQL Server and MySQL permit no overloads, so there the key always
    /// degrades to something stable for the same function; it is shared anyway
    /// so the three dialects cannot drift into different key shapes.
    /// </summary>
    public static string FunctionKey(string name, IReadOnlyList<string> argTypes) =>
        argTypes.Count == 0 ? name : name + "(" + string.Join(",", argTypes) + ")";

    /// <summary>
    /// Splits a rendered type like <c>character varying(11)</c> or
    /// <c>numeric(12,2)</c> into the bare type name and its facets.
    ///
    /// Needed because the catalogs that know a domain's base type render it
    /// WITH its modifiers (PostgreSQL's <c>format_type</c>), while every
    /// DbType already in the snapshot comes from information_schema and is
    /// bare, with the facets in their own columns. Recording
    /// "character varying(11)" as a DbType would miss every mapping rule in
    /// DialectMapper and land the column back on object -- the exact defect
    /// FR-004 exists to fix, reintroduced one layer down.
    /// </summary>
    public static (string DbType, int? MaxLength, int? Precision, int? Scale) SplitRenderedType(string rendered)
    {
        if (string.IsNullOrWhiteSpace(rendered))
            return (string.Empty, null, null, null);

        int open = rendered.IndexOf('(');
        if (open < 0 || !rendered.EndsWith(")", StringComparison.Ordinal))
            return (rendered.Trim(), null, null, null);

        string bare = rendered.Substring(0, open).Trim();
        string inside = rendered.Substring(open + 1, rendered.Length - open - 2);
        string[] parts = inside.Split(',');

        // Two arguments is always precision/scale (numeric, decimal). One is a
        // length for a string/binary type and a precision for a numeric one --
        // and the numeric families are the only place a single argument means
        // precision, so name them rather than guessing from the value.
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), out int prec) &&
            int.TryParse(parts[1].Trim(), out int scale))
        {
            return (bare, null, prec, scale);
        }

        if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out int single))
        {
            bool numeric = bare.IndexOf("numeric", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           bare.IndexOf("decimal", StringComparison.OrdinalIgnoreCase) >= 0;
            return numeric ? (bare, null, single, null) : (bare, single, null, null);
        }

        // An unparseable modifier list -- a timestamp's precision, an enum's
        // members, anything else a catalog might render. Keep the bare name so
        // the mapping still works and drop what could not be understood rather
        // than guessing at it.
        return (bare, null, null, null);
    }
}
