using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// AUD-R1-02. <see cref="UserTypeResolution.Apply"/> resolved a function
/// param/return through <c>resolvable</c> once, not to a fixed point. A
/// DOMAIN declared over another DOMAIN (Postgres <c>CREATE DOMAIN b AS a;
/// CREATE DOMAIN a AS int;</c>, param typed <c>b</c>) left DbType holding
/// <c>a</c> — itself still a user-type name, not the primitive underneath —
/// so DialectMapper fell back to <c>object</c> for it.
///
/// Columns are structurally protected from this (PostgresExtractor
/// pre-populates ResolvedFromUserType on columns before Apply runs, and SQL
/// Server's alias capture excludes user-defined base types), which is why
/// this test targets function params/returns specifically rather than columns.
/// </summary>
public class UserTypeResolutionChainTests
{
    private static DatabaseSchema BuildSchemaWithChainedDomain()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };

        schema.UserTypes["a"] = new UserTypeSchema
        {
            Name = "a",
            Kind = UserTypeKind.Domain,
            UnderlyingDbType = "int"
        };
        schema.UserTypes["b"] = new UserTypeSchema
        {
            Name = "b",
            Kind = UserTypeKind.Domain,
            UnderlyingDbType = "a"
        };

        schema.Functions["get_amount"] = new FunctionSchema
        {
            Name = "get_amount",
            Params = { new FunctionParam { Name = "p1", DbType = "b" } },
            Return = new FunctionReturn { DbType = "b" }
        };

        return schema;
    }

    [Fact]
    public void ChainedDomain_ResolvesParamToThePrimitiveUnderneath()
    {
        var schema = BuildSchemaWithChainedDomain();

        UserTypeResolution.Apply(schema);

        var param = schema.Functions["get_amount"].Params[0];
        Assert.Equal("int", param.DbType);
        Assert.Equal("b", param.ResolvedFromUserType);
    }

    [Fact]
    public void ChainedDomain_ResolvesReturnToThePrimitiveUnderneath()
    {
        var schema = BuildSchemaWithChainedDomain();

        UserTypeResolution.Apply(schema);

        var ret = schema.Functions["get_amount"].Return;
        Assert.Equal("int", ret.DbType);
        Assert.Equal("b", ret.ResolvedFromUserType);
    }

    [Fact]
    public void SingleLevelDomain_StillResolvesAsBefore()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.UserTypes["ssn"] = new UserTypeSchema
        {
            Name = "ssn",
            Kind = UserTypeKind.Domain,
            UnderlyingDbType = "varchar",
            MaxLength = 11
        };
        schema.Functions["lookup"] = new FunctionSchema
        {
            Name = "lookup",
            Params = { new FunctionParam { Name = "p1", DbType = "ssn" } },
            Return = new FunctionReturn { DbType = "ssn" }
        };

        UserTypeResolution.Apply(schema);

        var param = schema.Functions["lookup"].Params[0];
        Assert.Equal("varchar", param.DbType);
        Assert.Equal("ssn", param.ResolvedFromUserType);
        Assert.Equal(11, param.MaxLength);
    }
}
