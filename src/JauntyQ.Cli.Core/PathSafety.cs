namespace JauntyQ.Cli;

/// <summary>
/// Shared CWE-22 guard for CLI-written output paths: rejects rooted paths and
/// <c>..</c> escapes so a stray or malicious argument can never write outside
/// the current working directory's tree. Originally implemented only inside
/// <c>Program.TryResolveOutputPath</c> for <c>schema pull</c>/<c>verify --output</c>
/// (M2); <c>jauntyq usage export --output</c> (feature 012) turned out to be an
/// unguarded sibling — see AUD-R22-01. Centralizing here so a future
/// output-writing verb picks this up automatically instead of needing its own
/// from-scratch reimplementation (and its own chance to forget the guard).
/// </summary>
internal static class PathSafety
{
    /// <summary>
    /// Resolves <paramref name="input"/> relative to the current working
    /// directory and confirms the resolved path stays inside that directory's
    /// tree. <paramref name="argName"/> (e.g. <c>"--output"</c>) is used only
    /// to phrase <paramref name="error"/>.
    /// </summary>
    public static bool TryResolveWithinCurrentDirectory(string input, string argName, out string resolved, out string? error)
    {
        resolved = "";
        error = null;

        if (Path.IsPathRooted(input))
        {
            error = $"{argName} must be a relative path inside the project (got rooted path '{input}').";
            return false;
        }

        string root = Path.GetFullPath(Directory.GetCurrentDirectory());
        string candidate = Path.GetFullPath(Path.Combine(root, input));

        string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            error = $"{argName} '{input}' escapes the project directory.";
            return false;
        }

        resolved = candidate;
        return true;
    }
}
