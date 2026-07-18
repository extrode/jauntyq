using System;

namespace JauntyQ.Analysis;

/// <summary>
/// The single canonical algorithm for deriving a .sql query file's entity name
/// from its path, shared by the Roslyn generator (JauntyQGenerator, via a thin
/// forwarding wrapper in Part5.cs so its own registered regression tests stay
/// unedited) and the CLI (MigrateImpact.ImpactCommand, Usage.UsageManifestBuilder).
///
/// Until audit round 61 the CLI carried its own, simpler, independently
/// maintained copy (bare immediate-parent-folder-name) that only coincidentally
/// agreed with this one for every one-folder-deep sample layout in the repo.
/// It silently diverged for a file nested more than one folder below its entity,
/// and separately mishandled a configured root passed with a trailing separator
/// combined with a root-level file (AUD-R61-01; the latter was independently
/// reachable today, not merely a latent deep-nesting edge case). Both CLI call
/// sites now normalize their paths with <see cref="System.IO.Path.GetFullPath(string)"/>
/// (mirroring what the old duplicated code already did) and call this method
/// directly instead of re-deriving the convention themselves.
/// </summary>
public static class EntityNameResolver
{
    /// <summary>
    /// Entity name = the first path segment after stripping <paramref name="commonPrefix"/>
    /// and skipping a leading "tables"/"views" structural folder if present.
    /// Files directly in the root (no entity subfolder) get the catch-all
    /// entity name "Queries".
    /// </summary>
    public static string ExtractEntityName(string filePath, string commonPrefix)
    {
        // Normalize separators
        string normalized = filePath.Replace('\\', '/');
        string normalizedPrefix = commonPrefix.Replace('\\', '/');

        // Get the relative path after the common prefix
        string relative;
        if (normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = normalized.Substring(normalizedPrefix.Length);
        }
        else
        {
            // Fallback: just use the filename
            relative = System.IO.Path.GetFileName(filePath);
        }

        // Trim leading separators
        relative = relative.TrimStart('/');

        // Split into segments
        var segments = relative.Split('/');

        // Skip structural prefixes (tables/, views/)
        int entityIndex = 0;
        if (segments.Length >= 3 &&
            (string.Equals(segments[0], "tables", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "views", StringComparison.OrdinalIgnoreCase)))
        {
            entityIndex = 1;
        }

        if (segments.Length >= entityIndex + 2)
        {
            // Has an entity subfolder
            return segments[entityIndex];
        }

        // No subfolder — catch-all
        return "Queries";
    }
}
