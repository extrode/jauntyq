extern alias contract;

using System.Collections.Immutable;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

public class ScopedCrudLiveTests
{
    private const string Ddl = @"
CREATE TABLE customers (id INT PRIMARY KEY);
CREATE TABLE orders (
    id INT PRIMARY KEY,
    tenant_id INT NOT NULL,
    customer_id INT NOT NULL,
    note VARCHAR(50) NULL,
    FOREIGN KEY (customer_id) REFERENCES customers(id)
);
INSERT INTO customers (id) VALUES (1);
INSERT INTO orders (id, tenant_id, customer_id, note) VALUES (1, 2, 1, 'b-one');";

    private const string ScopeJson = @"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" } ] }";

    private static string Scenario(bool hasUpsert) => @"
using System.Collections.Generic;
using System.Data.Common;
namespace Extrode.JauntyQ.Generated
{
    public static class Scenario
    {
        public static string Run(DbConnection conn)
        {
            var log = new List<string>();
            log.Add(""all:"" + Orders.GetAll(conn, 1).Count);
            log.Add(""byId:"" + (Orders.GetById(conn, 1, 1) == null));
            log.Add(""byCustomer:"" + Orders.GetByCustomerId(conn, 1, 1).Count);
            var theirs = new Order { Id = 1, TenantId = 2, CustomerId = 1, Note = ""a-hijack"" };
            log.Add(""update:"" + Orders.Update(conn, 1, theirs));
            log.Add(""delete:"" + Orders.Delete(conn, 1, theirs));
            log.Add(""deleteScalar:"" + Orders.Delete(conn, 1, 1));
            var mine = new Order { Id = 10, TenantId = 2, CustomerId = 1, Note = ""a-ten"" };
            log.Add(""insert:"" + Orders.Insert(conn, 1, mine));
            log.Add(""bulk:"" + Orders.BulkInsert(conn, 1, new[] { new Order { Id = 11, TenantId = 2, CustomerId = 1, Note = ""a-eleven"" } }));
            mine.Note = ""a-ten-updated"";
            log.Add(""updateOwn:"" + Orders.Update(conn, 1, mine));
" + (hasUpsert ? @"
            try { log.Add(""upsert:"" + Orders.Upsert(conn, 1, theirs)); }
            catch (DbException) { log.Add(""upsert:error""); }
            mine.Note = ""a-ten-upserted"";
            log.Add(""upsertOwn:"" + Orders.Upsert(conn, 1, mine));
" : "") + @"
            log.Add(""allAfter:"" + Orders.GetAll(conn, 1).Count);
            log.Add(""byCustomerAfter:"" + Orders.GetByCustomerId(conn, 1, 1).Count);
            return string.Join("" "", log);
        }
    }
}";

    private const string ReadsAndRefusals =
        "all:0 byId:True byCustomer:0 update:0 delete:0 deleteScalar:0 insert:1 bulk:1 updateOwn:1";

    private static async Task Verify(
        string dialect, Func<DbConnection> open, Func<Task<contract::Extrode.JauntyQ.Schema.DatabaseSchema>> extract,
        string? upsertLog, string ownNote)
    {
        await using var conn = open();
        await conn.OpenAsync();
        await Execute(conn, Ddl);

        var schema = await extract();
        schema.Dialect = dialect;
        var (asm, diagnostics) = GenerateAndCompile(contract::Extrode.JauntyQ.Schema.SchemaLoader.Serialize(schema), upsertLog != null);

        Assert.Equal(upsertLog == null, diagnostics.Any(d => d.Id == "JNT4007"));
        string log = (string)asm.GetType("Extrode.JauntyQ.Generated.Scenario")!.GetMethod("Run")!.Invoke(null, new object[] { conn })!;
        Assert.Equal(ReadsAndRefusals + (upsertLog == null ? "" : $" upsert:{upsertLog} upsertOwn:1") + " allAfter:2 byCustomerAfter:2", log);

        Assert.Equal(new[] { "1|2|b-one", "10|1|" + ownNote, "11|1|a-eleven" }, await Rows(conn));
    }

    private static async Task Execute(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> Rows(DbConnection conn)
    {
        var rows = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, tenant_id, note FROM orders ORDER BY id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add($"{reader.GetValue(0)}|{reader.GetValue(1)}|{reader.GetValue(2)}");
        return rows;
    }

    private static (Assembly, ImmutableArray<Diagnostic>) GenerateAndCompile(string snapshot, bool hasUpsert)
    {
        var texts = ImmutableArray.Create<AdditionalText>(
            new InMemoryAdditionalText("db/schema/jaunty.schema.json", snapshot),
            new InMemoryAdditionalText(ScopeHarness.ScopePath, ScopeJson));

        var compilation = CSharpCompilation.Create("ScopedCrudInput",
            new[] { CSharpSyntaxTree.ParseText("") }, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts)
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));
        var result = driver.RunGenerators(compilation).GetRunResult();

        var trees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .Append(CSharpSyntaxTree.ParseText(Scenario(hasUpsert)));
        var output = CSharpCompilation.Create("ScopedCrudGenerated", trees, References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var emit = output.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return (Assembly.Load(ms.ToArray()), result.Diagnostics);
    }

    private static MetadataReference[] References()
    {
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NpgsqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnection).Assembly.Location),
        };
        foreach (var name in new[] { "System.Runtime.dll", "System.Data.Common.dll", "System.Collections.dll", "System.Collections.NonGeneric.dll",
                     "System.Threading.Tasks.dll", "System.ComponentModel.Primitives.dll", "System.Linq.dll", "netstandard.dll" })
            refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, name)));
        return refs.ToArray();
    }

    [Fact]
    public async Task Sqlite()
    {
        string cs = "Data Source=scoped-crud-live;Mode=Memory;Cache=Shared";
        await Verify("sqlite", () => new SqliteConnection(cs),
            () => new contract::Extrode.JauntyQ.Schema.Extraction.SqliteExtractor().ExtractAsync(cs),
            "0", "a-ten-upserted");
    }

    [SkippableFact]
    public async Task Postgres()
    {
        var engine = await EngineContainers.Postgres;
        Skip.IfNot(engine.Available, engine.SkipReason);
        string cs = await EngineContainers.CreateDatabaseAsync(engine, "scoped_crud_live");
        await Verify("postgres", () => new NpgsqlConnection(cs),
            () => new contract::Extrode.JauntyQ.Schema.Extraction.PostgresExtractor().ExtractAsync(cs),
            "0", "a-ten-upserted");
    }

    [SkippableFact]
    public async Task SqlServer()
    {
        var engine = await EngineContainers.MsSql;
        Skip.IfNot(engine.Available, engine.SkipReason);
        string cs = await EngineContainers.CreateDatabaseAsync(engine, "scoped_crud_live");
        await Verify("sqlserver", () => new SqlConnection(cs),
            () => new contract::Extrode.JauntyQ.Schema.Extraction.SqlServerExtractor().ExtractAsync(cs),
            "error", "a-ten-upserted");
    }

    [SkippableFact]
    public async Task MySql()
    {
        var engine = await EngineContainers.MySql;
        Skip.IfNot(engine.Available, engine.SkipReason);
        string cs = new MySqlConnectionStringBuilder(await EngineContainers.CreateDatabaseAsync(engine, "scoped_crud_live"))
            { AllowLoadLocalInfile = true }.ConnectionString;
        await using (var admin = new MySqlConnection(engine.AdminConnectionString))
        {
            await admin.OpenAsync();
            await Execute(admin, "SET GLOBAL local_infile = 1");
        }
        await Verify("mysql", () => new MySqlConnection(cs),
            () => new contract::Extrode.JauntyQ.Schema.Extraction.MySqlExtractor().ExtractAsync(cs),
            null, "a-ten-updated");
    }
}
