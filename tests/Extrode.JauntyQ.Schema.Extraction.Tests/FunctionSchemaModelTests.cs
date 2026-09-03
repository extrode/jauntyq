using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Spec 014 / T1: the snapshot model for functions and user-defined types.
/// These cover the JSON contract itself. The serializer is source-generated
/// (<c>SchemaJsonContext</c>) and is read at app runtime under Native AOT
/// through Extrode.JauntyQ.Schema.Contract, so a type missing from the context fails
/// at RUN time rather than build time — which is what the round trips here
/// actually guard, and why every new entity gets one.
/// </summary>
public class FunctionSchemaModelTests
{
    private static DatabaseSchema BuildSchemaWithFunction()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Functions["calculate_tax"] = new FunctionSchema
        {
            Name = "calculate_tax",
            Schema = "public",
            Params =
            {
                new FunctionParam { Name = "amount", DbType = "numeric", Precision = 12, Scale = 2 },
                new FunctionParam { Name = "rate", DbType = "numeric", Precision = 5, Scale = 4 }
            },
            Return = new FunctionReturn { DbType = "numeric", Precision = 12, Scale = 2, IsNullable = false }
        };
        return schema;
    }

    [Fact]
    public void FunctionSurvivesTheRoundTripWithItsParametersInOrder()
    {
        string json = SchemaLoader.Serialize(BuildSchemaWithFunction());
        var reloaded = SchemaLoader.Load(json);

        Assert.True(reloaded.Functions.ContainsKey("calculate_tax"));
        var fn = reloaded.Functions["calculate_tax"];
        Assert.Equal("public", fn.Schema);
        Assert.Equal(new[] { "amount", "rate" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "numeric", "numeric" }, fn.Params.Select(p => p.DbType));
        Assert.Equal(5, fn.Params[1].Precision);
        Assert.Equal(4, fn.Params[1].Scale);
    }

    [Fact]
    public void ParameterOrderIsTheContractAndSurvivesSerialization()
    {
        // The emitted method binds positionally, so a serializer that reordered
        // parameters would swap arguments at every call site while still
        // compiling. Names chosen so the correct order is not also alphabetical
        // or insertion-order-by-accident.
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Functions["f"] = new FunctionSchema
        {
            Name = "f",
            Params =
            {
                new FunctionParam { Name = "zebra", DbType = "int" },
                new FunctionParam { Name = "alpha", DbType = "text" },
                new FunctionParam { Name = "mike", DbType = "bool" }
            },
            Return = new FunctionReturn { DbType = "int" }
        };

        var reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema));

        Assert.Equal(new[] { "zebra", "alpha", "mike" }, reloaded.Functions["f"].Params.Select(p => p.Name));
    }

    [Fact]
    public void ReturnDefaultsToNullableSoTheEmittedTypeIsTheSafeOne()
    {
        // Databases rarely declare return nullability. The default has to be
        // the recoverable direction: a consumer can assert non-null on a
        // nullable return, but cannot recover from a NullReferenceException
        // thrown inside generated code.
        Assert.True(new FunctionReturn().IsNullable);
    }

    [Fact]
    public void AnAliasResolvesToItsPrimitiveAndKeepsTheAliasName()
    {
        // FR-004. Both halves matter: DbType carries the RESOLVED type so every
        // existing mapping rule applies unchanged, and the alias name survives
        // so the snapshot does not claim the column was declared varchar(11)
        // when it was declared ssn.
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.UserTypes["ssn"] = new UserTypeSchema
        {
            Name = "ssn",
            Schema = "dbo",
            Kind = UserTypeKind.Alias,
            UnderlyingDbType = "varchar",
            MaxLength = 11,
            IsNullable = false
        };
        schema.Tables["people"] = new TableSchema
        {
            Name = "people",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["ssn"] = new ColumnSchema
                {
                    Name = "ssn",
                    DbType = "varchar",
                    MaxLength = 11,
                    ResolvedFromUserType = "ssn"
                }
            }
        };

        var reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema));

        Assert.Equal(UserTypeKind.Alias, reloaded.UserTypes["ssn"].Kind);
        Assert.Equal("varchar", reloaded.UserTypes["ssn"].UnderlyingDbType);
        Assert.Equal("varchar", reloaded.Tables["people"].Columns["ssn"].DbType);
        Assert.Equal("ssn", reloaded.Tables["people"].Columns["ssn"].ResolvedFromUserType);
    }

    [Fact]
    public void UserTypeKindSerializesAsAStringNotAnOrdinal()
    {
        // The generic JsonStringEnumConverter<T> is the AOT-safe one, and the
        // string form is also what keeps the snapshot readable and stable if a
        // member is ever inserted mid-enum. An ordinal would silently
        // reinterpret every existing snapshot on that day.
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.UserTypes["id_list"] = new UserTypeSchema { Name = "id_list", Kind = UserTypeKind.TableType };

        string json = SchemaLoader.Serialize(schema);

        Assert.Contains("\"TableType\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableTypeCarriesItsMembersSoTheRefusalCanNameThem()
    {
        // JNT2025 refuses to emit a method for a TVP-taking procedure and
        // prints the type's column list instead. It can only do that because
        // capture keeps the shape of a type the generator will never emit for.
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.UserTypes["id_list"] = new UserTypeSchema
        {
            Name = "id_list",
            Kind = UserTypeKind.TableType,
            Members =
            {
                new ColumnSchema { Name = "id", DbType = "int" },
                new ColumnSchema { Name = "label", DbType = "nvarchar", MaxLength = 50, IsNullable = true }
            }
        };

        var reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema));

        var members = reloaded.UserTypes["id_list"].Members;
        Assert.Equal(new[] { "id", "label" }, members.Select(m => m.Name));
        Assert.Equal(50, members[1].MaxLength);
    }

    [Fact]
    public void ASnapshotWrittenBeforeSpec014LoadsWithBothDictionariesEmpty()
    {
        // FR-008. The pre-014 shape, verbatim: no "functions" key, no
        // "userTypes" key, no "resolvedFromUserType" on the column. It must
        // load and generate exactly as it did, which is what makes the feature
        // additive rather than a snapshot migration.
        const string preSpec014 = """
        {
          "dialect": "postgres",
          "tables": {
            "orders": {
              "name": "orders",
              "columns": {
                "id": { "name": "id", "dbType": "int", "isPrimaryKey": true }
              }
            }
          }
        }
        """;

        var loaded = SchemaLoader.Load(preSpec014);

        Assert.Empty(loaded.Functions);
        Assert.Empty(loaded.UserTypes);
        Assert.Null(loaded.Tables["orders"].Columns["id"].ResolvedFromUserType);
    }

    [Fact]
    public void TheNewDictionariesAreWrittenExactlyAsTheirSiblingsAre()
    {
        // A re-pull on a dialect with no functions DOES add "functions": {} and
        // "userTypes": {} to an existing snapshot. That is a one-time diff, and
        // it is the same one "enums" caused when spec 013 landed — which is the
        // point of this test: the two new keys behave like every sibling
        // collection rather than being special-cased into invisibility.
        // Suppressing only the new pair would make the snapshot format
        // inconsistent to save a diff that lands once.
        string json = SchemaLoader.Serialize(new DatabaseSchema { Dialect = "sqlite" });

        foreach (string key in new[] { "enums", "sequences", "procedures", "functions", "userTypes" })
            Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
    }
}
