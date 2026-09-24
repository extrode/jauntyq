using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class QueryValidatorPart2MutationCoverageTests
{
    private static ColumnSchema Col(string name, string dbType = "int", bool pk = false,
        int? maxLength = null, int? precision = null, int? scale = null) =>
        new() { Name = name, DbType = dbType, IsPrimaryKey = pk, MaxLength = maxLength, Precision = precision, Scale = scale };

    private static IndexSchema Index(string name, bool unique, params string[] columns) =>
        new() { Name = name, IsUnique = unique, Columns = new List<string>(columns) };

    private static TableSchema Table(string name, ColumnSchema[] columns, params IndexSchema[] indexes)
    {
        var table = new TableSchema { Name = name, Indexes = new List<IndexSchema>(indexes) };
        foreach (var c in columns)
            table.Columns[c.Name] = c;
        return table;
    }

    private static DatabaseSchema Schema(string dialect, params TableSchema[] tables)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        foreach (var t in tables)
            schema.Tables[t.Name] = t;
        return schema;
    }

    private static QueryModel Parse(string sql) =>
        SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), "TestQuery");

    private static List<ValidationError> Validate(string sql, DatabaseSchema schema) =>
        QueryValidator.Validate(Parse(sql), schema);

    private static List<string> Messages(List<ValidationError> errors, string code) =>
        errors.Where(e => e.Code == code).Select(e => e.Message).ToList();

    private static DatabaseSchema ForeignKeySchema()
    {
        var schema = Schema("sqlserver",
            Table("products", new[] { Col("product_id", pk: true), Col("product_name", "nvarchar"), Col("category_id") }),
            Table("categories", new[] { Col("category_id", pk: true), Col("category_name", "nvarchar") }));
        schema.ForeignKeys.Add(new ForeignKeySchema
        {
            FromTable = "products", FromColumn = "category_id", ToTable = "categories", ToColumn = "category_id"
        });
        return schema;
    }

    private static DatabaseSchema IndexedSchema(params TableSchema[] extra)
    {
        var schema = Schema("sqlserver",
            Table("t", new[] { Col("id", pk: true), Col("a"), Col("b"), Col("c"), Col("d"), Col("u") },
                Index("ix_ab", false, "a", "b"), Index("ix_u", true, "u")),
            Table("s", new[] { Col("id", pk: true), Col("tid"), Col("c") },
                Index("ix_tid", false, "tid")));
        foreach (var t in extra)
            schema.Tables[t.Name] = t;
        return schema;
    }

    private static string Unindexed(string table, string column) =>
        $"No index covers {table}.{column} used as a filter/join key: this query scans. Add an index or filter on an indexed column.";

    private static string Unordered(string table) =>
        $"{table} is paginated (LIMIT/OFFSET) with no ORDER BY: the rows in a page are an arbitrary subset and can differ between requests. Add an ORDER BY, tiebroken on a unique column.";

    private static string NotUnique(string table, string sorted) =>
        $"{table} is paginated (LIMIT/OFFSET) but ORDER BY {sorted} is not unique: rows that sort equally have no defined order, so one can appear on two pages or on none. Add a tiebreak on a unique column (typically the primary key).";

    [Fact]
    public void CartesianProduct_ReportsTableAndJoinCounts()
    {
        var errors = Validate("select x.id from t x, s y", IndexedSchema());

        Assert.Equal(
            new[] { "2 tables but only 0 join condition(s) found: every unlinked table multiplies the result set (cartesian product)." },
            Messages(errors, "JNT8001"));
    }

    [Fact]
    public void SingleTable_NoCartesianWarning()
    {
        var errors = Validate("select id from t", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8001"));
    }

    [Fact]
    public void JoinWithoutForeignKey_ReportsExactMessage()
    {
        var errors = Validate(
            "select p.product_id from products p join categories c on p.product_name = c.category_name",
            ForeignKeySchema());

        Assert.Equal(
            new[] { "Join products.product_name = categories.category_name matches no declared foreign key: verify the join columns (a wrong-column join returns wrong or slow results)." },
            Messages(errors, "JNT8006"));
    }

    [Theory]
    [InlineData("p.product_name = c.category_id")]
    [InlineData("c.category_name = p.product_name")]
    [InlineData("c.category_id = p.product_name")]
    public void JoinSharingOnlyPartOfAForeignKey_StillReported(string condition)
    {
        var errors = Validate(
            $"select p.product_id from products p join categories c on {condition}",
            ForeignKeySchema());

        Assert.Single(Messages(errors, "JNT8006"));
    }

    [Fact]
    public void JoinMatchingTheForeignKeyInReverse_NotReported()
    {
        var errors = Validate(
            "select p.product_id from products p join categories c on c.category_id = p.category_id",
            ForeignKeySchema());

        Assert.Empty(Messages(errors, "JNT8006"));
    }

    [Fact]
    public void JoinWithAmbiguousUnqualifiedSide_SkippedWithoutThrowing()
    {
        var errors = Validate(
            "select p.product_id from products p join categories c on category_id = c.category_id",
            ForeignKeySchema());

        Assert.Empty(Messages(errors, "JNT8006"));
    }

    [Fact]
    public void TwoDifferentUnmatchedJoins_BothReported()
    {
        var errors = Validate(
            "select p.product_id from products p join categories c on p.product_name = c.category_name join categories c2 on p.product_id = c2.category_name",
            ForeignKeySchema());

        Assert.Equal(2, Messages(errors, "JNT8006").Count);
    }

    [Fact]
    public void SameUnmatchedJoinTwice_ReportedOnce()
    {
        var errors = Validate(
            "select p.product_id from products p join categories c on p.product_name = c.category_name and p.product_name = c.category_name",
            ForeignKeySchema());

        Assert.Single(Messages(errors, "JNT8006"));
    }

    [Fact]
    public void FunctionOnUnresolvableColumn_SkippedWithoutThrowing()
    {
        var errors = Validate("select id from t where upper(x.bogus) = @p", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8002"));
    }

    [Fact]
    public void LeadingWildcardLike_ReportsExactMessageAndNoUnindexedWarning()
    {
        var errors = Validate("select id from t where t.d like '%abc'", IndexedSchema());

        Assert.Equal(
            new[] { "LIKE '%abc' on t.d cannot seek an index (leading wildcard forces a scan)." },
            Messages(errors, "JNT8003"));
        Assert.Empty(Messages(errors, "JNT8004"));
    }

    [Theory]
    [InlineData("select x.id from t x where x.a = @a and x.b = @b")]
    [InlineData("select id from t where a = @a and b = @b")]
    [InlineData("select x.id from t x join s y on x.a = y.id where x.b = @b")]
    [InlineData("select x.id from t x join s y on y.id = x.a where x.b = @b")]
    public void SecondCompositeColumn_CoveredWhenLeadingColumnFilteredOnSameInstance(string sql)
    {
        var errors = Validate(sql, IndexedSchema());

        Assert.DoesNotContain(Unindexed("t", "b"), Messages(errors, "JNT8004"));
    }

    [Fact]
    public void ColumnComparedToColumn_CountsAsFilterOnLeadingColumn()
    {
        var errors = Validate("select x.id from t x where x.a = x.d and x.b = @b", IndexedSchema());

        Assert.Equal(new[] { Unindexed("t", "d") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void WriteTargetParameter_IsNotAFilterColumn()
    {
        var errors = Validate("update t set a = @a where b = @b", IndexedSchema());

        Assert.Equal(new[] { Unindexed("t", "b") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void WriteTargetParameter_DoesNotCoverAUniqueKey()
    {
        var errors = Validate("update t set u = @u where d = @d", IndexedSchema());

        Assert.Equal(new[] { Unindexed("t", "d") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void AmbiguousUnqualifiedFilterAndOrderColumn_SkippedWithoutThrowing()
    {
        var errors = Validate(
            "select x.id from t x join s y on x.id = y.tid where c = @c order by c",
            IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8007"));
        Assert.DoesNotContain(errors, e => e.Code == "JNT8004" && e.Message.Contains(".c "));
    }

    [Fact]
    public void OrderByProjectedAliasNamedLikeAColumn_NotReported()
    {
        var errors = Validate("select upper(t.a) as d from t order by d", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8007"));
    }

    [Fact]
    public void OrderByUnindexedColumn_ReportsExactMessage()
    {
        var errors = Validate("select id from t order by d", IndexedSchema());

        Assert.Equal(
            new[] { "ORDER BY t.d has no supporting index: this query sorts at runtime. Add an index whose leading column is d, or accept the sort." },
            Messages(errors, "JNT8007"));
    }

    [Fact]
    public void OrderByTwoUnindexedColumns_BothReported()
    {
        var errors = Validate("select id from t order by c, d", IndexedSchema());

        Assert.Equal(2, Messages(errors, "JNT8007").Count);
    }

    [Fact]
    public void OrderBySameUnindexedColumnTwice_ReportedOnce()
    {
        var errors = Validate("select id from t order by d, d", IndexedSchema());

        Assert.Single(Messages(errors, "JNT8007"));
    }

    [Theory]
    [InlineData("select x.id from t x where x.a = @a order by x.b")]
    [InlineData("select id from t where a = @a order by b")]
    public void OrderBySecondCompositeColumn_CoveredWhenLeadingColumnFiltered(string sql)
    {
        var errors = Validate(sql, IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8007"));
    }

    [Theory]
    [InlineData("select x.id from t x where x.id = @id and x.d = @d")]
    [InlineData("select id from t where id = @id and d = @d")]
    [InlineData("select y.id from s y join t x on y.tid = x.id where x.d = @d")]
    [InlineData("select y.id from s y join t x on x.id = y.tid where x.d = @d")]
    public void ResidualFilter_SilentBesideAPrimaryKeySeekOnSameInstance(string sql)
    {
        var errors = Validate(sql, IndexedSchema());

        Assert.DoesNotContain(Unindexed("t", "d"), Messages(errors, "JNT8004"));
    }

    [Fact]
    public void SameUnindexedColumnFilteredTwice_ReportedOnceWithExactMessage()
    {
        var errors = Validate("select id from t where d = @d1 or d = @d2", IndexedSchema());

        Assert.Equal(new[] { Unindexed("t", "d") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void ExpressionIndex_DoesNotSupportItsRealColumn()
    {
        var expressionIndex = Index("ix_expr", false, "d");
        expressionIndex.HasExpressionKeyPart = true;
        var schema = IndexedSchema(Table("e", new[] { Col("id", pk: true), Col("d") }, expressionIndex));

        var errors = Validate("select id from e where d = @d", schema);

        Assert.Equal(new[] { Unindexed("e", "d") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void TableWithoutPrimaryKey_ResidualFilterStillReported()
    {
        var schema = IndexedSchema(Table("n", new[] { Col("a"), Col("b") }, Index("ix_a", false, "a")));

        var errors = Validate("select a from n where b = @b", schema);

        Assert.Equal(new[] { Unindexed("n", "b") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void UniqueIndexWithNoColumns_CoversNothing()
    {
        var schema = IndexedSchema(Table("z", new[] { Col("id", pk: true), Col("d") }, Index("ix_empty", true)));

        var errors = Validate("select id from z where d = @d", schema);

        Assert.Equal(new[] { Unindexed("z", "d") }, Messages(errors, "JNT8004"));
    }

    [Fact]
    public void PaginatedQueryWithAJoin_NotChecked()
    {
        var query = Parse("select id from t limit 5");
        query.Joins.Add(new JoinRef { LeftTable = "t", LeftColumn = "id", RightTable = "t", RightColumn = "id" });

        var errors = QueryValidator.Validate(query, IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8010"));
    }

    [Fact]
    public void PaginatedQueryWithNoTable_NotChecked()
    {
        var errors = Validate("select 1 as one limit 1", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8010"));
    }

    [Fact]
    public void PaginatedQueryOnUnknownTable_NotChecked()
    {
        var errors = Validate("select id from missing limit 5", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8010"));
    }

    [Theory]
    [InlineData("select x.id from t x where x.id = @id limit 1")]
    [InlineData("select id from t where id = @id limit 1")]
    public void PaginatedQueryPinnedToOneRow_NotReported(string sql)
    {
        var errors = Validate(sql, IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8010"));
    }

    [Fact]
    public void PaginatedQueryWithoutOrder_ReportsExactMessage()
    {
        var errors = Validate("select id from t limit 5", IndexedSchema());

        Assert.Equal(new[] { Unordered("t") }, Messages(errors, "JNT8010"));
    }

    [Fact]
    public void TwoUnorderedPagesOnDifferentTables_BothReported()
    {
        var errors = Validate(
            "with a as (select id from t limit 5), b as (select id from s limit 3) select a.id from a join b on a.id = b.id",
            IndexedSchema());

        Assert.Equal(new[] { Unordered("t"), Unordered("s") }, Messages(errors, "JNT8010"));
    }

    [Fact]
    public void TwoUnorderedPagesOnSameTable_ReportedOnce()
    {
        var errors = Validate(
            "with a as (select id from t limit 5), b as (select id from t limit 3) select a.id from a join b on a.id = b.id",
            IndexedSchema());

        Assert.Equal(new[] { Unordered("t") }, Messages(errors, "JNT8010"));
    }

    [Fact]
    public void PaginatedOrderByUnresolvableColumn_NotReported()
    {
        var errors = Validate("select id from t order by bogus limit 10", IndexedSchema());

        Assert.Empty(Messages(errors, "JNT8009"));
    }

    [Fact]
    public void PaginatedOrderByNonUniqueColumns_ReportsExactMessage()
    {
        var errors = Validate("select id from t order by c, d limit 10", IndexedSchema());

        Assert.Equal(new[] { NotUnique("t", "c, d") }, Messages(errors, "JNT8009"));
    }

    [Fact]
    public void TwoNonUniquePagesOnDifferentTables_BothReported()
    {
        var errors = Validate(
            "with a as (select id from t order by d limit 5), b as (select id from s order by c limit 3) select a.id from a join b on a.id = b.id",
            IndexedSchema());

        Assert.Equal(new[] { NotUnique("t", "d"), NotUnique("s", "c") }, Messages(errors, "JNT8009"));
    }

    [Fact]
    public void TwoNonUniquePagesOnSameTable_ReportedOnce()
    {
        var errors = Validate(
            "with a as (select id from t order by d limit 5), b as (select id from t order by d limit 3) select a.id from a join b on a.id = b.id",
            IndexedSchema());

        Assert.Equal(new[] { NotUnique("t", "d") }, Messages(errors, "JNT8009"));
    }

    private static List<ValidationError> ValidateLiteral(string dialect, ColumnSchema column, string literal) =>
        Validate($"select id from t where {column.Name} = {literal}",
            Schema(dialect, Table("t", new[] { Col("id", pk: true), column })));

    [Fact]
    public void StringLiteralOnZeroMaxLength_NotChecked()
    {
        var errors = ValidateLiteral("sqlserver", Col("s", "nvarchar", maxLength: 0), "'a'");

        Assert.Empty(Messages(errors, "JNT5001"));
    }

    [Fact]
    public void StringLiteralTooLong_ReportsExactMessage()
    {
        var errors = ValidateLiteral("sqlserver", Col("s", "nvarchar", maxLength: 2), "'abc'");

        Assert.Equal(
            new[] { "String literal (3 chars) exceeds t.s max length of 2. It would truncate on write and can never match on read." },
            Messages(errors, "JNT5001"));
    }

    [Fact]
    public void EscapedQuote_CountsAsOneCharacter()
    {
        var errors = ValidateLiteral("sqlserver", Col("s", "nvarchar", maxLength: 2), "'a''b'");

        Assert.Equal(
            new[] { "String literal (3 chars) exceeds t.s max length of 2. It would truncate on write and can never match on read." },
            Messages(errors, "JNT5001"));
    }

    [Fact]
    public void PostgresAsciiLiteral_CountsEveryCharacter()
    {
        var errors = ValidateLiteral("postgres", Col("s", "varchar", maxLength: 2), "'abc'");

        Assert.Single(Messages(errors, "JNT5001"));
    }

    [Fact]
    public void PostgresLiteralEndingInLoneHighSurrogate_CountedWithoutThrowing()
    {
        var errors = ValidateLiteral("postgres", Col("s", "varchar", maxLength: 1), "'a\uD83D'");

        Assert.Equal(
            new[] { "String literal (2 chars) exceeds t.s max length of 1. It would truncate on write and can never match on read." },
            Messages(errors, "JNT5001"));
    }

    [Fact]
    public void UnparsableNumericLiteral_NotChecked()
    {
        var errors = ValidateLiteral("postgres", Col("flag", "bit"), "1e5");

        Assert.Empty(errors);
    }

    [Fact]
    public void PostgresBitWithPrecision_ReportsOnlyTheTypeMismatch()
    {
        var errors = ValidateLiteral("postgres", Col("flag", "bit", precision: 1), "12");

        Assert.Equal(new[] { "JNT5003" }, errors.Select(e => e.Code));
        Assert.Equal(
            "Numeric literal 12 cannot be assigned to t.flag (bit): PostgreSQL's bit is a bit string, not a number. Write a bit-string literal such as B'1010'. The server rejects this statement at runtime.",
            errors[0].Message);
    }

    [Fact]
    public void SqlServerBitWithPrecision_ReportsOnlyTheCoercion()
    {
        var errors = ValidateLiteral("sqlserver", Col("flag", "bit", precision: 1), "12");

        Assert.Equal(
            new[] { "Numeric literal 12 does not fit t.flag (bit): SQL Server's bit holds only 0 or 1 and silently coerces any other number to 1, so this would be written as 1 rather than 12." },
            Messages(errors, "JNT5002"));
    }

    [Fact]
    public void IntegerAtItsMinimum_Fits()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "tinyint"), "0");

        Assert.Empty(Messages(errors, "JNT5002"));
    }

    [Fact]
    public void DecimalWithZeroPrecision_NotChecked()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "decimal", precision: 0), "5");

        Assert.Empty(Messages(errors, "JNT5002"));
    }

    [Fact]
    public void FractionOnlyLiteral_HasNoIntegerDigits()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "decimal", precision: 2, scale: 2), "0.5");

        Assert.Empty(Messages(errors, "JNT5002"));
    }

    [Fact]
    public void TooManyIntegerDigits_ReportedOnceWithExactMessage()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "decimal", precision: 4, scale: 2), "123.456");

        Assert.Equal(
            new[] { "Numeric literal 123.456 does not fit t.n (decimal, precision 4, scale 2): at most 2 digit(s) before the decimal point. It would overflow at runtime." },
            Messages(errors, "JNT5002"));
    }

    [Fact]
    public void ScaleBeyondDecimalRange_NotChecked()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "decimal", precision: 38, scale: 30), "1.5");

        Assert.Empty(Messages(errors, "JNT5002"));
    }

    [Fact]
    public void FractionOnScaleZero_ReportsExactMessage()
    {
        var errors = ValidateLiteral("sqlserver", Col("n", "decimal", precision: 5, scale: 0), "1.5");

        Assert.Equal(
            new[] { "Numeric literal 1.5 does not fit t.n (decimal, precision 5, scale 0): at most 0 digit(s) after the decimal point. It would be rounded on write and can never match on read." },
            Messages(errors, "JNT5002"));
    }

    [Theory]
    [InlineData("postgres", "int4", "3000000000", true)]
    [InlineData("postgres", "integer", "3000000000", true)]
    [InlineData("postgres", "serial", "3000000000", true)]
    [InlineData("postgres", "bigint", "10000000000000000000", true)]
    [InlineData("postgres", "int8", "10000000000000000000", true)]
    [InlineData("postgres", "bigserial", "10000000000000000000", true)]
    [InlineData("postgres", "serial8", "10000000000000000000", true)]
    [InlineData("postgres", "smallserial", "40000", true)]
    [InlineData("mysql", "int", "3000000000", true)]
    [InlineData("mysql", "int unsigned", "3000000000", false)]
    [InlineData("mysql", "bigint unsigned", "10000000000000000000", false)]
    [InlineData("mysql", "smallint unsigned", "40000", false)]
    [InlineData("mysql", "mediumint", "9000000", true)]
    public void IntegerFamilyRange(string dialect, string dbType, string literal, bool outOfRange)
    {
        var errors = ValidateLiteral(dialect, Col("n", dbType), literal);

        Assert.Equal(outOfRange ? 1 : 0, Messages(errors, "JNT5002").Count);
    }

    [Fact]
    public void MySqlBit64_RangedToTwoToTheSixtyFourMinusOne()
    {
        var errors = ValidateLiteral("mysql", Col("n", "bit", precision: 64), "18446744073709551616");

        Assert.Single(Messages(errors, "JNT5002"));
    }
}
