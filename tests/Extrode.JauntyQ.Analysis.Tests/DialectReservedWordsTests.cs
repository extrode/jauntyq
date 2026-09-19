using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

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

    [Theory]
    [InlineData("raise")]
    [InlineData("current_date")]
    [InlineData("current_time")]
    [InlineData("current_timestamp")]
    public void AutoCrud_Sqlite_SkipsTableNamedForAColumnOnlyKeyword(string tableName)
    {
        // These four are reserved as SQLite COLUMN names, not object names, so
        // "SELECT id, label FROM raise" parses fine. An earlier cut of this fix
        // therefore let the table through on the strength of the FROM position
        // alone -- and generated a GetById whose WHERE clause SQLite rejects,
        // because the table name is ALSO emitted as a qualifier there:
        //
        //   SELECT id, nm FROM raise                      -- parses
        //   SELECT id, nm FROM raise WHERE raise.id = 1   -- near ".": syntax error
        //
        // Verified live against SQLite 3.45.3 for all four words. A false
        // accept, i.e. generated SQL the engine rejects at runtime, which is
        // the one direction this gate must never fail in. The table name is now
        // checked against both positions.
        //
        // "cast" is the more obvious member of this set and is not in the
        // theory: SqlTokenizer.IsReservedKeyword lists CAST, so JauntyQ's own
        // gate rejects it regardless of dialect, and this test is about the
        // dialect gate.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables[tableName] = new TableSchema
        {
            Name = tableName,
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true },
                ["label"] = new ColumnSchema { Name = "label", DbType = "text" }
            }
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
        Assert.NotNull(AutoCrud.DescribeUnusableTable(schema.Tables[tableName], "sqlite"));
    }

    [Fact]
    public void AutoCrud_TableWithNoColumns_IsSkippedWithAStatedReason()
    {
        // Synthesize has always skipped this, silently. It is the same
        // silent-drop shape JNT2015 exists to end, so it needs a reason too.
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["empty_t"] = new TableSchema
        {
            Name = "empty_t",
            Columns = new Dictionary<string, ColumnSchema>()
        };

        Assert.Empty(AutoCrud.Synthesize(schema));
        string? why = AutoCrud.DescribeUnusableTable(schema.Tables["empty_t"], "sqlite");
        Assert.NotNull(why);
        Assert.Contains("no columns", why);
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

    // ── Exhaustive coverage: every word in each dialect's shipped list, so a

    // future edit that mistypes, drops or truncates a string literal in

    // DialectReservedWords fails here instead of silently under-reserving.



    [Theory]
    [InlineData("all")]
    [InlineData("analyse")]
    [InlineData("analyze")]
    [InlineData("and")]
    [InlineData("any")]
    [InlineData("array")]
    [InlineData("as")]
    [InlineData("asc")]
    [InlineData("asymmetric")]
    [InlineData("authorization")]
    [InlineData("binary")]
    [InlineData("both")]
    [InlineData("case")]
    [InlineData("cast")]
    [InlineData("check")]
    [InlineData("collate")]
    [InlineData("column")]
    [InlineData("concurrently")]
    [InlineData("constraint")]
    [InlineData("create")]
    [InlineData("cross")]
    [InlineData("current_catalog")]
    [InlineData("current_date")]
    [InlineData("current_role")]
    [InlineData("current_time")]
    [InlineData("current_timestamp")]
    [InlineData("current_user")]
    [InlineData("default")]
    [InlineData("deferrable")]
    [InlineData("desc")]
    [InlineData("distinct")]
    [InlineData("do")]
    [InlineData("else")]
    [InlineData("end")]
    [InlineData("except")]
    [InlineData("false")]
    [InlineData("fetch")]
    [InlineData("for")]
    [InlineData("foreign")]
    [InlineData("freeze")]
    [InlineData("from")]
    [InlineData("full")]
    [InlineData("grant")]
    [InlineData("group")]
    [InlineData("having")]
    [InlineData("ilike")]
    [InlineData("in")]
    [InlineData("initially")]
    [InlineData("inner")]
    [InlineData("intersect")]
    [InlineData("into")]
    [InlineData("is")]
    [InlineData("isnull")]
    [InlineData("join")]
    [InlineData("lateral")]
    [InlineData("leading")]
    [InlineData("left")]
    [InlineData("like")]
    [InlineData("limit")]
    [InlineData("localtime")]
    [InlineData("localtimestamp")]
    [InlineData("natural")]
    [InlineData("not")]
    [InlineData("notnull")]
    [InlineData("null")]
    [InlineData("offset")]
    [InlineData("on")]
    [InlineData("only")]
    [InlineData("or")]
    [InlineData("order")]
    [InlineData("outer")]
    [InlineData("overlaps")]
    [InlineData("placing")]
    [InlineData("primary")]
    [InlineData("references")]
    [InlineData("returning")]
    [InlineData("right")]
    [InlineData("select")]
    [InlineData("session_user")]
    [InlineData("similar")]
    [InlineData("some")]
    [InlineData("symmetric")]
    [InlineData("system_user")]
    [InlineData("table")]
    [InlineData("tablesample")]
    [InlineData("then")]
    [InlineData("to")]
    [InlineData("trailing")]
    [InlineData("true")]
    [InlineData("union")]
    [InlineData("unique")]
    [InlineData("user")]
    [InlineData("using")]
    [InlineData("variadic")]
    [InlineData("verbose")]
    [InlineData("when")]
    [InlineData("where")]
    [InlineData("window")]
    [InlineData("with")]
    public void AllPostgresReservedWords_AreFlaggedReserved(string word)
    {
        Assert.True(DialectReservedWords.IsReservedInDialect(word, "postgres"));
    }



    [Theory]
    [InlineData("accessible")]
    [InlineData("add")]
    [InlineData("all")]
    [InlineData("alter")]
    [InlineData("analyze")]
    [InlineData("and")]
    [InlineData("as")]
    [InlineData("asc")]
    [InlineData("asensitive")]
    [InlineData("before")]
    [InlineData("between")]
    [InlineData("bigint")]
    [InlineData("binary")]
    [InlineData("blob")]
    [InlineData("both")]
    [InlineData("by")]
    [InlineData("call")]
    [InlineData("cascade")]
    [InlineData("case")]
    [InlineData("change")]
    [InlineData("char")]
    [InlineData("character")]
    [InlineData("check")]
    [InlineData("collate")]
    [InlineData("column")]
    [InlineData("condition")]
    [InlineData("constraint")]
    [InlineData("continue")]
    [InlineData("convert")]
    [InlineData("create")]
    [InlineData("cross")]
    [InlineData("cube")]
    [InlineData("current_date")]
    [InlineData("current_role")]
    [InlineData("current_time")]
    [InlineData("current_timestamp")]
    [InlineData("current_user")]
    [InlineData("cursor")]
    [InlineData("database")]
    [InlineData("databases")]
    [InlineData("day_hour")]
    [InlineData("day_microsecond")]
    [InlineData("day_minute")]
    [InlineData("day_second")]
    [InlineData("dec")]
    [InlineData("decimal")]
    [InlineData("declare")]
    [InlineData("default")]
    [InlineData("delayed")]
    [InlineData("delete")]
    [InlineData("dense_rank")]
    [InlineData("desc")]
    [InlineData("describe")]
    [InlineData("deterministic")]
    [InlineData("distinct")]
    [InlineData("distinctrow")]
    [InlineData("div")]
    [InlineData("double")]
    [InlineData("drop")]
    [InlineData("dual")]
    [InlineData("each")]
    [InlineData("else")]
    [InlineData("elseif")]
    [InlineData("empty")]
    [InlineData("enclosed")]
    [InlineData("escaped")]
    [InlineData("except")]
    [InlineData("exists")]
    [InlineData("exit")]
    [InlineData("explain")]
    [InlineData("false")]
    [InlineData("fetch")]
    [InlineData("float")]
    [InlineData("float4")]
    [InlineData("float8")]
    [InlineData("for")]
    [InlineData("force")]
    [InlineData("foreign")]
    [InlineData("from")]
    [InlineData("fulltext")]
    [InlineData("function")]
    [InlineData("generated")]
    [InlineData("get")]
    [InlineData("grant")]
    [InlineData("group")]
    [InlineData("grouping")]
    [InlineData("groups")]
    [InlineData("having")]
    [InlineData("high_priority")]
    [InlineData("hour_microsecond")]
    [InlineData("hour_minute")]
    [InlineData("hour_second")]
    [InlineData("if")]
    [InlineData("ignore")]
    [InlineData("in")]
    [InlineData("index")]
    [InlineData("infile")]
    [InlineData("inner")]
    [InlineData("inout")]
    [InlineData("insensitive")]
    [InlineData("insert")]
    [InlineData("int")]
    [InlineData("int1")]
    [InlineData("int2")]
    [InlineData("int3")]
    [InlineData("int4")]
    [InlineData("int8")]
    [InlineData("integer")]
    [InlineData("intersect")]
    [InlineData("interval")]
    [InlineData("into")]
    [InlineData("is")]
    [InlineData("iterate")]
    [InlineData("join")]
    [InlineData("key")]
    [InlineData("keys")]
    [InlineData("kill")]
    [InlineData("lag")]
    [InlineData("lateral")]
    [InlineData("lead")]
    [InlineData("leading")]
    [InlineData("leave")]
    [InlineData("left")]
    [InlineData("like")]
    [InlineData("limit")]
    [InlineData("linear")]
    [InlineData("lines")]
    [InlineData("load")]
    [InlineData("localtime")]
    [InlineData("localtimestamp")]
    [InlineData("lock")]
    [InlineData("long")]
    [InlineData("longblob")]
    [InlineData("longtext")]
    [InlineData("loop")]
    [InlineData("low_priority")]
    [InlineData("match")]
    [InlineData("maxvalue")]
    [InlineData("mediumblob")]
    [InlineData("mediumint")]
    [InlineData("mediumtext")]
    [InlineData("middleint")]
    [InlineData("minute_microsecond")]
    [InlineData("minute_second")]
    [InlineData("mod")]
    [InlineData("modifies")]
    [InlineData("natural")]
    [InlineData("no_write_to_binlog")]
    [InlineData("not")]
    [InlineData("null")]
    [InlineData("numeric")]
    [InlineData("of")]
    [InlineData("offset")]
    [InlineData("on")]
    [InlineData("optimize")]
    [InlineData("optimizer_costs")]
    [InlineData("option")]
    [InlineData("optionally")]
    [InlineData("or")]
    [InlineData("order")]
    [InlineData("out")]
    [InlineData("outer")]
    [InlineData("outfile")]
    [InlineData("over")]
    [InlineData("partition")]
    [InlineData("precision")]
    [InlineData("primary")]
    [InlineData("procedure")]
    [InlineData("purge")]
    [InlineData("range")]
    [InlineData("rank")]
    [InlineData("read")]
    [InlineData("read_write")]
    [InlineData("reads")]
    [InlineData("real")]
    [InlineData("recursive")]
    [InlineData("references")]
    [InlineData("regexp")]
    [InlineData("release")]
    [InlineData("rename")]
    [InlineData("repeat")]
    [InlineData("replace")]
    [InlineData("require")]
    [InlineData("resignal")]
    [InlineData("restrict")]
    [InlineData("return")]
    [InlineData("returning")]
    [InlineData("revoke")]
    [InlineData("right")]
    [InlineData("rlike")]
    [InlineData("row")]
    [InlineData("row_number")]
    [InlineData("rows")]
    [InlineData("schema")]
    [InlineData("schemas")]
    [InlineData("second_microsecond")]
    [InlineData("select")]
    [InlineData("sensitive")]
    [InlineData("separator")]
    [InlineData("set")]
    [InlineData("show")]
    [InlineData("signal")]
    [InlineData("smallint")]
    [InlineData("spatial")]
    [InlineData("specific")]
    [InlineData("sql")]
    [InlineData("sql_big_result")]
    [InlineData("sql_calc_found_rows")]
    [InlineData("sql_small_result")]
    [InlineData("sqlexception")]
    [InlineData("sqlstate")]
    [InlineData("sqlwarning")]
    [InlineData("ssl")]
    [InlineData("starting")]
    [InlineData("stored")]
    [InlineData("straight_join")]
    [InlineData("table")]
    [InlineData("terminated")]
    [InlineData("then")]
    [InlineData("tinyblob")]
    [InlineData("tinyint")]
    [InlineData("tinytext")]
    [InlineData("to")]
    [InlineData("trailing")]
    [InlineData("trigger")]
    [InlineData("true")]
    [InlineData("undo")]
    [InlineData("union")]
    [InlineData("unique")]
    [InlineData("unlock")]
    [InlineData("unsigned")]
    [InlineData("update")]
    [InlineData("usage")]
    [InlineData("use")]
    [InlineData("using")]
    [InlineData("utc_date")]
    [InlineData("utc_time")]
    [InlineData("utc_timestamp")]
    [InlineData("values")]
    [InlineData("varbinary")]
    [InlineData("varchar")]
    [InlineData("varcharacter")]
    [InlineData("varying")]
    [InlineData("virtual")]
    [InlineData("when")]
    [InlineData("where")]
    [InlineData("while")]
    [InlineData("window")]
    [InlineData("with")]
    [InlineData("write")]
    [InlineData("xor")]
    [InlineData("year_month")]
    [InlineData("zerofill")]
    public void AllMySqlReservedWords_AreFlaggedReserved(string word)
    {
        Assert.True(DialectReservedWords.IsReservedInDialect(word, "mysql"));
    }



    [Theory]
    [InlineData("add")]
    [InlineData("all")]
    [InlineData("alter")]
    [InlineData("and")]
    [InlineData("any")]
    [InlineData("as")]
    [InlineData("asc")]
    [InlineData("authorization")]
    [InlineData("backup")]
    [InlineData("begin")]
    [InlineData("between")]
    [InlineData("break")]
    [InlineData("browse")]
    [InlineData("bulk")]
    [InlineData("by")]
    [InlineData("cascade")]
    [InlineData("case")]
    [InlineData("check")]
    [InlineData("checkpoint")]
    [InlineData("close")]
    [InlineData("clustered")]
    [InlineData("coalesce")]
    [InlineData("collate")]
    [InlineData("column")]
    [InlineData("commit")]
    [InlineData("compute")]
    [InlineData("constraint")]
    [InlineData("contains")]
    [InlineData("containstable")]
    [InlineData("continue")]
    [InlineData("convert")]
    [InlineData("create")]
    [InlineData("cross")]
    [InlineData("current")]
    [InlineData("current_date")]
    [InlineData("current_time")]
    [InlineData("current_timestamp")]
    [InlineData("current_user")]
    [InlineData("cursor")]
    [InlineData("database")]
    [InlineData("dbcc")]
    [InlineData("deallocate")]
    [InlineData("declare")]
    [InlineData("default")]
    [InlineData("delete")]
    [InlineData("deny")]
    [InlineData("desc")]
    [InlineData("disk")]
    [InlineData("distinct")]
    [InlineData("distributed")]
    [InlineData("double")]
    [InlineData("drop")]
    [InlineData("dump")]
    [InlineData("else")]
    [InlineData("end")]
    [InlineData("errlvl")]
    [InlineData("escape")]
    [InlineData("except")]
    [InlineData("exec")]
    [InlineData("execute")]
    [InlineData("exists")]
    [InlineData("exit")]
    [InlineData("external")]
    [InlineData("fetch")]
    [InlineData("file")]
    [InlineData("fillfactor")]
    [InlineData("for")]
    [InlineData("foreign")]
    [InlineData("freetext")]
    [InlineData("freetexttable")]
    [InlineData("from")]
    [InlineData("full")]
    [InlineData("function")]
    [InlineData("goto")]
    [InlineData("grant")]
    [InlineData("group")]
    [InlineData("having")]
    [InlineData("holdlock")]
    [InlineData("identity")]
    [InlineData("identity_insert")]
    [InlineData("identitycol")]
    [InlineData("if")]
    [InlineData("in")]
    [InlineData("index")]
    [InlineData("inner")]
    [InlineData("insert")]
    [InlineData("intersect")]
    [InlineData("into")]
    [InlineData("is")]
    [InlineData("join")]
    [InlineData("key")]
    [InlineData("kill")]
    [InlineData("left")]
    [InlineData("like")]
    [InlineData("lineno")]
    [InlineData("load")]
    [InlineData("merge")]
    [InlineData("national")]
    [InlineData("nocheck")]
    [InlineData("nonclustered")]
    [InlineData("not")]
    [InlineData("null")]
    [InlineData("nullif")]
    [InlineData("of")]
    [InlineData("off")]
    [InlineData("offsets")]
    [InlineData("on")]
    [InlineData("open")]
    [InlineData("opendatasource")]
    [InlineData("openquery")]
    [InlineData("openrowset")]
    [InlineData("openxml")]
    [InlineData("option")]
    [InlineData("or")]
    [InlineData("order")]
    [InlineData("outer")]
    [InlineData("over")]
    [InlineData("percent")]
    [InlineData("pivot")]
    [InlineData("plan")]
    [InlineData("precision")]
    [InlineData("primary")]
    [InlineData("print")]
    [InlineData("proc")]
    [InlineData("procedure")]
    [InlineData("public")]
    [InlineData("raiserror")]
    [InlineData("read")]
    [InlineData("readtext")]
    [InlineData("reconfigure")]
    [InlineData("references")]
    [InlineData("replication")]
    [InlineData("restore")]
    [InlineData("restrict")]
    [InlineData("return")]
    [InlineData("revert")]
    [InlineData("revoke")]
    [InlineData("right")]
    [InlineData("rollback")]
    [InlineData("rowcount")]
    [InlineData("rowguidcol")]
    [InlineData("rule")]
    [InlineData("save")]
    [InlineData("schema")]
    [InlineData("securityaudit")]
    [InlineData("select")]
    [InlineData("session_user")]
    [InlineData("set")]
    [InlineData("setuser")]
    [InlineData("shutdown")]
    [InlineData("some")]
    [InlineData("statistics")]
    [InlineData("system_user")]
    [InlineData("table")]
    [InlineData("tablesample")]
    [InlineData("textsize")]
    [InlineData("then")]
    [InlineData("to")]
    [InlineData("top")]
    [InlineData("tran")]
    [InlineData("transaction")]
    [InlineData("trigger")]
    [InlineData("truncate")]
    [InlineData("try_convert")]
    [InlineData("tsequal")]
    [InlineData("union")]
    [InlineData("unique")]
    [InlineData("unpivot")]
    [InlineData("update")]
    [InlineData("use")]
    [InlineData("user")]
    [InlineData("values")]
    [InlineData("varying")]
    [InlineData("view")]
    [InlineData("waitfor")]
    [InlineData("when")]
    [InlineData("where")]
    [InlineData("while")]
    [InlineData("with")]
    [InlineData("writetext")]
    public void AllSqlServerReservedWords_AreFlaggedReserved(string word)
    {
        Assert.True(DialectReservedWords.IsReservedInDialect(word, "sqlserver"));
    }



    [Theory]
    [InlineData("add")]
    [InlineData("all")]
    [InlineData("alter")]
    [InlineData("and")]
    [InlineData("as")]
    [InlineData("autoincrement")]
    [InlineData("between")]
    [InlineData("case")]
    [InlineData("check")]
    [InlineData("collate")]
    [InlineData("commit")]
    [InlineData("constraint")]
    [InlineData("create")]
    [InlineData("default")]
    [InlineData("deferrable")]
    [InlineData("delete")]
    [InlineData("distinct")]
    [InlineData("drop")]
    [InlineData("else")]
    [InlineData("escape")]
    [InlineData("except")]
    [InlineData("exists")]
    [InlineData("foreign")]
    [InlineData("from")]
    [InlineData("group")]
    [InlineData("having")]
    [InlineData("in")]
    [InlineData("index")]
    [InlineData("insert")]
    [InlineData("intersect")]
    [InlineData("into")]
    [InlineData("is")]
    [InlineData("isnull")]
    [InlineData("join")]
    [InlineData("limit")]
    [InlineData("not")]
    [InlineData("nothing")]
    [InlineData("notnull")]
    [InlineData("null")]
    [InlineData("on")]
    [InlineData("or")]
    [InlineData("order")]
    [InlineData("primary")]
    [InlineData("references")]
    [InlineData("returning")]
    [InlineData("select")]
    [InlineData("set")]
    [InlineData("table")]
    [InlineData("then")]
    [InlineData("to")]
    [InlineData("transaction")]
    [InlineData("union")]
    [InlineData("unique")]
    [InlineData("update")]
    [InlineData("using")]
    [InlineData("values")]
    [InlineData("when")]
    [InlineData("where")]
    public void AllSqliteReservedWords_AreFlaggedReserved(string word)
    {
        Assert.True(DialectReservedWords.IsReservedInDialect(word, "sqlite"));
    }



    // ── RequiresQuotingForCase: PostgreSQL-only unquoted-identifier case fold.

    [Fact]
    public void RequiresQuotingForCase_EmptyOrNullInputs_ReturnsFalse()
    {
        Assert.False(DialectReservedWords.RequiresQuotingForCase("", "postgres"));
        Assert.False(DialectReservedWords.RequiresQuotingForCase("Name", null));
    }

    [Fact]
    public void RequiresQuotingForCase_NonPostgresDialect_ReturnsFalseEvenWithUppercase()
    {
        Assert.False(DialectReservedWords.RequiresQuotingForCase("OrderNumber", "mysql"));
        Assert.False(DialectReservedWords.RequiresQuotingForCase("OrderNumber", "sqlserver"));
        Assert.False(DialectReservedWords.RequiresQuotingForCase("OrderNumber", "sqlite"));
    }

    [Fact]
    public void RequiresQuotingForCase_PostgresAllLowercase_ReturnsFalse()
    {
        Assert.False(DialectReservedWords.RequiresQuotingForCase("ordernumber", "postgres"));
    }

    [Fact]
    public void RequiresQuotingForCase_PostgresWithUppercase_ReturnsTrue()
    {
        Assert.True(DialectReservedWords.RequiresQuotingForCase("OrderNumber", "postgres"));
    }
}
