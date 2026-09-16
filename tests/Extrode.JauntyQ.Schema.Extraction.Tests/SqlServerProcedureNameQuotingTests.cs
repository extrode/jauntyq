using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

// AUD-R1-01. SqlServerExtractor's first-result-set-shape probe built
// "EXEC " + proc.Name unquoted for sys.dm_exec_describe_first_result_set.
// A procedure whose name needs bracket-quoting (a space, in this test)
// produced invalid T-SQL that the surrounding catch swallowed silently,
// indistinguishable from the genuinely-undeterminable case -- so Results
// came back empty for a proc whose shape was, in fact, determinable.
public sealed class QuotedProcNameFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MsSql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_proc_quoting");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE OR ALTER PROCEDURE [get order info]
                @order_id int
            AS
            BEGIN
                SELECT @order_id AS order_id, CAST('open' AS nvarchar(20)) AS status;
            END";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerProcedureNameQuotingTests : IClassFixture<QuotedProcNameFixture>
{
    private readonly QuotedProcNameFixture _fx;
    public SqlServerProcedureNameQuotingTests(QuotedProcNameFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ProcedureNameWithASpace_StillGetsItsResultSetShapeCaptured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Procedures.ContainsKey("get order info"));
        var proc = schema.Procedures["get order info"];

        Assert.Equal(2, proc.Results.Count);
        Assert.Equal("order_id", proc.Results[0].Name);
        Assert.Equal("status", proc.Results[1].Name);
    }
}
