using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;
public partial class JauntyQGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Computes the common directory prefix across all SQL file paths.
    /// This identifies the root SQL folder so we can extract entity subfolder names.
    /// Uses the grandparent directory of each file (parent-of-parent) to avoid
    /// swallowing the entity subfolder when all files share the same entity.
    /// </summary>
    internal static string ComputeCommonDirectoryPrefix(ImmutableArray<string> paths)
    {
        if (paths.IsEmpty)
            return "";

        // Collect grandparent directories (go up 2 levels from the file).
        // For files like "db/Products/GetAll.sql", grandparent = "db".
        // For root-level files like "db/GetOrphaned.sql", grandparent = "" (empty).
        // We use grandparents so the common prefix doesn't include the entity folder.
        var dirs = new System.Collections.Generic.List<string>();
        foreach (var path in paths)
        {
            string dir = (System.IO.Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
            string grandparent = (System.IO.Path.GetDirectoryName(dir) ?? "").Replace('\\', '/');
            dirs.Add(grandparent);
        }

        // Filter to non-empty grandparents (files with entity subfolders).
        // Root-level files (grandparent empty) shouldn't affect the common prefix.
        var nonEmpty = new System.Collections.Generic.List<string>();
        foreach (var d in dirs)
        {
            if (d.Length > 0)
                nonEmpty.Add(d);
        }

        if (nonEmpty.Count == 0)
        {
            // ALL files are at most 1 folder deep — use the direct parent as root
            string firstDir = (System.IO.Path.GetDirectoryName(paths[0]) ?? "").Replace('\\', '/');
            if (firstDir.Length > 0 && !firstDir.EndsWith("/"))
                firstDir += "/";
            return firstDir;
        }

        // Compute common prefix across non-empty grandparent directories
        string prefix = nonEmpty[0];
        for (int i = 1; i < nonEmpty.Count; i++)
        {
            string dir = nonEmpty[i];
            int len = System.Math.Min(prefix.Length, dir.Length);
            int matchEnd = 0;
            for (int j = 0; j < len; j++)
            {
                if (char.ToLowerInvariant(prefix[j]) != char.ToLowerInvariant(dir[j]))
                    break;
                if (prefix[j] == '/')
                    matchEnd = j + 1;
                if (j == len - 1)
                    matchEnd = len;
            }
            prefix = prefix.Substring(0, matchEnd);
        }

        // Ensure prefix ends with separator
        if (prefix.Length > 0 && !prefix.EndsWith("/"))
            prefix += "/";

        return prefix;
    }
}
