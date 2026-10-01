using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class FunctionPlanTests
{
    private static FunctionSchema Fn(string name, string? schema = null, string returns = "integer", params FunctionParam[] ps)
    {
        var fn = new FunctionSchema { Name = name, Schema = schema, Return = new FunctionReturn { DbType = returns } };
        fn.Params.AddRange(ps);
        return fn;
    }

    private static FunctionParam P(string name, string type = "integer", string? userType = null)
        => new() { Name = name, DbType = type, ResolvedFromUserType = userType };

    private static DatabaseSchema Schema(string dialect, params FunctionSchema[] fns)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        foreach (var fn in fns)
            schema.Functions[fn.Name + "#" + schema.Functions.Count] = fn;
        return schema;
    }

    [Fact]
    public void Plan_IsSortedByMethodAndSkipsUnusableNamesAndSchemas()
    {
        var schema = Schema("postgres",
            Fn("b_fn", "public"), Fn("a_fn", "public"), Fn("c_fn", "bad schema"), Fn("select", "public"));

        var plan = CodeEmitter.PlanFunctionEmission(schema);

        Assert.Equal(new[] { "AFn", "BFn" }, plan.ConvertAll(f => f.Method));
        Assert.Equal("SELECT public.a_fn()", plan[0].Sql);
    }

    [Fact]
    public void Plan_MySqlIgnoresTheSchemaQualifierEvenWhenItIsNotAnIdentifier()
    {
        var plan = CodeEmitter.PlanFunctionEmission(Schema("mysql", Fn("f_x", "my-db", "int")));

        Assert.Equal("SELECT f_x()", Assert.Single(plan).Sql);
    }

    [Fact]
    public void Plan_ReportsUnmappableFunctionsByDisplayNameAndReason()
    {
        var unmappable = new List<(string Function, string Reason)>();
        var schema = Schema("postgres",
            Fn("g", returns: "weird_type"),
            Fn("h", ps: P("bad name")),
            Fn("k", ps: new[] { P("x"), P("X"), P("_x") }));

        Assert.Empty(CodeEmitter.PlanFunctionEmission(schema, unmappable: unmappable));
        Assert.Equal(new[]
        {
            ("g()", "its return type 'weird_type'"),
            ("h(integer)", "parameter 'bad name' is not a usable identifier"),
            ("k(integer, integer, integer)", "parameter '_x' cannot be given a unique C# name"),
        }, unmappable);
    }

    [Fact]
    public void Plan_TableTypeOrCompositeParameter_IsReportedAsTableValued()
    {
        var tableValued = new List<(string Function, string TypeName)>();
        var schema = Schema("postgres", Fn("tv", ps: P("rows", "integer", "tvp")), Fn("cp", ps: P("addr", "address")));
        schema.UserTypes["tvp"] = new UserTypeSchema { Name = "tvp", Kind = UserTypeKind.TableType };
        schema.UserTypes["address"] = new UserTypeSchema { Name = "address", Kind = UserTypeKind.Composite };

        Assert.Empty(CodeEmitter.PlanFunctionEmission(schema, tableValued: tableValued));
        Assert.Equal(new[] { ("tv(integer)", "tvp"), ("cp(address)", "address") }, tableValued);
    }

    [Fact]
    public void DescribeUserTypeMembers_MissingOrMemberlessType_IsEmpty()
    {
        var schema = new DatabaseSchema();
        schema.UserTypes["bare"] = new UserTypeSchema { Name = "bare", Kind = UserTypeKind.TableType };

        Assert.Equal("", CodeEmitter.DescribeUserTypeMembers(schema, "missing"));
        Assert.Equal("", CodeEmitter.DescribeUserTypeMembers(schema, "bare"));
    }
}
