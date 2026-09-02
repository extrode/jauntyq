namespace JauntyQ.Cli;

/// <summary>The verbs every edition of the tool carries. Free, ungated, no licence code involved.</summary>
public static class CoreVerbs
{
    public static IReadOnlyList<IVerb> All => new IVerb[]
    {
        new SchemaPullVerb(),
    };
}
