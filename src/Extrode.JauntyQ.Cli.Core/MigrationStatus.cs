using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema.Extraction;

namespace Extrode.JauntyQ.Cli;

/// <summary>
/// What <c>jauntyq migrate status</c> found: the files in <c>db/migrations/</c>
/// set against the rows in <c>schema_migrations</c>.
/// </summary>
/// <param name="Pending">Files with no row, in the order a runner must apply them.</param>
/// <param name="AppliedStillPresent">
/// Files that already have a row. The generator applies every file in the folder on top of
/// the snapshot, so a deployed migration left there is applied twice at build time.
/// </param>
/// <param name="OutOfOrder">
/// Pending files that sort before <paramref name="LatestApplied"/>. A runner applies them
/// after migrations they come before, so the database ends up built in a different order
/// from the one the generator simulates.
/// </param>
/// <param name="LatestApplied">The recorded version that sorts last, or null when none is recorded.</param>
/// <param name="RecordedCount">Rows in <c>schema_migrations</c>, duplicates included; 0 when the table is missing.</param>
/// <param name="TableExists">False when the database has no <c>schema_migrations</c> table.</param>
public sealed record MigrationStatus(
    IReadOnlyList<string> Pending,
    IReadOnlyList<string> AppliedStillPresent,
    IReadOnlyList<string> OutOfOrder,
    string? LatestApplied,
    int RecordedCount,
    bool TableExists)
{
    /// <summary>True when a file is applied twice at build time or a runner would apply one out of order.</summary>
    public bool HasDrift => AppliedStillPresent.Count > 0 || OutOfOrder.Count > 0;

    /// <summary>
    /// Compares the migration file names (with <c>.sql</c>) against the recorded versions,
    /// or against nothing when <paramref name="applied"/> is null (no table). Names compare
    /// ordinally, because the contract keys each row by the file name exactly.
    /// </summary>
    public static MigrationStatus Compute(IReadOnlyList<string> files, IReadOnlyList<string>? applied)
    {
        var recorded = new HashSet<string>(applied ?? Array.Empty<string>(), StringComparer.Ordinal);

        string? latest = null;
        foreach (var version in recorded)
            if (latest == null || MigrationOrder.Compare(version, latest) > 0)
                latest = version;

        var ordered = new List<string>(files);
        ordered.Sort(MigrationOrder.Compare);

        var pending = new List<string>();
        var stillPresent = new List<string>();
        var outOfOrder = new List<string>();
        foreach (var file in ordered)
        {
            if (recorded.Contains(file))
            {
                stillPresent.Add(file);
                continue;
            }
            pending.Add(file);
            if (latest != null && MigrationOrder.Compare(file, latest) < 0)
                outOfOrder.Add(file);
        }

        return new MigrationStatus(pending, stillPresent, outOfOrder, latest, applied?.Count ?? 0, applied != null);
    }

    /// <summary>The report <c>migrate status</c> prints, one line per entry.</summary>
    public void Write(TextWriter output, string migrationsDir, int fileCount)
    {
        output.WriteLine($"Migration files in {migrationsDir}: {fileCount}");
        output.WriteLine(TableExists
            ? $"Recorded in {AppliedMigrations.TableName}: {RecordedCount}"
            : $"No {AppliedMigrations.TableName} table: nothing is recorded as applied.");

        WriteList(output, $"Pending, in apply order ({Pending.Count}):", Pending);
        WriteList(output,
            $"Applied but still in {migrationsDir} ({AppliedStillPresent.Count}). The build applies these again; re-pull the snapshot, then move them out:",
            AppliedStillPresent);
        WriteList(output,
            $"Out of order ({OutOfOrder.Count}). These sort before {LatestApplied}, the latest applied migration, so a runner applies them after migrations they come before:",
            OutOfOrder);

        if (Pending.Count == 0 && !HasDrift)
        {
            output.WriteLine();
            output.WriteLine("Up to date: nothing pending.");
        }
    }

    private static void WriteList(TextWriter output, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;
        output.WriteLine();
        output.WriteLine(heading);
        foreach (var item in items)
            output.WriteLine($"  {item}");
    }
}
