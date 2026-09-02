extern alias contract;

using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.TestInfra;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 013 T12a and T13, against a real server.
///
/// Everything else in this suite asserts on generated text. Text cannot tell
/// you that Postgres rejects a plain string for an enum column with 42804, or
/// that the exception a consumer sees names the column — both are claims about
/// a server's behavior, and the first of them was already wrong in the plan
/// before the T0 probe corrected it. So this file pulls a snapshot from a live
/// database, generates from it, compiles the result, and executes it against
/// that same database.
/// </summary>
public sealed class PostgresEnumRoundTripFixture : IAsyncLifetime
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
            CREATE TYPE order_status AS ENUM ('pending','shipped','canceled');

            CREATE TABLE orders (
                id serial PRIMARY KEY,
                status order_status NOT NULL,
                prior_status order_status NULL,
                note text
            );";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();

        // Npgsql caches the server's type catalog when the first connection
        // opens, which here is BEFORE the CREATE TYPE above. Without this the
        // enum column comes back as DataTypeName "-.-" and every read throws
        // InvalidCastException -- an artifact of seeding a fresh container, not
        // something a consumer connecting to an existing database would hit.
        await conn.ReloadTypesAsync();

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}

public class PostgresEnumRoundTripTests : IClassFixture<PostgresEnumRoundTripFixture>
{
    private readonly PostgresEnumRoundTripFixture _fx;
    public PostgresEnumRoundTripTests(PostgresEnumRoundTripFixture fx) => _fx = fx;

    private static readonly (string path, string sql)[] Queries =
    {
        ("db/Orders/ById.sql", "select id, status, prior_status from orders where id = @id"),
        ("db/Orders/ByStatus.sql", "select id, status from orders where status = @status"),
        ("db/Orders/ByStatuses.sql", "-- @each status\nselect id, status from orders where status in (@status)"),
    };

    /// <summary>
    /// Pulls the snapshot from the live database, runs the generator over it,
    /// and compiles the result against the real Npgsql the test host loaded.
    /// </summary>
    internal static Task<Assembly> GenerateAndCompileFor(string connectionString)
        => GenerateAndCompile(connectionString);

    private static async Task<Assembly> GenerateAndCompile(string connectionString)
    {
        var schema = await new contract::JauntyQ.Schema.Extraction.PostgresExtractor()
            .ExtractAsync(connectionString);
        // The extractors leave Dialect blank; `jaunty schema pull` stamps it
        // afterward (JauntyQ.Cli/Program.cs:246). Without this the generator
        // sees dialect "" and takes the portable branch, which binds an enum
        // parameter as text and gets 42804 from the server.
        schema.Dialect = "postgres";
        string snapshot = contract::JauntyQ.Schema.SchemaLoader.Serialize(schema);

        var texts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in Queries)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", snapshot));

        var compilation = CSharpCompilation.Create("EnumRoundTripInput",
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

        var output = CSharpCompilation.Create("EnumRoundTripGenerated", generated, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = output.Emit(ms);
        Assert.True(emit.Success, string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

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
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
        };
    }

    /// <summary>
    /// The static overload taking an explicit DbConnection, picked by argument
    /// count -- the instance overloads need a JauntyDb and add nothing here.
    /// </summary>
    private static MethodInfo StaticOp(Assembly asm, string name, int argCount)
        => asm.GetType("JauntyQ.Generated.Orders")!
              .GetMethods(BindingFlags.Public | BindingFlags.Static)
              .Single(m => m.Name == name
                        && m.GetParameters().Length == argCount
                        && m.GetParameters()[0].ParameterType == typeof(DbConnection));

    private static object Status(Assembly asm, string member)
        => Enum.Parse(asm.GetType("JauntyQ.Generated.OrderStatus")!, member);

    private static object Prop(object row, string name)
        => row.GetType().GetProperty(name)!.GetValue(row)!;

    /// <summary>
    /// T12a's core claim, and the one no string assertion can make: a generated
    /// INSERT of an enum column is accepted by a real PostgreSQL. Binding the
    /// enum, or binding a string with DbType.String, both fail here with 42804.
    /// </summary>
    [SkippableFact]
    public async Task ScalarInsert_IsAcceptedByPostgres_AndReadsBackAsTheEnum()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await GenerateAndCompile(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await Truncate(conn);

        StaticOp(asm, "Insert", 5).Invoke(null,
            new object?[] { conn, Status(asm, "Shipped"), Status(asm, "Pending"), "first", null });

        var rows = (System.Collections.IEnumerable)StaticOp(asm, "GetAll", 2)
            .Invoke(null, new object?[] { conn, null })!;
        object row = rows.Cast<object>().Single();

        Assert.Equal(Status(asm, "Shipped"), Prop(row, "Status"));
        Assert.Equal(Status(asm, "Pending"), Prop(row, "PriorStatus"));
    }

    /// <summary>
    /// A nullable enum column bound as NULL. The unwrap has to stay inside the
    /// null branch or this is a NullReferenceException before the server is
    /// ever reached.
    /// </summary>
    [SkippableFact]
    public async Task NullableEnumColumn_BindsAndReadsBackAsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await GenerateAndCompile(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await Truncate(conn);

        StaticOp(asm, "Insert", 5).Invoke(null,
            new object?[] { conn, Status(asm, "Pending"), null, "no prior", null });

        var rows = (System.Collections.IEnumerable)StaticOp(asm, "GetAll", 2)
            .Invoke(null, new object?[] { conn, null })!;
        object row = rows.Cast<object>().Single();

        Assert.Null(row.GetType().GetProperty("PriorStatus")!.GetValue(row));
    }

    /// <summary>
    /// The POCO insert path, which builds its own parameters rather than going
    /// through the scalar binding.
    /// </summary>
    [SkippableFact]
    public async Task PocoInsert_IsAcceptedByPostgres()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await GenerateAndCompile(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await Truncate(conn);

        object order = Activator.CreateInstance(asm.GetType("JauntyQ.Generated.Order")!)!;
        order.GetType().GetProperty("Status")!.SetValue(order, Status(asm, "Canceled"));
        order.GetType().GetProperty("Note")!.SetValue(order, "poco");

        StaticOp(asm, "Insert", 3).Invoke(null, new object?[] { conn, order, null });

        var rows = (System.Collections.IEnumerable)StaticOp(asm, "GetAll", 2)
            .Invoke(null, new object?[] { conn, null })!;
        Assert.Equal(Status(asm, "Canceled"), Prop(rows.Cast<object>().Single(), "Status"));
    }

    /// <summary>
    /// An enum in a WHERE comparison. This is the shape the T0 probe found
    /// rejected outright as an ordinary string parameter.
    /// </summary>
    [SkippableFact]
    public async Task ScalarComparison_MatchesOnTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await GenerateAndCompile(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await Truncate(conn);

        var insert = StaticOp(asm, "Insert", 5);
        insert.Invoke(null, new object?[] { conn, Status(asm, "Shipped"), null, "a", null });
        insert.Invoke(null, new object?[] { conn, Status(asm, "Pending"), null, "b", null });

        var matched = (System.Collections.IEnumerable)StaticOp(asm, "ByStatus", 3)
            .Invoke(null, new object?[] { conn, Status(asm, "Shipped"), null })!;

        Assert.Single(matched.Cast<object>());
    }

    /// <summary>
    /// The -- @each IN-list, live. The generated SQL expands to @status0,
    /// @status1..., each bound separately, so a wrong parameter type here fails
    /// on the server rather than in the compiler.
    /// </summary>
    [SkippableFact]
    public async Task EachInList_MatchesOnTheServer()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var asm = await GenerateAndCompile(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await Truncate(conn);

        var insert = StaticOp(asm, "Insert", 5);
        insert.Invoke(null, new object?[] { conn, Status(asm, "Shipped"), null, "a", null });
        insert.Invoke(null, new object?[] { conn, Status(asm, "Pending"), null, "b", null });
        insert.Invoke(null, new object?[] { conn, Status(asm, "Canceled"), null, "c", null });

        Type enumType = asm.GetType("JauntyQ.Generated.OrderStatus")!;
        var list = (System.Collections.IList)Activator.CreateInstance(
            typeof(System.Collections.Generic.List<>).MakeGenericType(enumType))!;
        list.Add(Status(asm, "Shipped"));
        list.Add(Status(asm, "Canceled"));

        var matched = (System.Collections.IEnumerable)StaticOp(asm, "ByStatuses", 3)
            .Invoke(null, new object?[] { conn, list, null })!;

        Assert.Equal(2, matched.Cast<object>().Count());
    }

    private static async Task Truncate(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE orders RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// A separate container, because the test below mutates the enum type itself
/// with ALTER TYPE. Sharing the round-trip fixture would leak that mutation
/// into whichever tests xUnit happened to schedule afterward.
/// </summary>
public sealed class PostgresEnumDriftFixture : IAsyncLifetime
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
            CREATE TYPE order_status AS ENUM ('pending','shipped','canceled');

            CREATE TABLE orders (
                id serial PRIMARY KEY,
                status order_status NOT NULL,
                prior_status order_status NULL,
                note text
            );";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();

        // Npgsql caches the server's type catalog when the first connection
        // opens, which here is BEFORE the CREATE TYPE above. Without this the
        // enum column comes back as DataTypeName "-.-" and every read throws
        // InvalidCastException -- an artifact of seeding a fresh container, not
        // something a consumer connecting to an existing database would hit.
        await conn.ReloadTypesAsync();

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}

public class PostgresEnumUnknownValueTests : IClassFixture<PostgresEnumDriftFixture>
{
    private readonly PostgresEnumDriftFixture _fx;
    public PostgresEnumUnknownValueTests(PostgresEnumDriftFixture fx) => _fx = fx;

    /// <summary>
    /// T13 / SC-004. The snapshot is pulled and the code generated FIRST; only
    /// then does the database gain a member. That ordering is the whole point:
    /// it reproduces a deployed binary meeting a database somebody has since
    /// migrated, which is the situation the exception exists for. A hand-built
    /// snapshot could not tell you that PostgreSQL accepts the new value, that
    /// the reader hands it back as text, or that the throw survives the trip.
    /// </summary>
    [SkippableFact]
    public async Task ValueAddedAfterThePull_ThrowsNamingColumnAndValue()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Generated against the three-member type.
        var asm = await PostgresEnumRoundTripTests.GenerateAndCompileFor(_fx.ConnectionString);

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // The migration the deployed binary knows nothing about. ADD VALUE
        // cannot run inside the implicit transaction of a multi-statement
        // batch on PG < 12 semantics, so it goes on its own.
        await using (var alter = conn.CreateCommand())
        {
            alter.CommandText = "ALTER TYPE order_status ADD VALUE 'refunded';";
            await alter.ExecuteNonQueryAsync();
        }
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO orders (status, note) VALUES ('refunded', 'post-migration');";
            await insert.ExecuteNonQueryAsync();
        }

        var getAll = asm.GetType("JauntyQ.Generated.Orders")!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "GetAll" && m.GetParameters().Length == 2);

        var outer = Assert.Throws<TargetInvocationException>(
            () => getAll.Invoke(null, new object?[] { conn, null }));

        Exception thrown = outer.InnerException!;
        Assert.Equal("JauntyQ.Generated.JauntyQEnumValueException", thrown.GetType().FullName);
        Assert.IsAssignableFrom<InvalidOperationException>(thrown);

        // Naming the column and the value is the requirement, not just failing.
        Assert.Equal("refunded", thrown.GetType().GetProperty("Value")!.GetValue(thrown));
        Assert.Equal("orders.status", thrown.GetType().GetProperty("Column")!.GetValue(thrown));
        Assert.Equal("order_status", thrown.GetType().GetProperty("EnumType")!.GetValue(thrown));
        Assert.Contains("refunded", thrown.Message);
        Assert.Contains("orders.status", thrown.Message);
        Assert.Contains("jaunty schema pull", thrown.Message);
    }
}
