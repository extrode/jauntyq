using System;
using System.Collections.Generic;
using System.Text;

namespace JauntyQ.Generator.Directives;

public static class DirectiveParser
{
    /// <summary>
    /// Parses directive comments from raw SQL text and returns the directive model
    /// plus the cleaned SQL (with directive lines removed).
    /// Directives are lines matching: -- @directive value
    /// </summary>
    public static (DirectiveModel directives, string cleanedSql) Parse(string rawSql)
    {
        var directives = new DirectiveModel();
        var cleanedLines = new List<string>();

        var lines = rawSql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("--"))
            {
                // AUD-R79-01: Trim, not TrimStart. `line.TrimStart()` above
                // leaves trailing whitespace on the line, and the three
                // no-value directives matched the whole comment body -- so
                // "-- @first " with one trailing space (ordinary in a
                // hand-edited .sql file) matched nothing, was left in the SQL
                // as a comment, and set no flag. JNT3008 did not catch it
                // either: the word IS a known directive, so IsOneEditAway's
                // a == b guard returns false and no near-miss message is
                // produced. Neither applied nor diagnosed is exactly the
                // silent-ignore the @each and @call gates in
                // JauntyQGenerator.Part2.cs exist to prevent.
                //
                // Trimming the tail also routes the value-taking directives'
                // bare-but-padded forms ("-- @result   ") to the suspicious
                // check, rather than handing ParseResultDirective an empty
                // value and setting an empty ResultTypeName.
                var commentBody = trimmed.Substring(2).Trim();

                if (!commentBody.StartsWith("@"))
                {
                    cleanedLines.Add(line);
                    continue;
                }

                // The name token is '@' plus the letters that follow it, and
                // the value is everything after that. Splitting once, here,
                // replaces six literal "@name " prefix matches, each of which
                // required the separator to be a SPACE: "-- @type<TAB>total
                // decimal" matched nothing, so the line fell through to
                // CheckSuspiciousDirective and reported JNT3008 "-- @type
                // requires a value" -- accurate that the directive was not
                // applied, inaccurate that no value was present. The author is
                // then told to write the line they already wrote, which is the
                // failure AUD-R79-03 named one level down (a tab INSIDE
                // @type's value) and left here deliberately, because fixing it
                // meant reshaping all six matches rather than widening that
                // round's fix.
                //
                // The name must be terminated by whitespace or end-of-body.
                // That is what keeps "-- @proceed with caution" an ordinary
                // comment rather than @proc with the name "eed with caution"
                // (AUD-R8-01's word-boundary rule, now structural instead of a
                // consequence of prefix ordering) and "-- @type(x)" unmatched,
                // exactly as before.
                int nameEnd = 1;
                while (nameEnd < commentBody.Length && char.IsLetter(commentBody[nameEnd]))
                    nameEnd++;

                if (nameEnd > 1
                    && (nameEnd == commentBody.Length || char.IsWhiteSpace(commentBody[nameEnd]))
                    && TryApplyDirective(
                        directives,
                        commentBody.Substring(1, nameEnd - 1).ToLowerInvariant(),
                        commentBody.Substring(nameEnd).Trim()))
                {
                    continue; // strip this line from cleaned SQL
                }

                // Nothing applied: the line stays a plain comment. Before it
                // does, check for a near-miss the author probably meant as a
                // directive (JNT3008, surfaced by the generator).
                CheckSuspiciousDirective(directives, commentBody);
            }

            cleanedLines.Add(line);
        }

        var cleanedSql = string.Join("\n", cleanedLines);
        return (directives, cleanedSql);
    }

    /// <summary>
    /// Applies one directive by name, returning false when the line is not a
    /// directive after all — in which case the caller keeps it as a plain
    /// comment and runs the near-miss check.
    ///
    /// The value/no-value split is the pre-split behaviour restated: the four
    /// value-taking directives and <c>@call</c> did nothing without a value
    /// (AUD-R79-02 removed the bare-<c>@call</c> acceptance, since with no name
    /// <c>CallProcName</c> stays null and the file is emitted as though the
    /// directive were absent), <c>@proc</c> alone is meaningful because the
    /// procedure name is synthesized from entity + method, and the three
    /// no-value directives matched the whole comment body — so a trailing word
    /// makes the line an ordinary comment rather than a directive with junk.
    /// </summary>
    private static bool TryApplyDirective(DirectiveModel directives, string name, string value)
    {
        switch (name)
        {
            case "result":
                if (value.Length == 0)
                    return false;
                ParseResultDirective(directives, value);
                return true;

            case "params":
                if (value.Length == 0)
                    return false;
                ParseParamsDirective(directives, value);
                return true;

            case "type":
                if (value.Length == 0)
                    return false;
                ParseTypeDirective(directives, value);
                return true;

            case "each":
                if (value.Length == 0)
                    return false;
                ParseEachDirective(directives, value);
                return true;

            case "call":
                if (value.Length == 0)
                    return false;
                directives.CallProcName = value;
                return true;

            case "proc":
                directives.IsProc = true;
                if (value.Length > 0)
                    directives.ProcName = value;
                return true;

            case "first":
                if (value.Length > 0)
                    return false;
                directives.IsFirst = true;
                return true;

            case "identity":
                if (value.Length > 0)
                    return false;
                directives.ReturnsIdentity = true;
                return true;

            case "stream":
                if (value.Length > 0)
                    return false;
                directives.IsStream = true;
                return true;

            default:
                return false;
        }
    }

    private static void ParseResultDirective(DirectiveModel directives, string value)
    {
        if (string.Equals(value, "void", StringComparison.OrdinalIgnoreCase))
        {
            directives.ResultIsVoid = true;
            return;
        }

        if (value.StartsWith("(") && value.EndsWith(")"))
        {
            // Inline column definitions: (int Id, string Name)
            var inner = value.Substring(1, value.Length - 2).Trim();
            directives.InlineColumns = ParseInlineColumns(inner);
            return;
        }

        // Simple type name
        directives.ResultTypeName = value;
    }

    private static List<InlineColumn> ParseInlineColumns(string inner)
    {
        var columns = new List<InlineColumn>();
        var parts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var spaceIndex = trimmed.LastIndexOf(' ');
            if (spaceIndex > 0)
            {
                var type = trimmed.Substring(0, spaceIndex).Trim();
                var name = trimmed.Substring(spaceIndex + 1).Trim();
                columns.Add(new InlineColumn(type, name));
            }
        }

        return columns;
    }

    /// <summary>
    /// Parses one "-- @type &lt;alias&gt; &lt;dbtype&gt;" directive: the first
    /// whitespace-delimited token is the expression alias, the remainder is the
    /// db type (which may itself contain spaces, e.g. "double precision").
    /// </summary>
    private static void ParseTypeDirective(DirectiveModel directives, string value)
    {
        // AUD-R79-03: any whitespace separates alias from dbtype, not a space
        // only. A tab here used to drop the directive silently, and the build
        // then failed with JNT3005 -- an Error whose message tells the author
        // to "Declare it with: -- @type <alias> <dbtype>", which is the line
        // they had already written. Advice the author has followed is worse
        // than no advice.
        int space = value.IndexOfAny(new[] { ' ', '\t' });
        if (space <= 0)
            return;
        var alias = value.Substring(0, space).Trim();
        var dbType = value.Substring(space + 1).Trim();
        if (alias.Length == 0 || dbType.Length == 0)
            return;

        directives.TypeDirectives ??= new List<TypeDirective>();
        directives.TypeDirectives.Add(new TypeDirective(alias, dbType));
    }

    /// <summary>
    /// Parses one "-- @each &lt;ParamName&gt;" directive. Repeatable: each line
    /// names one parameter to expand as an IN-list at runtime.
    /// </summary>
    private static void ParseEachDirective(DirectiveModel directives, string value)
    {
        if (value.Length == 0)
            return;

        directives.EachParams ??= new List<string>();
        directives.EachParams.Add(value);
    }

    private static void ParseParamsDirective(DirectiveModel directives, string value)
    {
        var explicitParams = new List<ExplicitParam>();
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = trimmed.Substring(0, colonIndex).Trim();
                var type = trimmed.Substring(colonIndex + 1).Trim();
                explicitParams.Add(new ExplicitParam(name, type));
            }
        }

        if (explicitParams.Count > 0)
        {
            directives.ExplicitParams = explicitParams;
        }
    }

    // Every recognized directive name; the value-taking subset falls through
    // to the suspicious check when written bare (TryApplyDirective declines a
    // value-taking directive whose value is empty).
    private static readonly string[] KnownDirectives =
        { "result", "params", "type", "each", "first", "identity", "stream", "call", "proc" };

    // AUD-R79-02 added "call": it names an existing procedure and does nothing
    // at all without one, so bare "-- @call" belongs here rather than in the
    // silently-accepted set.
    private static readonly HashSet<string> ValueRequiredDirectives =
        new(StringComparer.Ordinal) { "result", "params", "type", "each", "call" };

    /// <summary>
    /// Records a JNT3008 warning when an unmatched <c>-- @word</c> comment is
    /// probably a directive: the word IS a known value-taking directive
    /// (written bare, so its "@name " prefix never matched), or is one edit
    /// away from a known name (<c>@frist</c>, <c>@indentity</c>). Anything
    /// further away — <c>@author</c>, <c>@firstborn</c> — is an ordinary
    /// comment and stays silent.
    /// </summary>
    private static void CheckSuspiciousDirective(DirectiveModel directives, string commentBody)
    {
        int end = 1;
        while (end < commentBody.Length && char.IsLetter(commentBody[end]))
            end++;
        if (end == 1)
            return; // bare '@' or '@123' — not directive-shaped
        string word = commentBody.Substring(1, end - 1).ToLowerInvariant();

        string? message = null;
        if (ValueRequiredDirectives.Contains(word))
        {
            message = $"-- @{word} requires a value (e.g. '-- @{word} <...>'); the line was kept as a plain comment and had no effect.";
        }
        else if (word.Length >= 3)
        {
            foreach (var known in KnownDirectives)
            {
                if (IsOneEditAway(word, known))
                {
                    message = $"unrecognized directive '-- @{word}'; did you mean '-- @{known}'? The line was kept as a plain comment and had no effect.";
                    break;
                }
            }
        }

        if (message == null)
            return;
        directives.SuspiciousDirectives ??= new List<string>();
        directives.SuspiciousDirectives.Add(message);
    }

    /// <summary>
    /// Damerau-Levenshtein distance exactly 1: one substitution, one adjacent
    /// transposition, or one insertion/deletion. Equal strings return false
    /// (an exact known name is handled by the real directive matches or the
    /// value-required branch above).
    /// </summary>
    private static bool IsOneEditAway(string a, string b)
    {
        if (a == b)
            return false;

        if (a.Length == b.Length)
        {
            // One substitution, or one adjacent transposition.
            int firstDiff = -1, diffs = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    if (diffs == 0) firstDiff = i;
                    diffs++;
                    if (diffs > 2) return false;
                }
            }
            if (diffs == 1) return true;
            return diffs == 2 && firstDiff + 1 < a.Length
                && a[firstDiff] == b[firstDiff + 1] && a[firstDiff + 1] == b[firstDiff]
                && (firstDiff + 2 >= a.Length || string.CompareOrdinal(a, firstDiff + 2, b, firstDiff + 2, a.Length - firstDiff - 2) == 0);
        }

        // One insertion/deletion: lengths differ by 1; the longer must equal
        // the shorter with exactly one char skipped.
        string longer = a.Length > b.Length ? a : b;
        string shorter = a.Length > b.Length ? b : a;
        if (longer.Length - shorter.Length != 1)
            return false;
        int li = 0, si = 0;
        bool skipped = false;
        while (li < longer.Length && si < shorter.Length)
        {
            if (longer[li] == shorter[si]) { li++; si++; continue; }
            if (skipped) return false;
            skipped = true;
            li++;
        }
        return true;
    }
}
