using JauntyQ.Schema;

namespace JauntyQ.Cli;

/// <summary>
/// <c>jauntyq schema pull</c>: reflect the live database into the committed
/// snapshot. Free in both tools and implemented once, here.
/// </summary>
public sealed class SchemaPullVerb : IVerb
{
    public string Name => "schema pull";

    public IReadOnlyList<string> Synopsis { get; } = new[]
    {
        $"  {CliHost.CommandName} schema pull   --provider <provider> --connection <connstr> [--output <path>]",
    };

    public IReadOnlyList<string> Help { get; } = new[]
    {
        "  schema pull:    reflect the live database into the snapshot at --output",
        "                  (tables, columns, indexes, foreign keys, procedures, sequences).",
    };

    public async Task<int> RunAsync(string[] args, CliHost host)
    {
        var options = new LiveSchemaOptions();
        for (int i = 0; i < args.Length; i++)
        {
            if (options.TryConsume(args, ref i))
                continue;
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            host.PrintUsage();
            return 1;
        }

        if (!LiveSchema.TryPrepare(options, host, out var extractor, out string dialect, out string connection, out string output))
            return 1;

        try
        {
            var schema = await LiveSchema.ExtractAsync(
                extractor, connection, options.Provider!, dialect, LiveSchema.ProgressStream(verify: false));

            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(output, SchemaLoader.Serialize(schema));
            Console.WriteLine($"Schema written to {output}");
            return 0;
        }
        catch (Exception ex)
        {
            LiveSchema.ReportFailure("schema pull", ex);
            return 1;
        }
    }
}
