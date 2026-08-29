namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// Queries written to provoke disagreement between the five engines.
///
/// Every case is a literal expression or a derived table of literals: none of
/// them touches Conduit's schema. That is deliberate -- what is under test is
/// engine semantics, so a case that depended on seeded rows would be asserting
/// two things at once. The suite needs the five connections and nothing else.
///
/// The declared groupings come from the discovery pass described in
/// 018-plan.md and are measurements, not predictions. A case is not allowed to
/// ship with a grouping nobody ran.
/// </summary>
public static class StressCorpus
{
    public static readonly IReadOnlyList<string> KnownEngines =
        ["Sqlite", "Postgres", "MySql", "MariaDb", "SqlServer"];

    public const string NullOrdering = "null-ordering";
    public const string Collation = "collation";
    public const string IntegerDivision = "integer-division";
    public const string EmptyAggregate = "empty-aggregate";
    public const string DateArithmetic = "date-arithmetic";
    public const string Concatenation = "concatenation";
    public const string BooleanHandling = "boolean-handling";

    public static IReadOnlyList<StressCase> Cases => _cases;

    private static readonly List<StressCase> _cases =
    [
        // --- R2's four silent categories first -----------------------------

        new()
        {
            Name = "ascending-places-null",
            Category = NullOrdering,
            Sql = "SELECT v FROM (SELECT 1 AS v UNION ALL SELECT NULL UNION ALL SELECT 2) t ORDER BY v ASC",
            Ordered = true,
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "descending-places-null",
            Category = NullOrdering,
            Sql = "SELECT v FROM (SELECT 1 AS v UNION ALL SELECT NULL UNION ALL SELECT 2) t ORDER BY v DESC",
            Ordered = true,
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        new()
        {
            Name = "equality-ignores-case",
            Category = Collation,
            Sql = "SELECT CASE WHEN 'ABC' = 'abc' THEN 1 ELSE 0 END AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "equality-ignores-trailing-space",
            Category = Collation,
            Sql = "SELECT CASE WHEN 'a' = 'a ' THEN 1 ELSE 0 END AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "mixed-case-sort-order",
            Category = Collation,
            Sql = "SELECT v FROM (SELECT 'a' AS v UNION ALL SELECT 'B') t ORDER BY v ASC",
            Ordered = true,
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        new()
        {
            Name = "positive-operands",
            Category = IntegerDivision,
            Sql = "SELECT 5 / 2 AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "negative-operand-rounding",
            Category = IntegerDivision,
            Sql = "SELECT -5 / 2 AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "division-by-zero",
            Category = IntegerDivision,
            Sql = "SELECT 1 / 0 AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        new()
        {
            Name = "count-and-max-over-no-rows",
            Category = EmptyAggregate,
            Sql = "SELECT COUNT(*) AS c, MAX(v) AS m FROM (SELECT 1 AS v) t WHERE 1 = 0",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "sum-over-no-rows",
            Category = EmptyAggregate,
            Sql = "SELECT SUM(v) AS s FROM (SELECT 1 AS v) t WHERE 1 = 0",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "average-of-integers",
            Category = EmptyAggregate,
            Sql = "SELECT AVG(v) AS a FROM (SELECT 1 AS v UNION ALL SELECT 2) t",
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        // --- the three that tend to fail loudly ----------------------------

        new()
        {
            Name = "month-end-overflow",
            Category = DateArithmetic,
            Sql = "SELECT date('2026-01-31', '+1 month') AS r",
            SqlByEngine = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Postgres"] = "SELECT (DATE '2026-01-31' + INTERVAL '1 month') AS r",
                ["MySql"] = "SELECT DATE_ADD('2026-01-31', INTERVAL 1 MONTH) AS r",
                ["MariaDb"] = "SELECT DATE_ADD('2026-01-31', INTERVAL 1 MONTH) AS r",
                ["SqlServer"] = "SELECT DATEADD(month, 1, CAST('2026-01-31' AS date)) AS r",
            },
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        new()
        {
            Name = "string-joined-to-number",
            Category = Concatenation,
            Sql = "SELECT 'a' || 1 AS r",
            SqlByEngine = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MySql"] = "SELECT CONCAT('a', 1) AS r",
                ["MariaDb"] = "SELECT CONCAT('a', 1) AS r",
                ["SqlServer"] = "SELECT 'a' + 1 AS r",
            },
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "null-operand",
            Category = Concatenation,
            Sql = "SELECT 'a' || NULL AS r",
            SqlByEngine = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MySql"] = "SELECT CONCAT('a', NULL) AS r",
                ["MariaDb"] = "SELECT CONCAT('a', NULL) AS r",
                ["SqlServer"] = "SELECT 'a' + NULL AS r",
            },
            Groups = [],
            Reason = "",
            Guidance = "",
        },

        new()
        {
            Name = "boolean-literal",
            Category = BooleanHandling,
            Sql = "SELECT TRUE AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
        new()
        {
            Name = "comparison-as-a-value",
            Category = BooleanHandling,
            Sql = "SELECT (1 = 1) AS r",
            Groups = [],
            Reason = "",
            Guidance = "",
        },
    ];

    /// <summary>
    /// Structural rules, checked before any engine runs. A malformed case must
    /// not read as a passing one, which is why the suite validates first and
    /// skips second.
    /// </summary>
    public static IReadOnlyList<string> Validate() => Validate(_cases);

    public static IReadOnlyList<string> Validate(IReadOnlyList<StressCase> cases)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (StressCase c in cases)
        {
            if (!seen.Add(c.ToString()))
                problems.Add($"{c}: duplicate case name");

            if (string.IsNullOrWhiteSpace(c.Sql))
                problems.Add($"{c}: no SQL");

            if (c.Groups.Count == 0)
            {
                problems.Add($"{c}: no grouping declared -- run the discovery pass and record what it measured");
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.Reason))
                problems.Add($"{c}: no reason given -- a grouping without one asserts a difference it cannot explain");

            if (string.IsNullOrWhiteSpace(c.Guidance))
                problems.Add($"{c}: no consumer guidance -- naming a difference without saying what to write instead leaves the reader where it found them");

            var named = new List<string>();
            foreach (IReadOnlyList<string> group in c.Groups)
            {
                if (group.Count == 0)
                    problems.Add($"{c}: an empty group");
                named.AddRange(group);
            }

            foreach (string engine in named.Where(e => !KnownEngines.Contains(e)).Distinct(StringComparer.Ordinal))
                problems.Add($"{c}: '{engine}' is not one of the engines this suite runs");

            foreach (string engine in named.GroupBy(e => e, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
                problems.Add($"{c}: '{engine}' appears in more than one group");

            foreach (string engine in KnownEngines.Where(e => !named.Contains(e, StringComparer.Ordinal)))
                problems.Add($"{c}: '{engine}' is in no group -- every engine the suite may run must be placed");

            // R6. A case whose engines all agree is not stressing anything, and
            // keeping it green would pad the suite with a no-op while reading
            // as coverage. Delete it and record the measurement instead.
            if (c.Groups.Count == 1)
                problems.Add($"{c}: one group holding every engine -- this case does not diverge and must be dropped, with its measurement recorded in 018-tasks.md");

            foreach (string engine in c.SqlByEngine.Keys.Where(e => !KnownEngines.Contains(e)))
                problems.Add($"{c}: SQL override for '{engine}', which is not an engine this suite runs");
        }

        return problems;
    }
}
