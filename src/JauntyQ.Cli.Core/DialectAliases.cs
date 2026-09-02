namespace JauntyQ.Cli;

/// <summary>
/// Maps a user-facing <c>--provider</c> value to the canonical dialect id a
/// snapshot records and every dialect switch downstream compares against.
///
/// AUD-R88-03: this was two separately-maintained switches — one in
/// <c>Program.RunSchemaAsync</c>, one in <c>ExplainCli.RunAsync</c> — with
/// nothing relating them. They had already drifted once: the <c>mariadb</c>
/// alias was added to the first (commit 5a40fb1) without being propagated to
/// the second, so <c>jauntyq explain --provider mariadb</c> was rejected
/// outright while <c>jauntyq schema pull --provider mariadb</c> worked
/// (AUD-R26-01). A parity test over two copies would have caught the drift;
/// one copy cannot drift at all, which is the stronger fix, so the test that
/// accompanies this asserts agreement with the surfaces that genuinely remain
/// separate rather than with a sibling that no longer exists.
///
/// MariaDB is deliberately an alias rather than a dialect of its own: it is
/// wire/SQL-compatible with MySQL for everything JauntyQ emits and for
/// EXPLAIN FORMAT=JSON alike, so a MariaDB snapshot declares
/// <c>"dialect": "mysql"</c> (matching JNT7003's guidance).
/// </summary>
internal static class DialectAliases
{
    /// <summary>
    /// The <c>--provider</c> spellings accepted, listed for error messages in
    /// the order users are most likely to reach for. Not the canonical dialect
    /// set: <c>mariadb</c> and <c>postgresql</c> and <c>mssql</c> are aliases,
    /// and <c>mariadb</c> is the one this list names because it is the alias a
    /// user would not otherwise guess is supported.
    /// </summary>
    internal const string SupportedProviders = "postgres, sqlserver, mysql, mariadb, sqlite";

    /// <summary>
    /// The canonical dialect id for <paramref name="provider"/>, or null when
    /// no alias matches. Callers report the failure themselves so each verb
    /// keeps its own exit code and stream.
    /// </summary>
    internal static string? Resolve(string provider) => provider.ToLowerInvariant() switch
    {
        "postgres" or "postgresql" => "postgres",
        "sqlserver" or "mssql" => "sqlserver",
        "mysql" or "mariadb" => "mysql",
        "sqlite" => "sqlite",
        _ => null
    };

    /// <summary>The message every verb prints for an unresolvable provider.</summary>
    internal static string UnknownProviderMessage(string provider) =>
        $"Error: unknown provider '{provider}'. Supported: {SupportedProviders}.";
}
