using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// T2 production-readiness probe (2026-07-31): <see cref="SqlParser.Parse"/> runs
/// UNGUARDED inside the Roslyn generator (JauntyQGenerator.Part2.cs) after the
/// token-level gates (JNT1002-JNT1005) have filtered TooLarge/TooDeep/
/// Unterminated/Unknown. Any exception it throws on a cleanly-tokenized but
/// malformed statement crashes the generator itself — Roslyn reports CS8785 and
/// EVERY generated method vanishes from the consumer's build. Parse must
/// therefore be total over hostile-but-tokenizable input: garbage in, a (possibly
/// empty or nonsensical) QueryModel out, for QueryValidator to reject with a
/// proper diagnostic. Each case here is a shape a user can put in a .sql file.
/// </summary>
public class HostileInputParserTests
{
    private static QueryModel ParseSql(string sql, string name = "HostileQuery")
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        return SqlParser.Parse(tokens, name);
    }

    public static TheoryData<string, string> HostileStatements() => new()
    {
        // Truncated / skeletal statements
        { "bare-select", "SELECT" },
        { "select-from-nothing", "SELECT FROM" },
        { "select-star-no-from", "SELECT *" },
        { "from-first", "FROM WHERE" },
        { "bare-where", "WHERE" },
        { "select-trailing-comma", "SELECT a, FROM t" },
        { "from-trailing-comma", "SELECT a FROM t," },
        { "where-no-predicate", "SELECT a FROM t WHERE" },
        { "where-dangling-operator", "SELECT a FROM t WHERE x =" },
        { "where-dangling-and", "SELECT a FROM t WHERE x = 1 AND" },
        { "join-no-target", "SELECT a FROM t JOIN" },
        { "join-no-on", "SELECT a FROM t JOIN u" },
        { "join-on-nothing", "SELECT a FROM t JOIN u ON" },
        { "on-dangling-eq", "SELECT a FROM t JOIN u ON t.x =" },
        { "order-by-nothing", "SELECT a FROM t ORDER BY" },
        { "group-by-nothing", "SELECT a FROM t GROUP BY" },
        { "having-nothing", "SELECT a FROM t GROUP BY a HAVING" },
        { "limit-no-count", "SELECT a FROM t LIMIT" },
        { "offset-no-count", "SELECT a FROM t LIMIT 1 OFFSET" },

        // Keyword soup and repetition
        { "keyword-soup", "SELECT SELECT FROM FROM WHERE WHERE" },
        { "where-where", "SELECT a FROM t WHERE WHERE x = 1" },
        { "and-or-chain", "SELECT a FROM t WHERE AND OR AND OR" },
        { "not-not-not", "SELECT a FROM t WHERE NOT NOT NOT" },
        { "all-keywords", "WITH RECURSIVE AS IN NOT NULL IS LIKE BETWEEN CASE WHEN THEN ELSE END" },

        // Unbalanced parentheses (tokenize fine; depth cap not hit)
        { "open-parens-only", "(((" },
        { "close-parens-only", ")))" },
        { "select-unclosed-paren", "SELECT a FROM t WHERE (x = 1" },
        { "select-stray-close", "SELECT a FROM t WHERE x = 1)" },
        { "close-before-open", "SELECT a FROM ) t (" },
        { "empty-parens-projection", "SELECT () FROM t" },
        { "in-unclosed-list", "SELECT a FROM t WHERE x IN (1, 2," },
        { "subquery-unclosed", "SELECT a FROM (SELECT b FROM u" },
        { "nested-empty-parens", "SELECT a FROM t WHERE ((()))" },

        // Broken constructs
        { "case-without-end", "SELECT CASE WHEN x = 1 THEN 2 FROM t" },
        { "case-alone", "SELECT CASE FROM t" },
        { "between-without-and", "SELECT a FROM t WHERE x BETWEEN 1" },
        { "cast-no-type", "SELECT CAST(a AS) FROM t" },
        { "cast-unclosed", "SELECT CAST(a AS int FROM t" },
        { "with-unclosed", "WITH x AS (" },
        { "with-no-body", "WITH x AS (SELECT 1)" },
        { "union-alone", "UNION" },
        { "union-trailing", "SELECT a FROM t UNION" },
        { "exists-alone", "SELECT a FROM t WHERE EXISTS" },
        { "insert-no-target", "INSERT INTO" },
        { "insert-no-values", "INSERT INTO t (a, b)" },
        { "values-unclosed", "INSERT INTO t (a) VALUES (1" },
        { "update-no-set", "UPDATE t" },
        { "update-set-nothing", "UPDATE t SET" },
        { "set-dangling-eq", "UPDATE t SET a =" },
        { "delete-alone", "DELETE" },
        { "returning-nothing", "INSERT INTO t (a) VALUES (1) RETURNING" },

        // Symbol and punctuation streams
        { "commas-only", ", , , ," },
        { "dots-only", ". . . ." },
        { "operators-only", "= <> < > <= >= + - * /" },
        { "dangling-qualifier", "SELECT t. FROM t" },
        { "leading-dot", "SELECT .a FROM t" },
        { "double-dots", "SELECT a..b FROM t" },
        { "star-everywhere", "SELECT * FROM * WHERE *" },
        { "semicolons", ";;;" },

        // Parameters in wrong places
        { "params-only", "@p @p @p" },
        { "param-as-table", "SELECT a FROM @p" },
        { "param-as-keyword-slot", "SELECT @p FROM t WHERE @p @p @p" },
        { "bare-at-run", "SELECT a FROM t WHERE x = @" },

        // Literals in odd positions
        { "string-as-table", "SELECT a FROM 'not a table'" },
        { "number-as-table", "SELECT a FROM 42" },
        { "literal-soup", "SELECT 'a' 1 'b' 2 FROM t" },

        // Deep-but-under-cap nesting (999 < MaxNestingDepth = 1000)
        { "deep-nesting-under-cap", new string('(', 999) + "1" + new string(')', 999) },
        { "deep-nesting-unbalanced", new string('(', 999) + "SELECT a FROM t" },
    };

    [Theory]
    [MemberData(nameof(HostileStatements))]
    public void Parse_NeverThrows_OnHostileButTokenizableInput(string label, string sql)
    {
        Assert.NotNull(label);
        var ex = Record.Exception(() => ParseSql(sql));
        Assert.Null(ex);
    }
}
