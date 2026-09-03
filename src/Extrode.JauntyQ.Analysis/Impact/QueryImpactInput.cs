namespace Extrode.JauntyQ.Analysis.Impact;

/// <summary>
/// One query fed to the <see cref="ImpactClassifier"/>. Carries the resolved
/// referenced objects plus two caller-computed flags: <see cref="PreexistingError"/>
/// (already invalid against the baseline, so any breakage is not migration-caused)
/// and <see cref="UnmodeledTouch"/> (references a table changed by a statement the
/// simulator could not model — JNT9001 — so the effective schema may be incomplete).
/// </summary>
public sealed class QueryImpactInput
{
    public string QueryFile { get; }
    public string EntityMethod { get; }
    public ReferencedObjects Referenced { get; }
    public bool PreexistingError { get; }
    public bool UnmodeledTouch { get; }

    public QueryImpactInput(
        string queryFile,
        string entityMethod,
        ReferencedObjects referenced,
        bool preexistingError = false,
        bool unmodeledTouch = false)
    {
        QueryFile = queryFile;
        EntityMethod = entityMethod;
        Referenced = referenced;
        PreexistingError = preexistingError;
        UnmodeledTouch = unmodeledTouch;
    }
}
