using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using MySqlConnector;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public sealed class MySqlMutationCoverageFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";
    public string Database => new MySqlConnectionStringBuilder(ConnectionString).Database;

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MySql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_mysql_mutation");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        string[] statements =
        {
            @"CREATE TABLE facets (
                id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
                nn VARCHAR(40) CHARACTER SET utf8mb4 NOT NULL,
                latin VARCHAR(10) CHARACTER SET latin1 NULL,
                body LONGTEXT,
                amount DECIMAL(10,2) NULL,
                n INT NULL,
                KEY ix_facets_nn (nn))",
            "CREATE PROCEDURE p_none() SELECT 1",
            "CREATE PROCEDURE p_facets(INOUT io INT, IN body LONGTEXT, IN amt DECIMAL(10,2)) SET io = io + 1",
            "CREATE FUNCTION fn_mix(a VARCHAR(20), b DECIMAL(8,3)) RETURNS DECIMAL(12,4) DETERMINISTIC RETURN b",
            "CREATE FUNCTION fn_str() RETURNS VARCHAR(30) DETERMINISTIC RETURN 'x'",
            "CREATE FUNCTION fn_long(t LONGTEXT) RETURNS LONGTEXT DETERMINISTIC RETURN t",
        };
        foreach (string sql in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class MariaDbBacktickSequenceFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MariaDb;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        string plain = await EngineContainers.CreateDatabaseAsync(engine, "fx_backtick_host");
        string database = "fx`q_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        await using (var conn = new MySqlConnection(plain))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE DATABASE `" + database.Replace("`", "``") + "`";
            await cmd.ExecuteNonQueryAsync();
        }

        ConnectionString = new MySqlConnectionStringBuilder(plain) { Database = database }.ConnectionString;
        await using (var conn = new MySqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE SEQUENCE `s``q` START WITH 100 INCREMENT BY 5";
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlExtractorMutationCoverageTests : IClassFixture<MySqlMutationCoverageFixture>
{
    private readonly MySqlMutationCoverageFixture _fx;
    public MySqlExtractorMutationCoverageTests(MySqlMutationCoverageFixture fx) => _fx = fx;

    private async Task<DatabaseSchema> Extract()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        return await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
    }

    [SkippableFact]
    public async Task Table_IsNamedAndNotAView()
    {
        var table = (await Extract()).Tables["facets"];

        Assert.Equal("facets", table.Name);
        Assert.False(table.IsView);
    }

    [SkippableFact]
    public async Task UnsignedAutoIncrementKey_CarriesItsFlags()
    {
        var id = (await Extract()).Tables["facets"].Columns["id"];

        Assert.Equal("int unsigned", id.DbType);
        Assert.True(id.IsIdentity);
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);
        Assert.Null(id.MaxLength);
        Assert.Null(id.IsUnicode);
    }

    [SkippableFact]
    public async Task CharacterColumns_CarryLengthNullabilityAndUnicode()
    {
        var columns = (await Extract()).Tables["facets"].Columns;

        var nn = columns["nn"];
        Assert.Equal(("varchar", 40, true, false, false, false), (nn.DbType, nn.MaxLength, nn.IsUnicode, nn.IsNullable, nn.IsIdentity, nn.IsPrimaryKey));
        var latin = columns["latin"];
        Assert.Equal((10, false, true), (latin.MaxLength, latin.IsUnicode, latin.IsNullable));
        Assert.Equal(-1, columns["body"].MaxLength);
    }

    [SkippableFact]
    public async Task NumericColumns_CarryPrecisionAndScaleOnlyForDecimal()
    {
        var columns = (await Extract()).Tables["facets"].Columns;

        var amount = columns["amount"];
        Assert.Equal((null, 10, 2), (amount.MaxLength, amount.Precision, amount.Scale));
        var n = columns["n"];
        Assert.Equal(("int", null, null), (n.DbType, n.Precision, n.Scale));
    }

    [SkippableFact]
    public async Task PlainIndex_HasNeitherPrefixNorExpressionKeyPart()
    {
        var index = Assert.Single((await Extract()).Tables["facets"].Indexes, i => i.Name == "ix_facets_nn");

        Assert.Equal(new[] { "nn" }, index.Columns);
        Assert.False(index.HasPrefixKeyPart);
        Assert.False(index.HasExpressionKeyPart);
    }

    [SkippableFact]
    public async Task ProcedureWithoutParameters_IsCapturedByName()
    {
        var proc = (await Extract()).Procedures["p_none"];

        Assert.Equal("p_none", proc.Name);
        Assert.Empty(proc.Params);
    }

    [SkippableFact]
    public async Task ProcedureParameters_CarryDirectionAndFacets()
    {
        var proc = (await Extract()).Procedures["p_facets"];

        Assert.Equal("p_facets", proc.Name);
        Assert.Collection(proc.Params,
            p => Assert.Equal(("io", "int", ProcedureParamDirection.InOut), (p.Name, p.DbType, p.Direction)),
            p => Assert.Equal(("body", -1, ProcedureParamDirection.In), (p.Name, p.MaxLength, p.Direction)),
            p => Assert.Equal(("amt", null, 10, 2), (p.Name, p.MaxLength, p.Precision, p.Scale)));
    }

    [SkippableFact]
    public async Task FunctionWithArguments_IsKeyedByArgumentTypesAndCarriesFacets()
    {
        var functions = (await Extract()).Functions;

        Assert.True(functions.TryGetValue("fn_mix(varchar,decimal)", out var fn), string.Join(", ", functions.Keys));
        Assert.Equal(("fn_mix", _fx.Database), (fn!.Name, fn.Schema));
        Assert.Equal(("decimal", null, 12, 4), (fn.Return.DbType, fn.Return.MaxLength, fn.Return.Precision, fn.Return.Scale));
        Assert.Collection(fn.Params,
            p => Assert.Equal(("a", "varchar", 20, null, null), (p.Name, p.DbType, p.MaxLength, p.Precision, p.Scale)),
            p => Assert.Equal(("b", "decimal", null, 8, 3), (p.Name, p.DbType, p.MaxLength, p.Precision, p.Scale)));
    }

    [SkippableFact]
    public async Task FunctionWithoutArguments_IsKeyedByBareNameAndCarriesReturnLength()
    {
        var functions = (await Extract()).Functions;

        Assert.True(functions.TryGetValue("fn_str", out var fn), string.Join(", ", functions.Keys));
        Assert.Equal("fn_str", fn!.Name);
        Assert.Equal(("varchar", 30, null, null), (fn.Return.DbType, fn.Return.MaxLength, fn.Return.Precision, fn.Return.Scale));
        Assert.Empty(fn.Params);
    }

    [SkippableFact]
    public async Task LongTextFunction_ReportsUnboundedLengthForParamAndReturn()
    {
        var functions = (await Extract()).Functions;

        Assert.True(functions.TryGetValue("fn_long(longtext)", out var fn), string.Join(", ", functions.Keys));
        Assert.Equal(-1, fn!.Return.MaxLength);
        Assert.Equal(-1, Assert.Single(fn.Params).MaxLength);
    }
}

public class MariaDbBacktickSequenceExtractorTests : IClassFixture<MariaDbBacktickSequenceFixture>
{
    private readonly MariaDbBacktickSequenceFixture _fx;
    public MariaDbBacktickSequenceExtractorTests(MariaDbBacktickSequenceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task SequenceInBacktickNamedDatabase_IsReadWithAllBounds()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        var seq = schema.Sequences["s`q"];
        Assert.Equal((100L, 5L), (seq.StartValue, seq.Increment));
        Assert.Equal(1L, seq.MinValue);
        Assert.Equal(long.MaxValue - 1, seq.MaxValue);
        Assert.Equal(100L, seq.CurrentValue);
    }
}

public class MySqlEnumParsingMutationCoverageTests
{
    [Fact]
    public void CaptureInlineEnum_NamesTheCapturedEnum()
    {
        var schema = new DatabaseSchema { Dialect = "mysql" };

        string? name = MySqlExtractor.CaptureInlineEnum(schema, "orders", "status", "enum", "enum('a','b')");

        Assert.NotNull(name);
        Assert.Equal(name, schema.Enums[name!].Name);
    }

    [Theory]
    [InlineData("('a')", new[] { "a" })]
    [InlineData("enum('a',,'b')", new[] { "a", "b" })]
    [InlineData("enum('a',)", new[] { "a" })]
    public void ParseEnumMembers_ReadsOnlyQuotedMembers(string columnType, string[] expected)
    {
        Assert.Equal(expected, MySqlExtractor.ParseEnumMembers(columnType));
    }
}
