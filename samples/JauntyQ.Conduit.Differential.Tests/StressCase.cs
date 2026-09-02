namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// One query written to provoke disagreement, with the grouping of engines it
/// is expected to produce.
///
/// The declared grouping is the assertion. An engine that moves out of its
/// group fails the suite, and so does an engine that unexpectedly joins one:
/// a stress case that stops stressing is as much a change in behavior as one
/// that starts.
/// </summary>
public sealed record StressCase
{
    public required string Name { get; init; }

    public required string Category { get; init; }

    /// <summary>SQL for every engine without an override.</summary>
    public required string Sql { get; init; }

    /// <summary>
    /// Per-engine SQL, for the cases where the construct under test has no
    /// portable spelling -- string concatenation and the boolean literal. An
    /// override replaces <see cref="Sql"/> for that engine only.
    /// </summary>
    public IReadOnlyDictionary<string, string> SqlByEngine { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// True when the case makes an ordering claim, in which case row order is
    /// part of the result. The NULL-ordering cases are the reason this exists.
    /// </summary>
    public bool Ordered { get; init; }

    /// <summary>
    /// The expected partition of engine names. Measured by the discovery pass,
    /// never guessed -- see 018-plan.md.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<string>> Groups { get; init; }

    /// <summary>Why the engines differ, in terms of documented behavior.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// What a consumer should write instead, or an explicit statement that no
    /// portable form exists. R7 -- a matrix that names a difference without
    /// saying what to do about it leaves the reader where it found them.
    /// </summary>
    public required string Guidance { get; init; }

    public string SqlFor(string engine) =>
        SqlByEngine.TryGetValue(engine, out string? overridden) ? overridden : Sql;

    public override string ToString() => $"{Category}/{Name}";
}
