using JauntyQ.Analysis.Diff;

namespace JauntyQ.Analysis.Impact;

/// <summary>
/// The single, shared classification engine (FR-006): both the Roslyn generator
/// (build-time JNT9004) and the CLI report call this so the two surfaces cannot
/// diverge. Classification is the intersection of a query's referenced objects
/// with the <see cref="SchemaDelta"/>:
/// <list type="bullet">
/// <item>references a removed table/column → BREAKING</item>
/// <item>references a still-present modified column → RISKY</item>
/// <item>touched by an unmodeled (JNT9001) statement → RISKY</item>
/// <item>references no changed object → SAFE</item>
/// </list>
/// A query already invalid against the baseline is not attributed to the
/// migration (SAFE, no reasons — SC-006/FR-010). Any query whose referenced set
/// intersects the delta is never SAFE (SC-004).
/// </summary>
public static class ImpactClassifier
{
    public static MigrationImpactReport Classify(
        SchemaDelta delta,
        IReadOnlyList<QueryImpactInput> queries,
        string baselineId,
        IEnumerable<string> migrationSet)
    {
        var entries = new List<ImpactEntry>(queries.Count);
        foreach (var q in queries)
            entries.Add(ClassifyOne(delta, q));

        return new MigrationImpactReport(baselineId, new List<string>(migrationSet), entries);
    }

    private static ImpactEntry ClassifyOne(SchemaDelta delta, QueryImpactInput q)
    {
        // A query already broken against the baseline: its state is not
        // migration-caused, so we do not attribute breakage to the migration.
        if (q.PreexistingError)
            return new ImpactEntry(q.QueryFile, q.EntityMethod, Classification.Safe, new List<ImpactReason>());

        var breaking = new List<ImpactReason>();
        var risky = new List<ImpactReason>();

        foreach (var table in q.Referenced.Tables)
        {
            if (delta.IsTableRemoved(table))
                breaking.Add(new ImpactReason(table, "removed",
                    "table referenced by the query no longer exists"));
        }

        foreach (var col in q.Referenced.Columns)
        {
            var name = $"{col.Table}.{col.Column}";

            // A removed table already produced a breaking reason; a column on it
            // is redundant noise.
            if (delta.IsTableRemoved(col.Table))
                continue;

            if (delta.IsColumnRemoved(col.Table, col.Column))
            {
                breaking.Add(new ImpactReason(name, "removed",
                    "column referenced by the query no longer exists"));
                continue;
            }

            var change = delta.FindColumnChange(col.Table, col.Column);
            if (change != null)
                risky.Add(new ImpactReason(name, PrimaryKind(change), DescribeChange(change)));
        }

        if (q.UnmodeledTouch)
        {
            foreach (var table in q.Referenced.Tables)
                risky.Add(new ImpactReason(table, "unmodeled",
                    "referenced table was changed by a statement the simulator could not model; effective schema may be incomplete"));
        }

        if (breaking.Count > 0)
        {
            // Include risky reasons too for a full picture, breaking first.
            breaking.AddRange(risky);
            return new ImpactEntry(q.QueryFile, q.EntityMethod, Classification.Breaking, breaking);
        }
        if (risky.Count > 0)
            return new ImpactEntry(q.QueryFile, q.EntityMethod, Classification.Risky, risky);

        return new ImpactEntry(q.QueryFile, q.EntityMethod, Classification.Safe, new List<ImpactReason>());
    }

    private static string PrimaryKind(ColumnChange change) => WireKind(change.Kinds[0]);

    private static string WireKind(ColumnChangeKind kind) => kind switch
    {
        ColumnChangeKind.Type => "type",
        ColumnChangeKind.Nullability => "nullability",
        ColumnChangeKind.MaxLength => "maxLength",
        ColumnChangeKind.PrecisionScale => "precisionScale",
        ColumnChangeKind.Unicode => "unicode",
        ColumnChangeKind.PrimaryKey => "primaryKey",
        ColumnChangeKind.Identity => "identity",
        ColumnChangeKind.RowVersion => "rowVersion",
        _ => "changed"
    };

    private static string DescribeChange(ColumnChange change)
    {
        var parts = new List<string>();
        foreach (var kind in change.Kinds)
        {
            switch (kind)
            {
                case ColumnChangeKind.Type:
                    parts.Add($"type {change.Baseline.DbType} → {change.Effective.DbType}");
                    break;
                case ColumnChangeKind.Nullability:
                    parts.Add(change.Effective.IsNullable
                        ? "became nullable"
                        : "became non-nullable");
                    break;
                case ColumnChangeKind.MaxLength:
                    parts.Add($"max length {Fmt(change.Baseline.MaxLength)} → {Fmt(change.Effective.MaxLength)}");
                    break;
                case ColumnChangeKind.PrecisionScale:
                    parts.Add($"precision/scale {Fmt(change.Baseline.Precision)},{Fmt(change.Baseline.Scale)} → {Fmt(change.Effective.Precision)},{Fmt(change.Effective.Scale)}");
                    break;
                case ColumnChangeKind.Unicode:
                    parts.Add("unicode changed");
                    break;
                case ColumnChangeKind.PrimaryKey:
                    parts.Add("primary-key membership changed");
                    break;
                case ColumnChangeKind.Identity:
                    parts.Add("identity changed");
                    break;
                case ColumnChangeKind.RowVersion:
                    parts.Add("rowversion changed");
                    break;
            }
        }
        return string.Join("; ", parts);
    }

    private static string Fmt(int? value) => value?.ToString() ?? "-";
}
