using JauntyQ.Schema;
using JauntyQ.Schema.Extraction;

namespace JauntyQ.Cli;

/// <summary>
/// The arguments <c>schema pull</c> and <c>schema verify</c> share: where the
/// database is and where the snapshot goes. Held once so the two verbs, which
/// live in different packages since the split, cannot disagree about how a
/// credential is resolved or an output path is guarded.
/// </summary>
public sealed class LiveSchemaOptions
{
    public const string DefaultOutput = "schema/jaunty.schema.json";

    public string? Provider { get; set; }
    public string? Connection { get; set; }
    public string? ConnectionEnv { get; set; }
    public string Output { get; set; } = DefaultOutput;

    /// <summary>
    /// Consumes <c>args[i]</c> (and its value) when it is one of the shared
    /// flags, leaving <paramref name="i"/> on the last token taken. A verb's own
    /// switch calls this first and handles its own flags in the arms that follow.
    /// </summary>
    public bool TryConsume(string[] args, ref int i)
    {
        switch (args[i])
        {
            case "--provider" when i + 1 < args.Length:
                Provider = args[++i];
                return true;
            case "--connection" when i + 1 < args.Length:
                Connection = args[++i];
                return true;
            case "--connection-env" when i + 1 < args.Length:
                ConnectionEnv = args[++i];
                return true;
            case "--output" when i + 1 < args.Length:
                Output = args[++i];
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// The extraction half both live verbs run: validate the shared options,
/// choose the extractor, reflect the database. Errors are printed to
/// <see cref="Console.Error"/> here because the wording is part of the verb's
/// contract and the two verbs must print the same words.
/// </summary>
public static class LiveSchema
{
    /// <summary>
    /// Resolves the connection string, guards the output path and picks the
    /// extractor for the provider. Returns false after printing the reason;
    /// the caller exits 1. <paramref name="host"/> supplies the usage block the
    /// missing-provider and missing-connection errors are followed by.
    /// </summary>
    public static bool TryPrepare(
        LiveSchemaOptions options, CliHost host,
        out ISchemaExtractor extractor, out string dialect, out string connection, out string output)
    {
        extractor = null!;
        dialect = "";
        connection = "";
        output = "";

        if (string.IsNullOrEmpty(options.Provider))
        {
            Console.Error.WriteLine("Error: --provider is required");
            host.PrintUsage();
            return false;
        }

        // Connection string resolution, most secure first. Passing the secret
        // on the command line exposes it via process listings, shell history
        // and CI logs (CWE-214), so an env var is the recommended path:
        //   --connection-env MYVAR, else JAUNTYQ_CONNECTION, else --connection.
        string? resolvedConnection = options.Connection;
        if (string.IsNullOrEmpty(resolvedConnection))
            resolvedConnection = CliEnvironment.ReadConnectionString(options.ConnectionEnv);

        if (string.IsNullOrEmpty(resolvedConnection))
        {
            Console.Error.WriteLine($"Error: no connection string. Set it in an environment variable (recommended) and pass --connection-env <VAR>, set {CliEnvironment.ConnectionEnvVar}, or use --connection <connstr>.");
            host.PrintUsage();
            return false;
        }

        // M2 (CWE-22): --output must stay within the current working directory
        // tree so a stray '..' can't write outside the project.
        if (!PathSafety.TryResolveWithinCurrentDirectory(options.Output, "--output", out string resolvedOutput, out string? pathError))
        {
            Console.Error.WriteLine($"Error: {pathError}");
            return false;
        }

        string? resolvedDialect = DialectAliases.Resolve(options.Provider!);
        if (resolvedDialect == null)
        {
            Console.Error.WriteLine(DialectAliases.UnknownProviderMessage(options.Provider!));
            return false;
        }

        extractor = resolvedDialect switch
        {
            "postgres" => new PostgresExtractor(),
            "sqlserver" => new SqlServerExtractor(),
            "mysql" => new MySqlExtractor(),
            "sqlite" => new SqliteExtractor(),
            // Named rather than a catch-all: the alias switch and this one are
            // two halves of the same mapping with nothing relating them, and
            // they have already drifted once (AUD-R26-01). A discard arm would
            // run SQLite catalog SQL against a newly-added engine and surface
            // it as an opaque SqliteException.
            _ => throw new ArgumentOutOfRangeException(
                nameof(options), resolvedDialect, "No extractor is wired for this dialect.")
        };
        dialect = resolvedDialect;
        connection = resolvedConnection!;
        output = resolvedOutput;
        return true;
    }

    /// <summary>Reflects the database, announcing progress on <paramref name="progress"/>.</summary>
    public static async Task<DatabaseSchema> ExtractAsync(
        ISchemaExtractor extractor, string connection, string provider, string dialect, TextWriter progress)
    {
        progress.WriteLine($"Connecting to {provider}...");
        var schema = await extractor.ExtractAsync(connection);
        schema.Dialect = dialect;
        progress.WriteLine($"Extracted {schema.Tables.Count} table(s), {schema.ForeignKeys.Count} foreign key(s)");
        return schema;
    }

    /// <summary>
    /// AUD-R86-04: the stream the progress notices go to. <c>verify</c> writes a
    /// report to stdout, a JSON document under --format json, so its notices
    /// belong on stderr; <c>pull</c> has no document, and there the same lines
    /// are the result the user came for. One home and one test.
    /// </summary>
    public static TextWriter ProgressStream(bool verify) => verify ? Console.Error : Console.Out;

    /// <summary>
    /// M1 (CWE-532): provider exceptions can echo host/credential fragments
    /// from the connection string. Report the failure without spilling the raw
    /// message into logs; JAUNTYQ_VERBOSE=1 opts into the full detail.
    /// </summary>
    public static void ReportFailure(string verbName, Exception ex)
    {
        if (CliEnvironment.VerboseErrors)
            Console.Error.WriteLine($"Error: {verbName} failed: {ex.Message}");
        else
            Console.Error.WriteLine($"Error: {verbName} failed ({ex.GetType().Name}). Set {CliEnvironment.VerboseEnvVar}={CliEnvironment.VerboseOptIn} for detail.");
    }
}
