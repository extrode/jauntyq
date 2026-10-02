using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class UserTypeSchemaRoundTripTests
{
    [Fact]
    public void ADomainsPrecisionAndScale_SurviveASnapshotRoundTrip()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.UserTypes["money_amount"] = new UserTypeSchema
        {
            Name = "money_amount",
            Kind = UserTypeKind.Domain,
            UnderlyingDbType = "numeric",
            Precision = 12,
            Scale = 2,
        };

        UserTypeSchema reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema)).UserTypes["money_amount"];

        Assert.Equal(12, reloaded.Precision);
        Assert.Equal(2, reloaded.Scale);
    }
}
