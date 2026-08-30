extern alias contract;

using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 014 T6, against real servers on all three dialects that have scalar
/// functions.
///
/// FunctionGenerationTests asserts on generated TEXT, which can say the emitted
/// SQL is <c>SELECT dbo.calc_tax(@amount, @rate)</c> but cannot say the server
/// accepts it. Three claims in the design are about engine behaviour and
/// nothing else can check them: that a schema-qualified call resolves
/// independently of search_path/default schema on Postgres and SQL Server; that
/// an UNqualified one is what MySQL needs; and that binding every parameter
/// through the abstract <see cref="DbParameter"/> — no provider type anywhere —
/// is accepted by all three. The last is the property the whole P1 design rests
/// on (014-plan.md §1.1), so it is the one worst served by a string assertion.
/// </summary>
internal static class FunctionRoundTrip
{
    /// <summary>
    /// Pulls the snapshot from the live database, runs the generator over it,
    /// compiles the result, and returns the loaded assembly.
    /// </summary>
    internal static async Task<Assembly> GenerateAndCompile(
        contract::JauntyQ.Schema.DatabaseSchema schema, string dialect)
    {
        // The extractors leave Dialect blank; `jaunty schema pull` stamps it
        // afterwards. Without this the generator sees dialect "" and
        // PlanFunctionEmission's gate refuses everything, so the round-trip
        // would compile an assembly with no FunctionAccessor at all and the
        // reflection lookup below would fail with a confusing message rather
        // than this comment.
        schema.Dialect = dialect;
        string snapshot = contract::JauntyQ.Schema.SchemaLoader.Serialize(schema);

        var texts = new System.Collections.Generic.List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", snapshot),
        };

        var compilation = CSharpCompilation.Create("FunctionRoundTripInput",
            new[] { CSharpSyntaxTree.ParseText("") },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var generated = driver.GetRunResult().Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToArray();

        var output = CSharpCompilation.Create("FunctionRoundTripGenerated", generated, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = output.Emit(ms);
        Assert.True(emit.Success, string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        await Task.CompletedTask;
        return Assembly.Load(ms.ToArray());
    }

    private static MetadataReference[] References()
    {
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NpgsqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            // Microsoft.Data.SqlClient's SqlParameterCollection inherits
            // CollectionBase, so referencing SqlClient at all drags this in.
            // Nothing generated names it; without it every SQL Server
            // round-trip fails to compile with four CS0012s.
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
        };
    }

    /// <summary>
    /// The static overload on the emitted FunctionAccessor, picked by argument
    /// count. The instance overloads need a JauntyDb, which needs the
    /// generated connection plumbing; the static ones take a bare
    /// <see cref="DbConnection"/> and are the point of this test — a consumer
    /// holding nothing but a DbConnection must be able to call the function.
    /// </summary>
    internal static MethodInfo StaticFn(Assembly asm, string name, int argCount)
        => asm.GetType("JauntyQ.Generated.JauntyDb+FunctionAccessor")!
              .GetMethods(BindingFlags.Public | BindingFlags.Static)
              .Single(m => m.Name == name
                        && m.GetParameters().Length == argCount
                        && m.GetParameters()[0].ParameterType == typeof(DbConnection));
}

public sealed class PostgresFunctionRoundTripFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE DOMAIN national_id AS varchar(11);

            CREATE TABLE people (
                id serial PRIMARY KEY,
                code national_id NOT NULL
            );

            CREATE FUNCTION calc_tax(amount numeric, rate numeric)
                RETURNS numeric LANGUAGE sql IMMUTABLE AS $$ SELECT amount * rate $$;

            CREATE FUNCTION answer() RETURNS int LANGUAGE sql IMMUTABLE AS $$ SELECT 42 $$;

            CREATE FUNCTION greet(who text) RETURNS text LANGUAGE sql IMMUTABLE
                AS $$ SELECT 'hello ' || who $$;";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();
        await conn.ReloadTypesAsync();

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}

public class PostgresFunctionRoundTripTests : IClassFixture<PostgresFunctionRoundTripFixture>
{
    private readonly PostgresFunctionRoundTripFixture _fx;
    public PostgresFunctionRoundTripTests(PostgresFunctionRoundTripFixture fx) => _fx = fx;

    private async Task<Assembly> Build()
    {
        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.PostgresExtractor()
            .ExtractAsync(_fx.ConnectionString);
        return await FunctionRoundTrip.GenerateAndCompile(schema, "postgres");
    }

    [SkippableFact]
    public async Task AScalarFunctionReturnsItsRealValueFromTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        object? result = FunctionRoundTrip.StaticFn(asm, "CalcTax", 4)
            .Invoke(null, new object?[] { conn, 100m, 0.25m, null });

        Assert.Equal(25m, result);
    }

    [SkippableFact]
    public async Task TheAsyncOverloadReturnsTheSameValue()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        var task = (Task)FunctionRoundTrip.StaticFn(asm, "CalcTaxAsync", 5)
            .Invoke(null, new object?[] { conn, 100m, 0.25m, null, default(CancellationToken) })!;
        await task;

        Assert.Equal(25m, task.GetType().GetProperty("Result")!.GetValue(task));
    }

    [SkippableFact]
    public async Task AZeroArgumentFunctionRunsAgainstTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(42, FunctionRoundTrip.StaticFn(asm, "Answer", 2)
            .Invoke(null, new object?[] { conn, null }));
    }

    [SkippableFact]
    public async Task AStringReturningFunctionRoundTripsItsText()
    {
        // A reference-type return, which goes down a different arm of
        // GetReaderCall than every numeric case above.
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal("hello world", FunctionRoundTrip.StaticFn(asm, "Greet", 3)
            .Invoke(null, new object?[] { conn, "world", null }));
    }

    [SkippableFact]
    public async Task ADomainTypedColumnRecordsItsProvenance_AndStillMapsToTheBaseType()
    {
        // FR-004, and the part of it spec 014 changed is the PROVENANCE, not
        // the mapping. information_schema.columns already reports a DOMAIN
        // column's data_type as the base type, so `Code` was a string before
        // this spec too — what was missing was any record that the column was
        // declared through national_id at all, which is what
        // SchemaContractComparer's UserTypeChanged needs to mean anything.
        //
        // So this asserts both halves: the snapshot names the domain, and the
        // generated property is still the primitive.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.PostgresExtractor()
            .ExtractAsync(_fx.ConnectionString);
        var column = schema.Tables["people"].Columns["code"];

        Assert.Equal("national_id", column.ResolvedFromUserType);
        Assert.Equal("character varying", column.DbType);

        var asm = await FunctionRoundTrip.GenerateAndCompile(schema, "postgres");
        var row = asm.GetType("JauntyQ.Generated.PeopleRow")!;

        Assert.Equal(typeof(string), row.GetProperty("Code")!.PropertyType);
    }

    [SkippableFact]
    public async Task DroppingTheFunctionBreaksTheCallSiteAtRuntime()
    {
        // The claim that makes the whole feature a CONTRACT rather than a
        // convenience: the emitted method really binds to a database object,
        // so when that object goes away the call fails. This is what
        // SchemaContractComparer's FunctionMissing exists to catch at build
        // time instead, and it is only a real defence if the runtime failure
        // it prevents is real.
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        await using (var drop = conn.CreateCommand())
        {
            drop.CommandText = "DROP FUNCTION greet(text)";
            await drop.ExecuteNonQueryAsync();
        }

        try
        {
            var ex = Assert.Throws<TargetInvocationException>(() =>
                FunctionRoundTrip.StaticFn(asm, "Greet", 3)
                    .Invoke(null, new object?[] { conn, "world", null }));

            Assert.IsType<PostgresException>(ex.InnerException);
        }
        finally
        {
            await using var restore = conn.CreateCommand();
            restore.CommandText =
                "CREATE FUNCTION greet(who text) RETURNS text LANGUAGE sql IMMUTABLE " +
                "AS $$ SELECT 'hello ' || who $$";
            await restore.ExecuteNonQueryAsync();
        }
    }
}

public sealed class MySqlFunctionRoundTripFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MySqlBuilder("mysql:8.0").WithUsername("root").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (string ddl in new[]
        {
            "CREATE TABLE people (id int AUTO_INCREMENT PRIMARY KEY, code varchar(11) NOT NULL)",
            "CREATE FUNCTION calc_tax(amount decimal(12,2), rate decimal(5,4)) " +
            "RETURNS decimal(12,2) DETERMINISTIC RETURN amount * rate",
            "CREATE FUNCTION answer() RETURNS int DETERMINISTIC RETURN 42",
        })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}

public class MySqlFunctionRoundTripTests : IClassFixture<MySqlFunctionRoundTripFixture>
{
    private readonly MySqlFunctionRoundTripFixture _fx;
    public MySqlFunctionRoundTripTests(MySqlFunctionRoundTripFixture fx) => _fx = fx;

    private async Task<Assembly> Build()
    {
        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.MySqlExtractor()
            .ExtractAsync(_fx.ConnectionString);
        return await FunctionRoundTrip.GenerateAndCompile(schema, "mysql");
    }

    [SkippableFact]
    public async Task AnUnqualifiedCallIsWhatMySqlAccepts()
    {
        // The plain case: snapshot and connection name the same database.
        // ASnapshotTakenFromOneDatabaseStillCallsAgainstAnother is the one that
        // separates unqualified from qualified; this one only shows the emitted
        // call runs.
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new MySqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(25m, FunctionRoundTrip.StaticFn(asm, "CalcTax", 4)
            .Invoke(null, new object?[] { conn, 100m, 0.25m, null }));
    }

    [SkippableFact]
    public async Task AZeroArgumentFunctionRunsAgainstTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new MySqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(42, FunctionRoundTrip.StaticFn(asm, "Answer", 2)
            .Invoke(null, new object?[] { conn, null }));
    }

    [SkippableFact]
    public async Task ASnapshotTakenFromOneDatabaseStillCallsAgainstAnother()
    {
        // The test that makes the MySQL decision falsifiable. Every other
        // assertion here would pass just as well if the call WERE qualified,
        // because the snapshot and the connection name the same database — so
        // on its own "MySQL calls are unqualified" is only ever checked as
        // emitted text.
        //
        // MySQL's schema IS the database, so qualifying bakes the CAPTURE-time
        // database name into every call site. Here the capture database is
        // gone by the time the call runs, which is the ordinary dev-snapshot-
        // against-prod case with the timing made explicit. Unqualified
        // resolves against the connected database and works; qualified looks
        // for a database that no longer exists.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var admin = new MySqlConnectionStringBuilder(_fx.ConnectionString);
        string original = admin.Database;

        const string fnDdl =
            "CREATE FUNCTION calc_tax(amount decimal(12,2), rate decimal(5,4)) " +
            "RETURNS decimal(12,2) DETERMINISTIC RETURN amount * rate";

        await Exec(_fx.ConnectionString, "CREATE DATABASE fn_capture");
        // A table as well as the function: JauntyDb, which carries the
        // FunctionAccessor, is only emitted for a snapshot that has tables.
        await Exec(Db(admin, "fn_capture"),
            "CREATE TABLE people (id int AUTO_INCREMENT PRIMARY KEY, code varchar(11) NOT NULL)");
        await Exec(Db(admin, "fn_capture"), fnDdl);
        await Exec(_fx.ConnectionString, "CREATE DATABASE fn_runtime");
        await Exec(Db(admin, "fn_runtime"), fnDdl);

        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.MySqlExtractor()
            .ExtractAsync(Db(admin, "fn_capture"));
        var asm = await FunctionRoundTrip.GenerateAndCompile(schema, "mysql");

        await Exec(_fx.ConnectionString, "DROP DATABASE fn_capture");

        await using var conn = new MySqlConnection(Db(admin, "fn_runtime"));
        await conn.OpenAsync();

        Assert.Equal(25m, FunctionRoundTrip.StaticFn(asm, "CalcTax", 4)
            .Invoke(null, new object?[] { conn, 100m, 0.25m, null }));

        await Exec(Db(admin, original), "DROP DATABASE fn_runtime");
    }

    private static string Db(MySqlConnectionStringBuilder template, string database)
        => new MySqlConnectionStringBuilder(template.ConnectionString) { Database = database }
            .ConnectionString;

    private static async Task Exec(string connectionString, string sql)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class SqlServerFunctionRoundTripFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (string ddl in new[]
        {
            "CREATE TYPE national_id FROM varchar(11) NOT NULL",
            "CREATE TABLE people (id int IDENTITY PRIMARY KEY, code national_id NOT NULL)",
            "CREATE FUNCTION calc_tax(@amount decimal(12,2), @rate decimal(5,4)) " +
            "RETURNS decimal(12,2) AS BEGIN RETURN @amount * @rate END",
            "CREATE FUNCTION answer() RETURNS int AS BEGIN RETURN 42 END",
        })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}

public class SqlServerFunctionRoundTripTests : IClassFixture<SqlServerFunctionRoundTripFixture>
{
    private readonly SqlServerFunctionRoundTripFixture _fx;
    public SqlServerFunctionRoundTripTests(SqlServerFunctionRoundTripFixture fx) => _fx = fx;

    private async Task<Assembly> Build()
    {
        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.SqlServerExtractor()
            .ExtractAsync(_fx.ConnectionString);
        return await FunctionRoundTrip.GenerateAndCompile(schema, "sqlserver");
    }

    [SkippableFact]
    public async Task TheSchemaQualifiedCallIsAcceptedAndReturnsTheServersValue()
    {
        // Acceptance, not necessity. Capture is single-schema (the extractor
        // filters on SPECIFIC_SCHEMA = @schema), so every captured function is
        // in the caller's default schema and an unqualified call would resolve
        // here too. What this does check is that the qualified form the emitter
        // produces is valid T-SQL against a real server and that the value
        // comes back through DbParameter binding with no provider type
        // anywhere. Proving qualification is REQUIRED needs a function outside
        // the default schema, which nothing captures yet.
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(25m, FunctionRoundTrip.StaticFn(asm, "CalcTax", 4)
            .Invoke(null, new object?[] { conn, 100m, 0.25m, null }));
    }

    [SkippableFact]
    public async Task AZeroArgumentFunctionRunsAgainstTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await Build();

        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        Assert.Equal(42, FunctionRoundTrip.StaticFn(asm, "Answer", 2)
            .Invoke(null, new object?[] { conn, null }));
    }

    [SkippableFact]
    public async Task AnAliasTypedColumnRecordsItsProvenance_AndStillMapsToTheBaseType()
    {
        // FR-004 on SQL Server, where alias types are the common case. Same
        // shape as the Postgres one: DATA_TYPE was already the base type, and
        // what spec 014 added is DOMAIN_NAME reaching the snapshot.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new contract::JauntyQ.Schema.Contract.Extractors.SqlServerExtractor()
            .ExtractAsync(_fx.ConnectionString);
        var column = schema.Tables["people"].Columns["code"];

        Assert.Equal("national_id", column.ResolvedFromUserType);
        Assert.Equal("varchar", column.DbType);

        var asm = await FunctionRoundTrip.GenerateAndCompile(schema, "sqlserver");
        var row = asm.GetType("JauntyQ.Generated.PeopleRow")!;

        Assert.Equal(typeof(string), row.GetProperty("Code")!.PropertyType);
    }
}
