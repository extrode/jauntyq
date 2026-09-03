using System.Threading.Tasks;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

// AUD-R10-01 (§2.7 extractor parity, procedureSchema × 4 extractors):
// PostgresExtractor and MySqlExtractor never populated DatabaseSchema
// .Procedures at all -- the whole extraction block was simply absent, not
// merely incomplete. -- @call is documented (docs/06-reference/directives.md)
// and implemented (JauntyQGenerator.Part2.cs) as dialect-agnostic: unlike
// -- @proc (JNT7002-gated to sqlserver only, since it emits T-SQL DDL), there
// is no dialect gate on -- @call at all, because it only *binds* to a
// procedure that already exists in the live database -- and both Postgres
// (native since PG11) and MySQL (a bog-standard feature, not an edge case)
// support real stored procedures. Before this fix, TryResolveProcedure always
// found schema.Procedures empty for a Postgres/MySQL snapshot, so -- @call
// against a genuinely-existing procedure always misfired JNT2005 "procedure
// not found" -- a false-positive Error on valid input.
//
// This file exercises all 4 extractors against the same matrix cell:
// SqlServerExtractor (already correct -- locks in the existing baseline),
// PostgresExtractor and MySqlExtractor (the fix), and SqliteExtractor
// (legitimate N/A -- SQLite has no stored-procedure concept at all, so
// Procedures staying empty is correct, not a gap).
public sealed class SqlServerProcedureFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_procedure");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE OR ALTER PROCEDURE test_proc
                @p_id int,
                @p_name nvarchar(40) OUTPUT
            AS
            BEGIN
                SET @p_name = 'hello';
            END";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class PostgresProcedureFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.Postgres;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_procedure");

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE PROCEDURE test_proc(IN p_id integer, INOUT p_name varchar(40))
            LANGUAGE plpgsql
            AS $$
            BEGIN
                p_name := 'hello';
            END;
            $$;";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class MySqlProcedureFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MySql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_procedure");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE PROCEDURE test_proc(IN p_id INT, OUT p_name VARCHAR(40))
            BEGIN
                SET p_name = 'hello';
            END";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerProcedureExtractorTests : IClassFixture<SqlServerProcedureFixture>
{
    private readonly SqlServerProcedureFixture _fx;
    public SqlServerProcedureExtractorTests(SqlServerProcedureFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsProcedure_WithInAndOutParams()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Procedures.ContainsKey("test_proc"));
        var proc = schema.Procedures["test_proc"];
        Assert.Equal(2, proc.Params.Count);
        Assert.Equal("p_id", proc.Params[0].Name);
        Assert.Equal(ProcedureParamDirection.In, proc.Params[0].Direction);
        Assert.Equal("p_name", proc.Params[1].Name);
        // Confirmed live: SQL Server's INFORMATION_SCHEMA.PARAMETERS reports
        // PARAMETER_MODE = "INOUT" (never "OUT") for a T-SQL "OUTPUT"
        // parameter -- T-SQL OUTPUT parameters are always readable as well as
        // writable at the metadata level, so there is no pure "OUT" case to
        // observe from a real SQL Server here. Pre-existing SqlServerExtractor
        // behavior (unchanged by AUD-R10-01), not a defect -- documenting the
        // actual baseline rather than the direction a T-SQL reader might
        // naively expect.
        Assert.Equal(ProcedureParamDirection.InOut, proc.Params[1].Direction);
        Assert.Equal(40, proc.Params[1].MaxLength);
    }
}

[Trait("Category", "AuditRegression")]
public class PostgresProcedureExtractorTests : IClassFixture<PostgresProcedureFixture>
{
    private readonly PostgresProcedureFixture _fx;
    public PostgresProcedureExtractorTests(PostgresProcedureFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsProcedure_WithInAndInOutParams()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        // Before AUD-R10-01's fix, schema.Procedures was always empty here --
        // this ContainsKey assertion is the one that would have failed.
        Assert.True(schema.Procedures.ContainsKey("test_proc"));
        var proc = schema.Procedures["test_proc"];
        Assert.Equal(2, proc.Params.Count);
        Assert.Equal("p_id", proc.Params[0].Name);
        Assert.Equal(ProcedureParamDirection.In, proc.Params[0].Direction);
        Assert.Equal("p_name", proc.Params[1].Name);
        Assert.Equal(ProcedureParamDirection.InOut, proc.Params[1].Direction);
        // Confirmed live: Postgres does not retain a length/precision typmod
        // for function/procedure parameter types at all (pg_proc stores
        // parameter types as a bare OID list, no typmod slot) -- a
        // "varchar(40)" IN/OUT/INOUT parameter reports
        // character_maximum_length = NULL, unlike a table column of the same
        // declared type. This is a genuine, documented Postgres server
        // limitation (Postgres silently does not enforce the length either),
        // not a PostgresExtractor gap -- the extractor faithfully reports
        // what information_schema.parameters actually contains.
        Assert.Null(proc.Params[1].MaxLength);
    }
}

public class MySqlProcedureExtractorTests : IClassFixture<MySqlProcedureFixture>
{
    private readonly MySqlProcedureFixture _fx;
    public MySqlProcedureExtractorTests(MySqlProcedureFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsProcedure_WithInAndOutParams()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        // Before AUD-R10-01's fix, schema.Procedures was always empty here --
        // this ContainsKey assertion is the one that would have failed.
        Assert.True(schema.Procedures.ContainsKey("test_proc"));
        var proc = schema.Procedures["test_proc"];
        Assert.Equal(2, proc.Params.Count);
        Assert.Equal("p_id", proc.Params[0].Name);
        Assert.Equal(ProcedureParamDirection.In, proc.Params[0].Direction);
        Assert.Equal("p_name", proc.Params[1].Name);
        Assert.Equal(ProcedureParamDirection.Out, proc.Params[1].Direction);
        Assert.Equal(40, proc.Params[1].MaxLength);
    }
}

// SQLite has no stored-procedure concept at all (no CREATE PROCEDURE, no
// PRAGMA surfacing anything procedure-shaped) -- SqliteExtractor legitimately
// never populates Procedures. This closes the 4th cell of the
// procedureSchema × 4-extractors row as a confirmed N/A, not an unexamined gap.
public class SqliteProcedureExtractorTests
{
    [Fact]
    public async Task Procedures_AlwaysEmpty_NoStoredProcedureConceptInSqlite()
    {
        string dbName = $"procedure_extractor_test_{System.Guid.NewGuid():N}";
        string connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY)";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connectionString);

        Assert.True(schema.Tables.ContainsKey("t"));
        Assert.Empty(schema.Procedures);
    }
}
