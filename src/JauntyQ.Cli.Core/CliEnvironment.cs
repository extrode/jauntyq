namespace JauntyQ.Cli;

/// <summary>
/// Environment variables the CLI reads, named once.
///
/// AUD-R88-01: <c>JAUNTYQ_VERBOSE</c> was spelled as a bare literal at three
/// unrelated read sites (schema pull/verify, usage export, migrate impact),
/// each comparing against a second bare literal <c>"1"</c>. Its sibling
/// <c>JAUNTYQ_LICENSE</c> has had <c>LicenseConstants.EnvVar</c>
/// since it was introduced. The asymmetry matters more than it looks:
/// <see cref="System.Environment.GetEnvironmentVariable(string)"/> is
/// case-insensitive on Windows and case-sensitive on Linux and macOS, so a
/// casing divergence introduced at one of the three sites passes the entire
/// Windows suite and silently disables verbose output on the platforms most
/// likely to be running CI. One definition cannot diverge from itself.
/// </summary>
internal static class CliEnvironment
{
    /// <summary>Set to <see cref="VerboseOptIn"/> to print full exception detail on failure.</summary>
    internal const string VerboseEnvVar = "JAUNTYQ_VERBOSE";

    /// <summary>
    /// The connection-string variable consulted when <c>--connection-env</c> is
    /// not given. Named here for the same reason as <see cref="VerboseEnvVar"/>,
    /// and with more at stake: a casing divergence in this one does not disable
    /// a diagnostic, it makes `schema pull` and `explain` disagree about where
    /// the credential lives, on Linux and macOS only.
    /// </summary>
    internal const string ConnectionEnvVar = "JAUNTYQ_CONNECTION";

    /// <summary>The only value of <see cref="VerboseEnvVar"/> that opts in.</summary>
    internal const string VerboseOptIn = "1";

    /// <summary>
    /// True when the caller has opted into full exception detail.
    ///
    /// Deliberately exact and ordinal, preserving the behaviour all three
    /// original sites had: <c>"true"</c>, <c>"yes"</c>, <c>"0"</c> and
    /// <c>" 1"</c> are all off. The variable gates whether provider exception
    /// messages — which can echo host and credential fragments out of a
    /// connection string (CWE-532) — reach the log, so a permissive reading
    /// that guessed at intent would be the wrong direction to be generous in.
    /// </summary>
    /// <summary>
    /// The <c>--connection-env</c>-overrides-the-default rule, held once.
    ///
    /// AUD-R89-01: `schema pull/verify` and `explain` each carried their own
    /// copy of this two-line decision, which is the same shape of duplication
    /// AUD-R26-01 turned into a real divergence in the dialect aliases. The two
    /// verbs must agree on which variable holds the credential — a user who
    /// exports one variable and runs both would otherwise be told by one verb
    /// that no connection is configured.
    ///
    /// Returns null when neither the named variable nor the default is set;
    /// each caller decides what that means, and they legitimately differ
    /// (`schema verify` errors, `explain` exits 0 with a note).
    /// </summary>
    internal static string? ReadConnectionString(string? connectionEnv) =>
        System.Environment.GetEnvironmentVariable(
            string.IsNullOrEmpty(connectionEnv) ? ConnectionEnvVar : connectionEnv!);

    internal static bool VerboseErrors =>
        string.Equals(
            System.Environment.GetEnvironmentVariable(VerboseEnvVar),
            VerboseOptIn,
            System.StringComparison.Ordinal);
}
