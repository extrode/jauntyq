using Extrode.JauntyQ.Schema.Extraction;

namespace Extrode.JauntyQ.Cli;

/// <summary>
/// <c>jauntyq migrate status</c>: compare the <c>schema_migrations</c> table
/// against <c>db/migrations/*.sql</c> and report what is pending and what has
/// drifted. Read-only and free (decision 005): JauntyQ documents the tracking
/// contract and checks it, and never applies a migration itself.
/// </summary>
public sealed class MigrateStatusVerb : IVerb
{
    public const string DefaultMigrationsDir = "db/migrations";

    public string Name => "migrate status";

    public IReadOnlyList<string> Synopsis { get; } = new[]
    {
        $"  {CliHost.CommandName} migrate status --provider <provider> --connection <connstr> [--migrations <dir>] [--fail-on pending]",
    };

    public IReadOnlyList<string> Help { get; } = new[]
    {
        "  migrate status: compare schema_migrations with db/migrations/*.sql (read-only).",
        "                  Exits 2 on drift (a file applied but still present, or a pending file",
        "                  sorting before the latest applied one), or on any pending file with",
        "                  --fail-on pending.",
    };

    public async Task<int> RunAsync(string[] args, CliHost host)
    {
        var options = new LiveSchemaOptions();
        string migrationsDir = DefaultMigrationsDir;
        bool failOnPending = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provider" when i + 1 < args.Length:
                    options.Provider = args[++i];
                    break;
                case "--connection" when i + 1 < args.Length:
                    options.Connection = args[++i];
                    break;
                case "--connection-env" when i + 1 < args.Length:
                    options.ConnectionEnv = args[++i];
                    break;
                case "--migrations" when i + 1 < args.Length:
                    migrationsDir = args[++i];
                    break;
                case "--fail-on" when i + 1 < args.Length && args[i + 1] == "pending":
                    failOnPending = true;
                    i++;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    host.PrintUsage();
                    return 1;
            }
        }

        if (!Directory.Exists(migrationsDir))
        {
            Console.Error.WriteLine($"Error: migrations directory '{migrationsDir}' does not exist.");
            return 1;
        }

        if (!LiveSchema.TryPrepare(options, host, out _, out string dialect, out string connection, out _))
            return 1;

        var files = new List<string>();
        foreach (var path in Directory.GetFiles(migrationsDir, "*.sql"))
            files.Add(Path.GetFileName(path));

        IReadOnlyList<string>? applied;
        try
        {
            applied = await AppliedMigrations.ReadAsync(dialect, connection);
        }
        catch (Exception ex)
        {
            LiveSchema.ReportFailure("migrate status", ex);
            return 1;
        }

        var status = MigrationStatus.Compute(files, applied);
        status.Write(Console.Out, migrationsDir, files.Count);

        if (status.HasDrift || (failOnPending && status.Pending.Count > 0))
            return 2;
        return 0;
    }
}
