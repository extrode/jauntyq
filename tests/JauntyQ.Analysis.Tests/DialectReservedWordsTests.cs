using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// AUD-R64-01 (T8 residual). Round 64 curated the per-dialect reserved-word
/// lists from each engine's published grammar rather than from the engine
/// itself, and shipped no list at all for SQLite. The T8 assessment replaced
/// that with an exhaustive live probe: 425 candidate words (the union of all
/// four shipped lists, SQLite's keyword appendix, and a control group of
/// ordinary column nouns) were created quoted and then referenced BARE in
/// every position AutoCrud actually emits -- SELECT list, INSERT column list,
/// UPDATE SET, WHERE, ORDER BY, DELETE, the dialect-native upsert, and the
/// table-name position -- against postgres:16, mysql:8.0.46, MariaDB 11.8.8
/// (which rides the "mysql" dialect string) and SQL Server 2022 RTM-CU25,
/// plus SQLite 3.45.3 in-process.
///
/// A word is reserved here if the engine REJECTED the bare reference, or
/// accepted it and returned something other than the stored sentinel (the
/// silent-misparse case round 64 raised the finding for). The words asserted
/// below are the probe's verdicts, not a re-reading of the documentation.
/// </summary>
public class DialectReservedWordsTests
{
    // ── Under-inclusion: words the engine rejects that the shipped lists missed

    [Theory]
    // SQLite had NO list at all before this fix, so every one of these was
    // emitted bare into SQL the engine refuses to parse.
    [InlineData("sqlite", "add")]
    [InlineData("sqlite", "check")]
    [InlineData("sqlite", "commit")]
    [InlineData("sqlite", "constraint")]
    [InlineData("sqlite", "default")]
    [InlineData("sqlite", "index")]
    [InlineData("sqlite", "primary")]
    [InlineData("sqlite", "references")]
    [InlineData("sqlite", "table")]
    [InlineData("sqlite", "transaction")]
    [InlineData("sqlite", "unique")]
    [InlineData("sqlite", "using")]
    // PostgreSQL under-inclusions. The join keywords are the notable ones:
    // a column called "left" or "right" is entirely ordinary in a layout or
    // ledger schema, and every one of these aborts the generated statement.
    [InlineData("postgres", "authorization")]
    [InlineData("postgres", "binary")]
    [InlineData("postgres", "concurrently")]
    [InlineData("postgres", "cross")]
    [InlineData("postgres", "freeze")]
    [InlineData("postgres", "full")]
    [InlineData("postgres", "ilike")]
    [InlineData("postgres", "inner")]
    [InlineData("postgres", "is")]
    [InlineData("postgres", "isnull")]
    [InlineData("postgres", "join")]
    [InlineData("postgres", "left")]
    [InlineData("postgres", "like")]
    [InlineData("postgres", "natural")]
    [InlineData("postgres", "notnull")]
    [InlineData("postgres", "outer")]
    [InlineData("postgres", "overlaps")]
    [InlineData("postgres", "right")]
    [InlineData("postgres", "similar")]
    [InlineData("postgres", "system_user")]
    [InlineData("postgres", "tablesample")]
    [InlineData("postgres", "verbose")]
    // MySQL/MariaDB under-inclusions. "except"/"intersect" became reserved in
    // MySQL 8.0.31 and MariaDB 10.3; "returning"/"offset"/"current_role" are
    // MariaDB-only, and MariaDB shares this list because it rides the same
    // dialect string.
    [InlineData("mysql", "cube")]
    [InlineData("mysql", "current_role")]
    [InlineData("mysql", "except")]
    [InlineData("mysql", "grouping")]
    [InlineData("mysql", "intersect")]
    [InlineData("mysql", "offset")]
    [InlineData("mysql", "returning")]
    public void IsReservedInDialect_FlagsWordTheEngineRejectsBare(string dialect, string word)
    {
        Assert.True(DialectReservedWords.IsReservedInDialect(word, dialect));
    }

    // ── Over-inclusion: words the shipped lists reserved that the engine
    // accepts bare in every emitted position. Each one silently removed a
    // whole table from the generated API for no reason at all.

    [Theory]
    [InlineData("postgres", "groups")]
    [InlineData("postgres", "over")]
    [InlineData("postgres", "row")]
    [InlineData("postgres", "within")]
    [InlineData("mysql", "second")]
    [InlineData("mysql", "user")]
    public void IsReservedInDialect_DoesNotFlagWordTheEngineAcceptsBare(string dialect, string word)
    {
        Assert.False(DialectReservedWords.IsReservedInDialect(word, dialect, SqlIdentifierPosition.Column));
        Assert.False(DialectReservedWords.IsReservedInDialect(word, dialect, SqlIdentifierPosition.Object));
    }

    // ── Position matters, but only just: across 425 words and four engines
    // exactly six diverge between the column and object positions. Folding
    // them into one list would either drop working schemas (MySQL "value")
    // or emit SQL the engine refuses (SQLite "cast"), so the check takes the
    // position rather than guessing.

    [Fact]
    public void MySqlValue_IsReservedAsTableNameOnly()
    {
        // MariaDB 11.8 refuses "INSERT INTO value (...)" but accepts "value"
        // as a column in every position. "value" is a very common column
        // name; reserving it wholesale would silently drop those tables.
        Assert.True(DialectReservedWords.IsReservedInDialect("value", "mysql", SqlIdentifierPosition.Object));
        Assert.False(DialectReservedWords.IsReservedInDialect("value", "mysql", SqlIdentifierPosition.Column));
    }

    [Theory]
    [InlineData("cast")]
    [InlineData("current_date")]
    [InlineData("current_time")]
    [InlineData("current_timestamp")]
    [InlineData("raise")]
    public void SqliteFunctionKeywords_AreReservedAsColumnNameOnly(string word)
    {
        // "SELECT cast, id FROM t" is a syntax error because the parser
        // demands "cast(" -- but "FROM cast" parses fine as a table name.
        Assert.True(DialectReservedWords.IsReservedInDialect(word, "sqlite", SqlIdentifierPosition.Column));
        Assert.False(DialectReservedWords.IsReservedInDialect(word, "sqlite", SqlIdentifierPosition.Object));
    }

    [Fact]
    public void TwoArgOverload_TakesTheUnionOfBothPositions()
    {
        // The position-less overload is the conservative one: a caller that
        // does not know which position the name lands in must get the
        // stricter answer, never the laxer one.
        Assert.True(DialectReservedWords.IsReservedInDialect("value", "mysql"));
        Assert.True(DialectReservedWords.IsReservedInDialect("cast", "sqlite"));
    }

    // ── Controls. These are ordinary column nouns that survived every probed
    // position on every engine; a test suite that flagged them would be
    // asserting the gate is broken, not that it works.

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("price")]
    [InlineData("total")]
    [InlineData("customer")]
    [InlineData("amount")]
    [InlineData("status")]
    [InlineData("created_at")]
    public void OrdinaryColumnNouns_AreNotReservedInAnyDialect(string word)
    {
        foreach (string dialect in new[] { "postgres", "mysql", "sqlserver", "sqlite" })
        {
            Assert.False(DialectReservedWords.IsReservedInDialect(word, dialect, SqlIdentifierPosition.Column));
        }
    }

    [Fact]
    public void UnknownDialect_ReservesNothing()
    {
        Assert.False(DialectReservedWords.IsReservedInDialect("select", "oracle", SqlIdentifierPosition.Column));
        Assert.False(DialectReservedWords.IsReservedInDialect("select", null, SqlIdentifierPosition.Column));
        Assert.False(DialectReservedWords.IsReservedInDialect("", "sqlite", SqlIdentifierPosition.Column));
    }

    [Fact]
    public void MatchIsCaseInsensitive()
    {
        Assert.True(DialectReservedWords.IsReservedInDialect("SELECT", "sqlite", SqlIdentifierPosition.Column));
        Assert.True(DialectReservedWords.IsReservedInDialect("Join", "postgres", SqlIdentifierPosition.Column));
    }

    // ── End to end through AutoCrud, which is the thing that actually
    // decides whether a table reaches the generated API.

    [Fact]
    public void AutoCrud_Sqlite_SkipsTableWithReservedColumn()
    {
        // Before this fix SQLite had no list, so this table's CRUD was
        // synthesized and the generated SQL was "SELECT id, index FROM ..."
        // -- a syntax error at runtime, with nothing between authorship and
        // the failure.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["widgets"] = new TableSchema
        {
            Name = "widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true },
                ["index"] = new ColumnSchema { Name = "index", DbType = "integer" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void AutoCrud_Sqlite_SkipsTableWhoseNameIsReserved()
    {
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["transaction"] = new TableSchema
        {
            Name = "transaction",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true },
                ["amount"] = new ColumnSchema { Name = "amount", DbType = "integer" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void AutoCrud_Sqlite_EmitsTableWhoseKeywordIsSafeInThatPosition()
    {
        // "raise" is reserved as a SQLite COLUMN name (the parser demands
        // "raise(") but fine as a table name. The positional check is what
        // keeps this table in the API.
        //
        // "cast" would be the more obvious example and does NOT work here:
        // SqlTokenizer.IsReservedKeyword lists CAST, so JauntyQ's own gate
        // rejects it regardless of dialect. Two independent gates, and this
        // test is about the dialect one.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["raise"] = new TableSchema
        {
            Name = "raise",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true },
                ["label"] = new ColumnSchema { Name = "label", DbType = "text" }
            }
        };

        Assert.NotEmpty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void AutoCrud_MySql_EmitsTableWithValueColumn()
    {
        // The mirror image: "value" is reserved as a MySQL/MariaDB table
        // name only. A settings table with a "value" column must keep its
        // CRUD.
        var schema = new DatabaseSchema { Dialect = "mysql" };
        schema.Tables["settings"] = new TableSchema
        {
            Name = "settings",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["value"] = new ColumnSchema { Name = "value", DbType = "varchar" }
            }
        };

        Assert.NotEmpty(AutoCrud.Synthesize(schema));
    }

    [Fact]
    public void AutoCrud_Postgres_EmitsTableWithFormerlyOverIncludedColumn()
    {
        // "over", "row", "groups" and "within" were all reserved by round
        // 64's list and all parse fine bare on PostgreSQL 16. Each one cost
        // the whole table its generated API.
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["measurements"] = new TableSchema
        {
            Name = "measurements",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["row"] = new ColumnSchema { Name = "row", DbType = "int" },
                ["over"] = new ColumnSchema { Name = "over", DbType = "int" }
            }
        };

        Assert.NotEmpty(AutoCrud.Synthesize(schema));
    }
}
