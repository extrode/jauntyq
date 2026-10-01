using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Generator.Directives;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CodeEmitterParameterInferenceTests
{
    private static DatabaseSchema Schema()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["s"] = Table("s", ("id", "integer", true), ("a", "bigint", false));
        schema.Tables["t"] = Table("t", ("id", "integer", true), ("a", "integer", false), ("note", "text", false));
        schema.Tables["u"] = Table("u", ("id", "integer", true), ("only_u", "smallint", false));
        return schema;
    }

    private static TableSchema Table(string name, params (string Name, string DbType, bool Pk)[] cols)
    {
        var t = new TableSchema { Name = name };
        foreach (var c in cols)
            t.Columns[c.Name] = new ColumnSchema { Name = c.Name, DbType = c.DbType, IsPrimaryKey = c.Pk, IsIdentity = c.Pk };
        return t;
    }

    private static QueryModel Query(StatementType type, string? target, params (string Table, string Alias)[] tables)
    {
        var q = new QueryModel { Name = "Q", StatementType = type, TargetTable = target };
        foreach (var t in tables)
            q.Tables.Add(new TableRef { TableName = t.Table, Alias = t.Alias });
        return q;
    }

    [Fact]
    public void InferCrudParameterType_NullSchema_FallsBackToObject()
    {
        var param = new ParameterRef { Name = "a", BoundColumnName = "a" };
        var query = Query(StatementType.Update, "t", ("t", ""));

        Assert.Equal("object", CodeEmitter.InferCrudParameterType(param, query, null));
    }

    [Fact]
    public void EmitCrud_NullSchema_EmitsBoundParametersAsObject()
    {
        var query = Query(StatementType.Update, "t", ("t", ""));
        query.Name = "Touch";
        query.Parameters.Add(new ParameterRef { Name = "a", BoundColumnName = "a" });

        string code = CodeEmitter.EmitCrud(query, "update t set a = @a", "T");

        Assert.Contains("public int Touch(object a)", code);
    }

    [Fact]
    public void InferCrudParameterType_TargetTableColumnWinsOverAnEarlierSourceTable()
    {
        var param = new ParameterRef { Name = "a", BoundColumnName = "a" };
        var query = Query(StatementType.Insert, "t", ("s", ""), ("t", ""));

        Assert.Equal("int", CodeEmitter.InferCrudParameterType(param, query, Schema()));
    }

    [Fact]
    public void InferCrudParameterType_AliasMissStillFindsTheColumnInAnotherReferencedTable()
    {
        var param = new ParameterRef { Name = "p", BoundTableAlias = "x", BoundColumnName = "only_u" };
        var query = Query(StatementType.Select, null, ("t", "x"), ("u", "y"));

        Assert.Equal("short", CodeEmitter.InferCrudParameterType(param, query, Schema()));
    }

    [Fact]
    public void InferCrudParameterType_ExplicitParamsMatchByNameNotPosition()
    {
        var param = new ParameterRef { Name = "Second", BoundColumnName = "a" };
        var directives = new DirectiveModel
        {
            ExplicitParams = new() { new ExplicitParam("First", "int"), new ExplicitParam("second", "string") },
        };

        Assert.Equal("string", CodeEmitter.InferCrudParameterType(param, Query(StatementType.Select, "t", ("t", "")), Schema(), directives));
    }

    [Fact]
    public void IsBoundColumnNullable_UnboundParameter_IsNotNullable()
    {
        var param = new ParameterRef { Name = "Take" };

        Assert.False(CodeEmitter.IsBoundColumnNullable(param, Query(StatementType.Select, null, ("t", "")), Schema()));
    }

    [Fact]
    public void ResolveIdentityInfo_NonInsertStatement_HasNoIdentity()
    {
        var directives = new DirectiveModel { ReturnsIdentity = true };
        var schema = Schema();

        Assert.NotNull(CodeEmitter.ResolveIdentityInfo(Query(StatementType.Insert, "t", ("t", "")), schema, directives));
        Assert.Null(CodeEmitter.ResolveIdentityInfo(Query(StatementType.Update, "t", ("t", "")), schema, directives));
    }

    [Fact]
    public void InferScalarParameterType_ParameterNotInTheQuery_FallsBackToObject()
    {
        var query = Query(StatementType.Select, null, ("t", ""));

        Assert.Equal("object", CodeEmitter.InferScalarParameterType("Missing", query, new ProjectionModel(), Schema()));
    }

    [Fact]
    public void InferScalarParameterType_AliasOfANonSchemaTable_FallsBackToTheProjection()
    {
        var query = Query(StatementType.Select, null, ("nowhere", "n"));
        query.Parameters.Add(new ParameterRef { Name = "a", BoundTableAlias = "n", BoundColumnName = "a" });
        var projection = new ProjectionModel { Columns = { new ProjectionColumn { Name = "A", Type = "decimal" } } };

        Assert.Equal("decimal", CodeEmitter.InferScalarParameterType("a", query, projection, Schema()));
    }

    [Fact]
    public void InferScalarParameterType_QualifiedBinding_UsesTheAliasedTableNotTheFirstMatch()
    {
        var query = Query(StatementType.Select, null, ("t", "x"), ("s", "y"));
        query.Parameters.Add(new ParameterRef { Name = "p", BoundTableAlias = "y", BoundColumnName = "a" });

        Assert.Equal("long", CodeEmitter.InferScalarParameterType("p", query, new ProjectionModel(), Schema()));
    }

    [Fact]
    public void InferScalarParameterType_UnqualifiedBinding_SearchesEveryReferencedTable()
    {
        var query = Query(StatementType.Select, null, ("t", "x"), ("u", ""));
        query.Parameters.Add(new ParameterRef { Name = "p", BoundColumnName = "only_u" });

        Assert.Equal("short", CodeEmitter.InferScalarParameterType("p", query, new ProjectionModel(), Schema()));
    }
}
