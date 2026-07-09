using Microsoft.Data.Sqlite;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.DdlSchema.Sqlite.Tests;

// End-to-end guard for DDL-as-schema-source through real MSBuild.
//
// This project commits NO db/schema/*.schema.json snapshot. The generator
// therefore builds the schema from db/ddl/*.sql, which requires knowing the
// dialect. The only source of that dialect is <JauntyQDialect>sqlite</> in the
// .csproj, surfaced to the generator via the CompilerVisibleProperty
// registration (Directory.Build.props in-repo; the packaged
// build/JauntyQ.Generator.props for external consumers). If that wiring
// regresses, the generator cannot resolve the dialect and this project fails
// to compile (JNT9003 / JNT6001) — so a green build already proves the fix.
// The runtime assertions below confirm the generated auto-CRUD surface works.
public sealed class DdlSchemaSourceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteConnection _conn;
    private readonly JauntyDb _db;

    public DdlSchemaSourceTests()
    {
        string dbName = "jauntyq_ddl_" + Guid.NewGuid().ToString("N");
        string cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();

        string ddl = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema.sqlite.sql"));
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        _conn = new SqliteConnection(cs);
        _conn.Open();
        _db = new JauntyDb(_conn);
    }

    [Fact]
    public void AutoCrud_GeneratedFromDdl_ReadsSeededRows()
    {
        List<Widget> all = _db.Widgets.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, w => w.Name == "Sprocket" && w.Quantity == 10);
        Assert.Contains(all, w => w.Name == "Cog" && w.Quantity == 20);
    }

    [Fact]
    public void AutoCrud_GeneratedFromDdl_GetByIdRoundTrips()
    {
        Widget seed = _db.Widgets.GetAll().OrderBy(w => w.WidgetId).First();

        Widget? fetched = _db.Widgets.GetById(seed.WidgetId);

        Assert.NotNull(fetched);
        Assert.Equal(seed.Name, fetched!.Name);
        Assert.Equal(seed.Quantity, fetched.Quantity);
    }

    public void Dispose()
    {
        _conn.Dispose();
        _keepAlive.Dispose();
    }
}
