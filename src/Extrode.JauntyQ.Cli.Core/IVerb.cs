namespace Extrode.JauntyQ.Cli;

/// <summary>
/// One command the <c>jauntyq</c> tool dispatches to. <see cref="Name"/> is the
/// verb as the user types it, one or two words (<c>explain</c>,
/// <c>schema pull</c>); <see cref="CliHost"/> matches the longest name that
/// prefixes the arguments and hands the verb the rest.
/// </summary>
public interface IVerb
{
    /// <summary>The verb as typed, e.g. <c>"schema pull"</c>. Unique within a tool.</summary>
    string Name { get; }

    /// <summary>Lines printed under <c>Usage:</c>, already indented, e.g. <c>"  jauntyq schema pull ..."</c>.</summary>
    IReadOnlyList<string> Synopsis { get; }

    /// <summary>Lines printed in the description block below the synopsis; may be empty.</summary>
    IReadOnlyList<string> Help { get; }

    /// <summary>Runs the verb with the arguments after its name. Returns the process exit code.</summary>
    Task<int> RunAsync(string[] args, CliHost host);
}
