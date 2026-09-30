using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class UserTypeResolutionMutationCoverageTests
{
    private static DatabaseSchema SchemaWith(params UserTypeSchema[] types)
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        foreach (var t in types)
            schema.UserTypes[t.Name] = t;
        return schema;
    }

    private static ColumnSchema AddColumn(DatabaseSchema schema, ColumnSchema column)
    {
        if (!schema.Tables.TryGetValue("t", out var table))
        {
            table = new TableSchema { Name = "t" };
            schema.Tables["t"] = table;
        }
        table.Columns[column.Name] = column;
        return column;
    }

    [Fact]
    public void Column_TypedAsDomain_ResolvesToUnderlyingType()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "ssn", Kind = UserTypeKind.Domain, UnderlyingDbType = "varchar", MaxLength = 11 });
        var col = AddColumn(schema, new ColumnSchema { Name = "c", DbType = "ssn" });

        UserTypeResolution.Apply(schema);

        Assert.Equal("varchar", col.DbType);
        Assert.Equal("ssn", col.ResolvedFromUserType);
        Assert.Equal(11, col.MaxLength);
    }

    [Fact]
    public void Column_AlreadyResolved_IsLeftAlone()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "ssn", Kind = UserTypeKind.Domain, UnderlyingDbType = "varchar" });
        var col = AddColumn(schema, new ColumnSchema { Name = "c", DbType = "ssn", ResolvedFromUserType = "outer" });

        UserTypeResolution.Apply(schema);

        Assert.Equal("ssn", col.DbType);
        Assert.Equal("outer", col.ResolvedFromUserType);
    }

    [Fact]
    public void Column_OwnFacets_WinOverDomainFacets()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "money2", Kind = UserTypeKind.Domain, UnderlyingDbType = "numeric" });
        var col = AddColumn(schema, new ColumnSchema { Name = "c", DbType = "money2", MaxLength = 7, Precision = 12, Scale = 2 });

        UserTypeResolution.Apply(schema);

        Assert.Equal(7, col.MaxLength);
        Assert.Equal(12, col.Precision);
        Assert.Equal(2, col.Scale);
    }

    [Fact]
    public void Composite_WithUnderlyingType_IsNotResolved()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "addr", Kind = UserTypeKind.Composite, UnderlyingDbType = "int" });
        var col = AddColumn(schema, new ColumnSchema { Name = "c", DbType = "addr" });

        UserTypeResolution.Apply(schema);

        Assert.Equal("addr", col.DbType);
        Assert.Null(col.ResolvedFromUserType);
    }

    [Fact]
    public void Alias_WithoutUnderlyingType_IsNotResolved()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "bare", Kind = UserTypeKind.Alias, UnderlyingDbType = null });
        var col = AddColumn(schema, new ColumnSchema { Name = "c", DbType = "bare" });

        UserTypeResolution.Apply(schema);

        Assert.Equal("bare", col.DbType);
        Assert.Null(col.ResolvedFromUserType);
    }

    [Fact]
    public void Param_AlreadyResolved_IsLeftAlone()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "ssn", Kind = UserTypeKind.Domain, UnderlyingDbType = "varchar" });
        var param = new FunctionParam { Name = "p", DbType = "ssn", ResolvedFromUserType = "outer" };
        schema.Functions["f"] = new FunctionSchema { Name = "f", Params = { param }, Return = new FunctionReturn { DbType = "int" } };

        UserTypeResolution.Apply(schema);

        Assert.Equal("ssn", param.DbType);
        Assert.Equal("outer", param.ResolvedFromUserType);
    }

    [Fact]
    public void ParamAndReturn_OwnFacets_WinOverDomainFacets()
    {
        var schema = SchemaWith(new UserTypeSchema { Name = "d", Kind = UserTypeKind.Domain, UnderlyingDbType = "numeric" });
        var param = new FunctionParam { Name = "p", DbType = "d", MaxLength = 5, Precision = 9, Scale = 3 };
        var ret = new FunctionReturn { DbType = "d", MaxLength = 6, Precision = 8, Scale = 4 };
        schema.Functions["f"] = new FunctionSchema { Name = "f", Params = { param }, Return = ret };

        UserTypeResolution.Apply(schema);

        Assert.Equal("numeric", param.DbType);
        Assert.Equal((5, 9, 3), (param.MaxLength, param.Precision, param.Scale));
        Assert.Equal("numeric", ret.DbType);
        Assert.Equal((6, 8, 4), (ret.MaxLength, ret.Precision, ret.Scale));
    }

    [Fact]
    public void ParamAndReturn_OuterDomainFacets_WinOverInnerDomainFacets()
    {
        var schema = SchemaWith(
            new UserTypeSchema { Name = "inner", Kind = UserTypeKind.Domain, UnderlyingDbType = "varchar" },
            new UserTypeSchema { Name = "outer", Kind = UserTypeKind.Domain, UnderlyingDbType = "inner", MaxLength = 20, Precision = 1, Scale = 1 });
        var param = new FunctionParam { Name = "p", DbType = "outer" };
        var ret = new FunctionReturn { DbType = "outer" };
        schema.Functions["f"] = new FunctionSchema { Name = "f", Params = { param }, Return = ret };

        UserTypeResolution.Apply(schema);

        Assert.Equal("varchar", param.DbType);
        Assert.Equal((20, 1, 1), (param.MaxLength, param.Precision, param.Scale));
        Assert.Equal("varchar", ret.DbType);
        Assert.Equal((20, 1, 1), (ret.MaxLength, ret.Precision, ret.Scale));
    }

    [Fact]
    public void ParamAndReturn_CyclicDomains_StopAfterOneHopPerResolvableType()
    {
        var schema = SchemaWith(
            new UserTypeSchema { Name = "a", Kind = UserTypeKind.Domain, UnderlyingDbType = "b" },
            new UserTypeSchema { Name = "b", Kind = UserTypeKind.Domain, UnderlyingDbType = "a" });
        var param = new FunctionParam { Name = "p", DbType = "a" };
        var ret = new FunctionReturn { DbType = "a" };
        schema.Functions["f"] = new FunctionSchema { Name = "f", Params = { param }, Return = ret };

        UserTypeResolution.Apply(schema);

        Assert.Equal("a", param.DbType);
        Assert.Equal("a", param.ResolvedFromUserType);
        Assert.Equal("a", ret.DbType);
        Assert.Equal("a", ret.ResolvedFromUserType);
    }

    [Fact]
    public void FunctionKey_NoArguments_IsTheBareName()
    {
        Assert.Equal("f", UserTypeResolution.FunctionKey("f", Array.Empty<string>()));
    }

    [Fact]
    public void FunctionKey_WithArguments_ListsTheTypesInParens()
    {
        Assert.Equal("f(int,text)", UserTypeResolution.FunctionKey("f", new[] { "int", "text" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SplitRenderedType_Blank_IsEmpty(string rendered)
    {
        Assert.Equal((string.Empty, (int?)null, (int?)null, (int?)null), UserTypeResolution.SplitRenderedType(rendered));
    }

    [Theory]
    [InlineData("  integer  ", "integer")]
    [InlineData("foo(1) bar", "foo(1) bar")]
    [InlineData("int)", "int)")]
    public void SplitRenderedType_NoTrailingModifierList_IsTheTrimmedWhole(string rendered, string expected)
    {
        Assert.Equal((expected, (int?)null, (int?)null, (int?)null), UserTypeResolution.SplitRenderedType(rendered));
    }

    [Theory]
    [InlineData("numeric(12,2)", "numeric", null, 12, 2)]
    [InlineData("character varying(11)", "character varying", 11, null, null)]
    [InlineData("numeric(5)", "numeric", null, 5, null)]
    [InlineData("DECIMAL(7)", "DECIMAL", null, 7, null)]
    [InlineData("bit varying(3)", "bit varying", 3, null, null)]
    [InlineData("(5)", "", 5, null, null)]
    [InlineData("timestamp(3) with time zone)", "timestamp", null, null, null)]
    [InlineData("numeric(a,b)", "numeric", null, null, null)]
    [InlineData("enum(x,y,z)", "enum", null, null, null)]
    public void SplitRenderedType_ModifierList_IsSplitIntoFacets(string rendered, string bare, int? max, int? prec, int? scale)
    {
        Assert.Equal((bare, max, prec, scale), UserTypeResolution.SplitRenderedType(rendered));
    }
}
