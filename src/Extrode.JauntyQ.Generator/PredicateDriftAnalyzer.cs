using Microsoft.CodeAnalysis;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

/// <summary>
/// Cross-query predicate-drift check for <c>-- @mirrors</c> (JNT8011), plus the
/// diagnostic that refuses to stay silent when a declared pairing cannot be
/// compared at all (JNT3010).
///
/// A paginated list query and the count query that sizes its pager must filter
/// identically; when they drift, the page contents stay correct while the total
/// is wrong, and nothing surfaces it. The pairing cannot be inferred — the
/// motivating pair is <c>ListPage.sql</c> ↔ <c>CountForUser.sql</c>, which share
/// no stem — so it is declared. Being opt-in also means a count that
/// legitimately differs stays silent rather than teaching people to ignore a
/// warning.
///
/// Like <see cref="NPlusOneAnalyzer"/> this needs the whole corpus, so it runs
/// at the aggregate stage in <see cref="JauntyQGenerator"/>; and like it, it is
/// System.Linq-free (NativeAOT) and deterministic — entries are processed in
/// sorted order and every reported atom list is sorted.
/// </summary>
internal static class PredicateDriftAnalyzer
{
    /// <summary>
    /// One corpus entry: the query's <c>{Entity}.{Method}</c> name, its .sql
    /// path (the diagnostic anchor), its parsed model, and the verbatim
    /// <c>-- @mirrors</c> value when it declared one.
    /// </summary>
    internal readonly struct Entry
    {
        public string Name { get; }
        public string? Path { get; }
        public QueryModel? Query { get; }
        public string? MirrorsTarget { get; }

        public Entry(string name, string? path, QueryModel? query, string? mirrorsTarget)
        {
            Name = name;
            Path = path;
            Query = query;
            MirrorsTarget = mirrorsTarget;
        }
    }

    public static List<Diagnostic> Analyze(IReadOnlyList<Entry> corpus, DatabaseSchema schema)
    {
        var diagnostics = new List<Diagnostic>();

        // Nothing declares a pairing: the whole pass is inert, which is the
        // normal state of every project that has not opted in.
        // Stryker disable once Boolean : with no directive the main loop skips every entry and reports nothing, so skipping the early return changes no output
        bool anyDirective = false;
        foreach (var entry in corpus)
        {
            if (!string.IsNullOrEmpty(entry.MirrorsTarget))
            {
                anyDirective = true;
                // Stryker disable once Statement : later iterations can only set anyDirective to true again
                break;
            }
        }
        if (!anyDirective)
            return diagnostics;

        var declaring = new List<Entry>();
        foreach (var entry in corpus)
        {
            if (!string.IsNullOrEmpty(entry.MirrorsTarget))
                declaring.Add(entry);
        }
        declaring.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var source in declaring)
        {
            var location = JauntyQGenerator.FileLocation(source.Path);

            // The declaring file itself did not produce a model (failed
            // validation, @call proc, empty). It already carries whatever
            // diagnostic caused that; adding a second one about the pairing
            // would be noise on top of the real error.
            if (source.Query == null)
                continue;

            if (!TryResolveTarget(corpus, source, out Entry target, out string? resolveProblem))
            {
                diagnostics.Add(Diagnostic.Create(JauntyDiagnostics.JNT3010, location,
                    $"{source.Name} declares -- @mirrors {source.MirrorsTarget}, but {resolveProblem} " +
                    "The two filters were not compared."));
                continue;
            }

            var sourceAtoms = CanonicalAtoms(source.Query!, schema, out bool sourceResolved);
            var targetAtoms = CanonicalAtoms(target.Query!, schema, out bool targetResolved);

            // A column an atom names that will not resolve against the schema
            // (a CTE virtual column, an ambiguous unqualified name) cannot be
            // canonicalized. Falling back to the raw text would manufacture a
            // drift claim out of a spelling difference, and one false positive
            // kills an opt-in diagnostic outright -- so the pairing is declared
            // uncomparable instead. The author asked to hear about this pair;
            // silence is the one answer that is not allowed.
            if (!sourceResolved || !targetResolved)
            {
                string side = !sourceResolved && !targetResolved ? "both queries reference"
                    : !sourceResolved ? $"{source.Name} references"
                    : $"{target.Name} references";
                diagnostics.Add(Diagnostic.Create(JauntyDiagnostics.JNT3010, location,
                    $"{source.Name} declares -- @mirrors {source.MirrorsTarget}, but {side} a WHERE-clause " +
                    "column that does not resolve to a schema column, so the two filters cannot be " +
                    "compared. Qualify the column with its table, or remove the directive."));
                continue;
            }

            var onlyInSource = Difference(sourceAtoms, targetAtoms);
            var onlyInTarget = Difference(targetAtoms, sourceAtoms);
            if (onlyInSource.Count == 0 && onlyInTarget.Count == 0)
                continue;

            diagnostics.Add(Diagnostic.Create(JauntyDiagnostics.JNT8011, location,
                BuildDriftMessage(source, target, onlyInSource, onlyInTarget)));
        }

        return diagnostics;
    }

    /// <summary>
    /// Resolves a <c>-- @mirrors</c> value to a corpus entry. A qualified
    /// <c>Entity.Method</c> matches that name exactly; a bare <c>Method</c>
    /// matches on the method segment, and is an error when two entities both
    /// have one — guessing which was meant is exactly the silent-wrong-answer
    /// this diagnostic exists to prevent. Self-reference is rejected too: a
    /// query trivially mirrors itself, so the directive was meant for something
    /// else.
    /// </summary>
    private static bool TryResolveTarget(
        IReadOnlyList<Entry> corpus, Entry source, out Entry target, out string? problem)
    {
        target = default;
        problem = null;
        string wanted = source.MirrorsTarget!;

        var matches = new List<Entry>();
        bool qualified = wanted.IndexOf('.') >= 0;
        foreach (var candidate in corpus)
        {
            bool hit = qualified
                ? string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase)
                : string.Equals(MethodSegment(candidate.Name), wanted, StringComparison.OrdinalIgnoreCase);
            if (hit)
                matches.Add(candidate);
        }
        matches.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        if (matches.Count == 0)
        {
            problem = "no query by that name was compiled. Check the spelling, and that the file is in AdditionalFiles.";
            return false;
        }
        if (matches.Count > 1)
        {
            var names = new List<string>(matches.Count);
            foreach (var m in matches)
                names.Add(m.Name);
            problem = $"that name is ambiguous — it matches {string.Join(", ", names)}. Qualify it as Entity.Method.";
            return false;
        }
        if (string.Equals(matches[0].Name, source.Name, StringComparison.OrdinalIgnoreCase))
        {
            problem = "that is this same query.";
            return false;
        }
        if (matches[0].Query == null)
        {
            problem = $"{matches[0].Name} produced no query model (it failed validation, or binds a stored procedure).";
            return false;
        }

        target = matches[0];
        return true;
    }

    private static string MethodSegment(string qualifiedName)
    {
        return qualifiedName.Substring(qualifiedName.LastIndexOf('.') + 1);
    }

    /// <summary>
    /// The canonical form of every predicate atom in a statement AND in each of
    /// its CTE bodies, unioned.
    ///
    /// The union is what makes the motivating pair work at all: a list query
    /// paginates inside a <c>page</c> CTE, so its WHERE lives in the CTE body,
    /// while the count that mirrors it is a flat statement. Comparing top-level
    /// WHERE clauses only would be silent on exactly the shape this exists for.
    ///
    /// <paramref name="allResolved"/> comes back false when any column term the
    /// canonicalization had to resolve did not resolve.
    /// </summary>
    private static List<string> CanonicalAtoms(QueryModel query, DatabaseSchema schema, out bool allResolved)
    {
        var result = new List<string>();
        allResolved = true;

        CollectFrom(query, schema, result, ref allResolved);
        foreach (var cte in query.Ctes)
            CollectFrom(cte.Body, schema, result, ref allResolved);

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static void CollectFrom(
        QueryModel scope, DatabaseSchema schema, List<string> into, ref bool allResolved)
    {
        // Stryker disable once Statement : with no atoms the loop below adds nothing and leaves allResolved untouched
        if (scope.PredicateAtoms.Count == 0)
            return;

        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in scope.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            aliasToTable[key] = table.TableName;
        }

        foreach (var atom in scope.PredicateAtoms)
        {
            if (!TryCanonicalize(atom, scope, aliasToTable, schema, out string canonical))
                allResolved = false;
            into.Add(canonical);
        }
    }

    /// <summary>
    /// Renders one atom as a single canonical string: terms joined by spaces,
    /// keywords and operators upper-cased, parameters and literals as written,
    /// and columns resolved to <c>table.column</c> lowercased — which is what
    /// lets <c>b.user_id = @userId</c> in one file and <c>user_id = @userId</c>
    /// in another compare equal. The drift that matters is which columns are
    /// constrained how, not how the author spelled the table.
    ///
    /// Only DEPTH-0 column terms are resolved. Inside parentheses the atom is
    /// either a value list or a lifted-back subquery whose aliases belong to its
    /// own scope and resolve against nothing out here; those terms are compared
    /// as written, lowercased. That is why a query with an EXISTS predicate is
    /// still comparable rather than being ruled uncomparable by its own inner
    /// aliases.
    /// </summary>
    private static bool TryCanonicalize(
        PredicateAtom atom, QueryModel scope, Dictionary<string, string> aliasToTable,
        DatabaseSchema schema, out string canonical)
    {
        bool resolved = true;
        var parts = new List<string>(atom.Terms.Count);
        int depth = 0;

        foreach (var term in atom.Terms)
        {
            switch (term.Kind)
            {
                case AtomTermKind.Column:
                    if (depth > 0)
                    {
                        parts.Add(AsWritten(term));
                        break;
                    }
                    var column = QueryValidator.ResolveColumn(
                        scope, term.TableAlias, term.Text, aliasToTable, schema, out string? tableName);
                    if (column == null || tableName == null)
                    {
                        resolved = false;
                        // Stryker disable once Statement : an unresolved atom's canonical text is never compared; the pair is reported as JNT3010 instead
                        parts.Add(AsWritten(term));
                        break;
                    }
                    parts.Add(tableName.ToLowerInvariant() + "." + column.Name.ToLowerInvariant());
                    break;

                case AtomTermKind.Parameter:
                case AtomTermKind.Literal:
                    parts.Add(term.Text);
                    break;

                default:
                    if (term.Text == "(")
                        depth++;
                    else if (term.Text == ")")
                        depth--;
                    parts.Add(term.Text.ToUpperInvariant());
                    break;
            }
        }

        canonical = string.Join(" ", parts);
        return resolved;
    }

    private static string AsWritten(AtomTerm term) =>
        (string.IsNullOrEmpty(term.TableAlias) ? term.Text : term.TableAlias + "." + term.Text)
            .ToLowerInvariant();

    /// <summary>
    /// The atoms in <paramref name="left"/> that <paramref name="right"/> does
    /// not have, counting duplicates: two copies of the same predicate on one
    /// side and one on the other is still a difference. Both inputs are sorted,
    /// so a linear merge suffices and the result is sorted too.
    /// </summary>
    private static List<string> Difference(List<string> left, List<string> right)
    {
        var result = new List<string>();
        int i = 0, j = 0;
        while (i < left.Count)
        {
            if (j >= right.Count)
            {
                result.Add(left[i++]);
                continue;
            }
            int cmp = string.CompareOrdinal(left[i], right[j]);
            if (cmp == 0) { i++; j++; }
            else if (cmp < 0) result.Add(left[i++]);
            else j++;
        }
        return result;
    }

    private static string BuildDriftMessage(
        Entry source, Entry target, List<string> onlyInSource, List<string> onlyInTarget)
    {
        string sourceMethod = MethodSegment(source.Name);
        string targetMethod = MethodSegment(target.Name);

        var sides = new List<string>(2);
        if (onlyInSource.Count > 0)
            sides.Add($"only in {sourceMethod}: {Quote(onlyInSource)}");
        if (onlyInTarget.Count > 0)
            sides.Add($"only in {targetMethod}: {Quote(onlyInTarget)}");

        return $"{source.Name} declares -- @mirrors {source.MirrorsTarget} but their WHERE clauses " +
               $"differ ({string.Join("; ", sides)}). Two queries declared to filter alike must filter " +
               "alike: a count that filters differently from the list it sizes returns a total the " +
               "page contents contradict.";
    }

    private static string Quote(List<string> atoms)
    {
        var quoted = new List<string>(atoms.Count);
        foreach (var atom in atoms)
            quoted.Add("[" + atom + "]");
        return string.Join(", ", quoted);
    }
}
