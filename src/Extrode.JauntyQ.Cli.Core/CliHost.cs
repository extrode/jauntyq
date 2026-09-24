namespace Extrode.JauntyQ.Cli;

/// <summary>
/// The dispatcher both tools run: <c>Extrode.JauntyQ.Cli</c> passes
/// <see cref="CoreVerbs.All"/>, <c>Extrode.JauntyQ.Cli.Premium</c> passes the core list
/// followed by its own. Matching is by verb name against the leading
/// arguments, longest name first, so <c>schema pull</c> wins over a
/// hypothetical <c>schema</c>. A verb this tool does not carry but the premium
/// tool does is answered with the package to install rather than the usage
/// block, since "unknown command" would be the wrong diagnosis.
/// </summary>
public sealed class CliHost
{
    /// <summary>The installed command name, and the one every usage line and hint prints.</summary>
    public const string CommandName = "jauntyq";

    /// <summary>The tool package that carries the premium verbs.</summary>
    public const string PremiumPackageId = "Extrode.JauntyQ.Cli.Premium";

    /// <summary>The feed the premium package is served from.</summary>
    public const string PremiumFeed = "https://nuget.pkg.github.com/extrode/index.json";

    /// <summary>
    /// Exit code for a premium verb that is either not entitled or not installed.
    /// Shared with the license gate so a script can tell "premium refused" from a
    /// usage error (1) or findings (2) without knowing which tool answered.
    /// </summary>
    public const int PremiumOnlyExitCode = 3;

    /// <summary>
    /// The verbs that exist only in the premium tool, as a user would type them.
    /// The free tool consults this to answer them with the install line.
    /// </summary>
    public static readonly IReadOnlyList<string> PremiumVerbNames = new[]
    {
        "schema verify", "usage export", "migrate impact", "explain", "registry", "activate", "license",
    };

    private readonly IReadOnlyList<IVerb> _verbs;

    public CliHost(IReadOnlyList<IVerb> verbs)
    {
        _verbs = verbs;
        for (int i = 1; i < verbs.Count; i++)
            for (int j = 0; j < i; j++)
                if (string.Equals(verbs[i].Name, verbs[j].Name, StringComparison.Ordinal))
                    throw new ArgumentException($"Verb '{verbs[i].Name}' is registered twice.", nameof(verbs));
    }

    public IReadOnlyList<IVerb> Verbs => _verbs;

    /// <summary>True when the premium verbs are part of this tool.</summary>
    public bool HasPremiumVerbs => Find("schema verify") != null;

    public static Task<int> Run(string[] args, IReadOnlyList<IVerb> verbs) => new CliHost(verbs).RunAsync(args);

    public async Task<int> RunAsync(string[] args)
    {
        IVerb? verb = Match(args, out int consumed);
        if (verb != null)
            return await verb.RunAsync(SkipArgs(args, consumed), this);

        string? premium = MatchName(PremiumVerbNames, args, out _);
        if (premium != null && !HasPremiumVerbs)
        {
            PrintPremiumOnly(premium);
            return PremiumOnlyExitCode;
        }

        PrintUsage();
        return 1;
    }

    /// <summary>The registered verb whose name is the longest prefix of <paramref name="args"/>, or null.</summary>
    public IVerb? Match(string[] args, out int consumed)
    {
        var names = new string[_verbs.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = _verbs[i].Name;
        string? name = MatchName(names, args, out consumed);
        return Find(name);
    }

    private IVerb? Find(string? name)
    {
        foreach (var verb in _verbs)
            if (string.Equals(verb.Name, name, StringComparison.Ordinal))
                return verb;
        return null;
    }

    private static string? MatchName(IReadOnlyList<string> names, string[] args, out int consumed)
    {
        string? best = null;
        consumed = 0;
        foreach (var name in names)
        {
            string[] words = name.Split(' ');
            // Stryker disable once Equality : "<" differs only for a name with as many words as the current best, and two distinct names of equal length cannot both match the same leading arguments
            if (words.Length <= consumed)
                continue;
            if (args.Length < words.Length)
                continue;
            bool matches = true;
            for (int i = 0; i < words.Length && matches; i++)
                matches = string.Equals(args[i], words[i], StringComparison.Ordinal);
            if (matches)
            {
                best = name;
                consumed = words.Length;
            }
        }
        return best;
    }

    /// <summary>
    /// Manual replacement for args.Skip(count).ToArray(): product code stays
    /// System.Linq-free for NativeAOT compatibility.
    /// </summary>
    internal static string[] SkipArgs(string[] args, int count)
    {
        // Stryker disable once Equality : ">" differs only at count == args.Length, where the copy path returns a fresh empty array instead of Array.Empty, observable only by reference identity
        if (count >= args.Length)
            return Array.Empty<string>();
        var result = new string[args.Length - count];
        Array.Copy(args, count, result, 0, result.Length);
        return result;
    }

    /// <summary>The reply to a premium verb typed at the free tool: what it is and how to get it.</summary>
    public static void PrintPremiumOnly(string verbName)
    {
        Console.Error.WriteLine($"Error: '{CommandName} {verbName}' is part of {PremiumPackageId}, which is not installed.");
        Console.Error.WriteLine($"It replaces this tool and keeps every free verb. Install it with an entitling license:");
        Console.Error.WriteLine($"  dotnet tool install --global {PremiumPackageId} --add-source {PremiumFeed}");
        Console.Error.WriteLine("Pricing and licenses: https://extrode.com/jauntyq/pricing");
    }

    public void PrintUsage() => PrintUsage(Console.Out);

    public void PrintUsage(TextWriter output)
    {
        output.WriteLine(HasPremiumVerbs
            ? $"JauntyQ CLI ({PremiumPackageId}): schema extraction, contract testing and analysis"
            : "JauntyQ CLI (Extrode.JauntyQ.Cli): schema extraction tool");
        output.WriteLine();
        output.WriteLine("Usage:");
        foreach (var verb in _verbs)
            foreach (var line in verb.Synopsis)
                output.WriteLine(line);
        output.WriteLine();
        foreach (var verb in _verbs)
            foreach (var line in verb.Help)
                output.WriteLine(line);
        output.WriteLine();
        if (HasPremiumVerbs)
        {
            output.WriteLine("  Premium commands require an entitling license; without one they exit 3:");
            output.WriteLine("    migrate impact, schema verify (and schema verify --service), explain,");
            output.WriteLine("    registry, usage export.");
            output.WriteLine("  Everything else is free and ungated, including schema pull, the source generator");
            output.WriteLine("  and every JNTxxxx build diagnostic.");
            output.WriteLine("  A lapsed license still runs them (with a warning).");
            output.WriteLine("  Set JAUNTYQ_LICENSE to a license path for CI instead of running 'activate'.");
        }
        else
        {
            output.WriteLine($"  Premium commands require {PremiumPackageId} and an entitling license:");
            output.WriteLine("    migrate impact, schema verify (and schema verify --service), explain,");
            output.WriteLine("    registry, usage export. This tool answers them with exit 3 and the");
            output.WriteLine("    install line:");
            output.WriteLine($"    dotnet tool install --global {PremiumPackageId} --add-source {PremiumFeed}");
            output.WriteLine("  Everything else is free and ungated, including schema pull, the source generator");
            output.WriteLine("  and every JNTxxxx build diagnostic.");
        }
        output.WriteLine();
        output.WriteLine("Providers:");
        output.WriteLine("  postgres    PostgreSQL (Npgsql)");
        output.WriteLine("  sqlserver   SQL Server (Microsoft.Data.SqlClient)");
        output.WriteLine("  mysql       MySQL (MySqlConnector)");
        output.WriteLine("  mariadb     MariaDB (MySqlConnector; snapshot declares the 'mysql' dialect)");
        output.WriteLine("  sqlite      SQLite (Microsoft.Data.Sqlite)");
        output.WriteLine();
        output.WriteLine("Connection (most secure first):");
        output.WriteLine("  --connection-env <VAR>  Read the connection string from environment variable VAR");
        output.WriteLine($"  {CliEnvironment.ConnectionEnvVar}      Default env var used when neither flag is given");
        output.WriteLine("  --connection <connstr>  Inline (avoid: visible in process list, shell history, CI logs)");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine($"  --output    Output path, relative to the current directory (default: {LiveSchemaOptions.DefaultOutput})");
        output.WriteLine($"  {CliEnvironment.VerboseEnvVar}={CliEnvironment.VerboseOptIn}  Print full error detail on failure");
    }
}
