namespace Conduit.Differential.Tests;

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
/// ship with a grouping nobody ran, and a case measured to produce one group is
/// deleted rather than kept green -- the three that were are recorded in
/// 018-tasks.md.
/// </summary>
public static class StressCorpus
{
    public static readonly IReadOnlyList<string> KnownEngines =
        ["Sqlite", "Postgres", "MySql", "MariaDb", "SqlServer"];

    public const string NullOrdering = "null-ordering";
    public const string Collation = "collation";
    public const string IntegerDivision = "integer-division";
    public const string AggregateTyping = "aggregate-typing";
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
            Groups = [["Sqlite", "MySql", "MariaDb", "SqlServer"], ["Postgres"]],
            Reason = "SQL leaves the sort position of NULL implementation-defined. PostgreSQL orders NULL as greater than every non-null value, so ascending puts it last; SQLite, MySQL, MariaDB and SQL Server order it as smallest and put it first.",
            Guidance = "Say which you mean: ORDER BY v NULLS FIRST or NULLS LAST on PostgreSQL, SQLite 3.30+ and MariaDB 10.6+. Where the dialect has no such clause, sort on an explicit flag first -- ORDER BY CASE WHEN v IS NULL THEN 1 ELSE 0 END, v -- which behaves the same on all five.",
        },
        new()
        {
            Name = "descending-places-null",
            Category = NullOrdering,
            Sql = "SELECT v FROM (SELECT 1 AS v UNION ALL SELECT NULL UNION ALL SELECT 2) t ORDER BY v DESC",
            Ordered = true,
            Groups = [["Sqlite", "MySql", "MariaDb", "SqlServer"], ["Postgres"]],
            Reason = "The same ordering rule seen from the other end: because PostgreSQL treats NULL as the largest value, descending puts it first while the other four put it last.",
            Guidance = "As for the ascending case. A paged query ordering descending on a nullable column returns different rows on page one between PostgreSQL and the rest.",
        },

        new()
        {
            Name = "equality-ignores-case",
            Category = Collation,
            Sql = "SELECT CASE WHEN 'ABC' = 'abc' THEN 1 ELSE 0 END AS r",
            Groups = [["MySql", "MariaDb", "SqlServer"], ["Postgres", "Sqlite"]],
            Reason = "MySQL and MariaDB default to a case-insensitive collation, and SQL Server's usual install default (SQL_Latin1_General_CP1_CI_AS) is case-insensitive too, so all three call the two strings equal. PostgreSQL and SQLite compare text by code point and call them different.",
            Guidance = "Never let the default decide. Compare LOWER(a) = LOWER(b), or declare the column's collation explicitly. SQL Server's side of this is an install-time choice, so it can differ between two deployments of the same application.",
        },
        new()
        {
            Name = "equality-ignores-trailing-space",
            Category = Collation,
            Sql = "SELECT CASE WHEN 'a' = 'a ' THEN 1 ELSE 0 END AS r",
            Groups = [["MariaDb", "SqlServer"], ["Sqlite", "Postgres", "MySql"]],
            Reason = "PAD SPACE collations ignore trailing spaces when comparing. MariaDB's default (utf8mb4_general_ci) and SQL Server's default are PAD SPACE; MySQL 8's default (utf8mb4_0900_ai_ci) is NO PAD, and SQLite and PostgreSQL compare the bytes. This is the one measured case where MySQL and MariaDB fall on opposite sides, so a MySQL-to-MariaDB move is not the no-op it looks like.",
            Guidance = "Trim on the way in, or compare TRIM(a) = TRIM(b). A uniqueness constraint over a text column means something different on the two sides of this split.",
        },
        new()
        {
            Name = "mixed-case-sort-order",
            Category = Collation,
            Sql = "SELECT v FROM (SELECT 'a' AS v UNION ALL SELECT 'B') t ORDER BY v ASC",
            Ordered = true,
            Groups = [["MySql", "MariaDb", "SqlServer"], ["Postgres", "Sqlite"]],
            Reason = "The same collation split applied to ordering: a case-insensitive collation sorts 'a' before 'B', while code-point ordering puts 'B' (0x42) before 'a' (0x61). PostgreSQL's side depends on the database's LC_COLLATE and was measured against the C-locale default of the postgres:16-alpine image; a cluster initialized with a language locale sorts with MySQL instead.",
            Guidance = "ORDER BY LOWER(v) for an order that does not change under the engine. An alphabetical listing shown to a user otherwise reorders itself on a port.",
        },

        new()
        {
            Name = "positive-operands",
            Category = IntegerDivision,
            Sql = "SELECT 5 / 2 AS r",
            Groups = [["MySql", "MariaDb"], ["Sqlite", "Postgres", "SqlServer"]],
            Reason = "MySQL and MariaDB's / is always a decimal division and returns 2.5 for two integer operands. SQLite, PostgreSQL and SQL Server divide as integers and return 2.",
            Guidance = "Use DIV on MySQL and MariaDB where integer division is what you want, and multiply by 1.0 on the other three where it is not. Both the value and its type differ, so a caller mapping the column sees it change as well.",
        },
        new()
        {
            Name = "negative-operand-rounding",
            Category = IntegerDivision,
            Sql = "SELECT -5 / 2 AS r",
            Groups = [["MySql", "MariaDb"], ["Sqlite", "Postgres", "SqlServer"]],
            Reason = "The same split, kept for what it rules out: among the three that do divide as integers, all truncate toward zero (-2, not -3), so this is a decimal-against-integer difference and not a rounding-direction one.",
            Guidance = "As for the positive case.",
        },
        new()
        {
            Name = "division-by-zero",
            Category = IntegerDivision,
            Sql = "SELECT 1 / 0 AS r",
            Groups = [["Sqlite", "MySql", "MariaDb"], ["Postgres", "SqlServer"]],
            Reason = "PostgreSQL raises SQLSTATE 22012 and SQL Server raises its divide-by-zero error; SQLite, MySQL and MariaDB return NULL. The three that return NULL turn a broken calculation into a missing value, which is the harder failure to notice.",
            Guidance = "Guard the divisor rather than relying on either behavior: NULLIF(d, 0) yields NULL on all five, and a COALESCE around it makes the intent explicit.",
        },

        new()
        {
            Name = "average-of-integers",
            Category = AggregateTyping,
            Sql = "SELECT AVG(v) AS a FROM (SELECT 1 AS v UNION ALL SELECT 2) t",
            Groups = [["Sqlite", "Postgres", "MySql", "MariaDb"], ["SqlServer"]],
            Reason = "SQL Server's AVG returns the type of its argument, so averaging integers truncates and 1.5 comes back as 1. The other four promote to a fractional type.",
            Guidance = "Cast inside the aggregate -- AVG(CAST(v AS float)) or AVG(v * 1.0) -- in any query whose average must mean the same everywhere.",
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
            Groups = [["MySql", "MariaDb"], ["Postgres", "SqlServer"], ["Sqlite"]],
            Reason = "Adding one month to 31 January divides the engines two ways at once. On the value: PostgreSQL, SQL Server, MySQL and MariaDB all clamp to 28 February, while SQLite's date() normalizes the overflow and returns 3 March -- a different month, silently. On the type: the first two return a temporal value and the MySQL pair return text, because DATE_ADD over a string argument yields a string. The type half follows from how each dialect spells the operation and has no portable form; the value half is the finding.",
            Guidance = "Do not add months in SQL if the result must be the same everywhere. SQLite is the outlier and returns a date in the wrong month for any end-of-month input; compute the clamped date in the application, or keep a real date column and use the engine's own adder knowing SQLite's does not clamp.",
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
            Groups = [["Sqlite", "Postgres", "MySql", "MariaDb"], ["SqlServer"]],
            Reason = "SQL Server spells concatenation with the same + it uses for addition, so a string beside an integer makes it convert rather than join, and the conversion fails. The other four join and return 'a1'. This one fails loudly, which makes it the least dangerous case in the corpus.",
            Guidance = "Cast the number where it is joined: 'a' + CAST(1 AS varchar(11)) on SQL Server, or CONCAT('a', 1), which every engine here accepts except SQLite before 3.44.",
        },

        new()
        {
            Name = "boolean-literal",
            Category = BooleanHandling,
            Sql = "SELECT TRUE AS r",
            Groups = [["Sqlite", "MySql", "MariaDb"], ["Postgres"], ["SqlServer"]],
            Reason = "Three behaviors from one keyword. PostgreSQL has a real boolean type and returns one, so a caller receives a bool; SQLite, MySQL and MariaDB treat TRUE as the integer 1; SQL Server has no boolean literal at all, reads TRUE as a column name, and fails because no such column exists.",
            Guidance = "Write 1 and 0 and compare against them explicitly. This is the case where 017's normalizer and this suite disagree on purpose: a bool and the 1 it stands for are interchangeable for an application query and are the whole finding here.",
        },
        new()
        {
            Name = "comparison-as-a-value",
            Category = BooleanHandling,
            Sql = "SELECT (1 = 1) AS r",
            Groups = [["Sqlite", "MySql", "MariaDb"], ["Postgres"], ["SqlServer"]],
            Reason = "The same three-way split reached by a different route. SQL Server has no boolean value type, so a comparison cannot stand in a select list at all and the parser refuses it.",
            Guidance = "Wrap it: CASE WHEN 1 = 1 THEN 1 ELSE 0 END returns an integer 1 on all five.",
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
