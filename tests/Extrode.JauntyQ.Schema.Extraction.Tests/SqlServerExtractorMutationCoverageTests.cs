using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public sealed class SqlServerMutationCoverageFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_sqlserver_mutation");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var batch in Batches)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly string[] Batches =
    [
        @"CREATE TYPE dbo.dec_t FROM decimal(12,4) NOT NULL;
          CREATE TYPE dbo.num_t FROM numeric(9,3) NULL;
          CREATE TYPE dbo.cash_t FROM money NULL;
          CREATE TYPE dbo.small_cash_t FROM smallmoney NULL;
          CREATE TYPE dbo.code_t FROM varchar(20) NOT NULL;
          CREATE TYPE dbo.label_t FROM nvarchar(50) NULL;
          CREATE TYPE dbo.count_t FROM int NULL;",
        @"CREATE TYPE dbo.line_tt AS TABLE (
              sku varchar(20) NOT NULL,
              label nvarchar(30) NULL,
              price decimal(10,2) NOT NULL,
              ratio numeric(6,3) NULL,
              amount money NULL,
              fee smallmoney NULL,
              qty int NOT NULL,
              body nvarchar(max) NULL);",
        @"CREATE TABLE dbo.items (
              id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
              code dbo.code_t,
              name nvarchar(40) NOT NULL,
              note varchar(10) NULL,
              price decimal(10,2) NOT NULL,
              tally int NULL);",
        "CREATE VIEW dbo.v_items AS SELECT id, name FROM dbo.items;",
        "CREATE PROCEDURE dbo.p_noargs AS SELECT 1, id, note FROM dbo.items;",
        "CREATE PROCEDURE dbo.p_shape AS SELECT id, name, note FROM dbo.items;",
        @"CREATE PROCEDURE dbo.p_args @code dbo.code_t, @amount decimal(9,2), @lines dbo.line_tt READONLY, @total int OUTPUT
          AS SELECT @total = COUNT(*) FROM @lines;",
        "CREATE SEQUENCE dbo.seq_a AS bigint START WITH 5 INCREMENT BY 3 MINVALUE 2 MAXVALUE 1000;",
        "CREATE FUNCTION dbo.f_noargs() RETURNS int AS BEGIN RETURN 1 END;",
        "CREATE FUNCTION dbo.f_dec() RETURNS decimal(10,2) AS BEGIN RETURN 1.5 END;",
        @"CREATE FUNCTION dbo.f_label(@name nvarchar(30), @amt decimal(8,3), @c dbo.code_t)
          RETURNS nvarchar(50) AS BEGIN RETURN @name END;",
        "CREATE FUNCTION dbo.f_alias() RETURNS dbo.code_t AS BEGIN RETURN 'x' END;",
        "CREATE SCHEMA app;",
        "CREATE TABLE app.widgets (id int NOT NULL PRIMARY KEY);"
    ];
}

public class SqlServerExtractorMutationCoverageTests : IClassFixture<SqlServerMutationCoverageFixture>
{
    private readonly SqlServerMutationCoverageFixture _fx;
    public SqlServerExtractorMutationCoverageTests(SqlServerMutationCoverageFixture fx) => _fx = fx;

    private Task<DatabaseSchema> Extract(string schema = SqlServerExtractor.DefaultSchema) =>
        new SqlServerExtractor(schema).ExtractAsync(_fx.ConnectionString);

    [SkippableTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankSchemaArgument_FallsBackToDbo(string blank)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await Extract(blank);

        Assert.Equal(new[] { "items", "v_items" }, schema.Tables.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [SkippableFact]
    public async Task NamedSchemaArgument_ScopesExtractionToThatSchema()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await Extract("app");

        Assert.Equal(new[] { "widgets" }, schema.Tables.Keys);
    }

    [SkippableFact]
    public async Task TableColumns_CarryExactFacets()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await Extract();
        var items = schema.Tables["items"];
        var cols = items.Columns;

        Assert.Equal("items", items.Name);
        Assert.False(items.IsView);
        Assert.True(cols["id"].IsIdentity);
        Assert.False(cols["name"].IsIdentity);
        Assert.False(cols["tally"].IsIdentity);
        Assert.Equal(10, cols["price"].Precision);
        Assert.Equal(2, cols["price"].Scale);
        Assert.Null(cols["id"].Precision);
        Assert.Null(cols["id"].Scale);
        Assert.True(cols["name"].IsUnicode);
        Assert.False(cols["note"].IsUnicode);
        Assert.Null(cols["id"].IsUnicode);
        Assert.Equal("code_t", cols["code"].ResolvedFromUserType);
        Assert.Equal("varchar", cols["code"].DbType);
        Assert.Null(cols["name"].ResolvedFromUserType);
    }

    [SkippableFact]
    public async Task View_IsFlaggedAsView()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var view = (await Extract()).Tables["v_items"];

        Assert.Equal("v_items", view.Name);
        Assert.True(view.IsView);
        Assert.Equal(new[] { "id", "name" }, view.Columns.Keys);
    }

    [SkippableFact]
    public async Task ProcedureWithoutParameters_HasNoParams()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var procs = (await Extract()).Procedures;

        Assert.Empty(procs["p_noargs"].Params);
        Assert.Empty(procs["p_shape"].Params);
    }

    [SkippableFact]
    public async Task ProcedureParameters_CarryExactTypesAndFacets()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var ps = (await Extract()).Procedures["p_args"].Params;

        Assert.Equal(new[] { "code", "amount", "lines", "total" }, ps.Select(p => p.Name));
        Assert.Equal(new[] { "varchar", "decimal", "line_tt", "int" }, ps.Select(p => p.DbType));
        Assert.Equal(20, ps[0].MaxLength);
        Assert.Null(ps[0].Precision);
        Assert.Equal(9, ps[1].Precision);
        Assert.Equal(2, ps[1].Scale);
        Assert.Null(ps[3].Precision);
        Assert.Null(ps[3].Scale);
        Assert.Equal(
            new[] { ProcedureParamDirection.In, ProcedureParamDirection.In, ProcedureParamDirection.In, ProcedureParamDirection.InOut },
            ps.Select(p => p.Direction));
    }

    [SkippableFact]
    public async Task ProcedureResultShape_IsNamedColumnsInOrderWithBareTypes()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var procs = (await Extract()).Procedures;
        var shape = procs["p_shape"].Results;

        Assert.Equal(new[] { "id", "name", "note" }, shape.Select(c => c.Name));
        Assert.Equal(new[] { "int", "nvarchar", "varchar" }, shape.Select(c => c.DbType));
        Assert.Equal(new[] { false, false, true }, shape.Select(c => c.IsNullable));
        Assert.Equal(new[] { "id", "note" }, procs["p_noargs"].Results.Select(c => c.Name));
        Assert.Empty(procs["p_args"].Results);
    }

    [SkippableFact]
    public async Task Sequence_CarriesExactBounds()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var seq = (await Extract()).Sequences["seq_a"];

        Assert.Equal(5, seq.StartValue);
        Assert.Equal(3, seq.Increment);
        Assert.Equal(2, seq.MinValue);
        Assert.Equal(1000, seq.MaxValue);
        Assert.Equal(5, seq.CurrentValue);
    }

    [SkippableFact]
    public async Task AliasTypes_CarryBaseTypeNullabilityAndOnlyMeaningfulFacets()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var types = (await Extract()).UserTypes;

        foreach (var name in new[] { "dec_t", "num_t", "cash_t", "small_cash_t", "code_t", "label_t", "count_t" })
        {
            Assert.Equal(name, types[name].Name);
            Assert.Equal("dbo", types[name].Schema);
            Assert.Equal(UserTypeKind.Alias, types[name].Kind);
        }

        Assert.Equal(("decimal", false, 9, 12, 4), Facets(types["dec_t"]));
        Assert.Equal(("numeric", true, 5, 9, 3), Facets(types["num_t"]));
        Assert.Equal(("money", true, 8, 19, 4), Facets(types["cash_t"]));
        Assert.Equal(("smallmoney", true, 4, 10, 4), Facets(types["small_cash_t"]));
        Assert.Equal(("varchar", false, 20, (int?)null, (int?)null), Facets(types["code_t"]));
        Assert.Equal(("nvarchar", true, 50, (int?)null, (int?)null), Facets(types["label_t"]));
        Assert.Equal(("int", true, 4, (int?)null, (int?)null), Facets(types["count_t"]));

        static (string?, bool, int?, int?, int?) Facets(UserTypeSchema t) =>
            (t.UnderlyingDbType, t.IsNullable, t.MaxLength, t.Precision, t.Scale);
    }

    [SkippableFact]
    public async Task TableType_CarriesMembersWithExactFacets()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var tt = (await Extract()).UserTypes["line_tt"];

        Assert.Equal("line_tt", tt.Name);
        Assert.Equal("dbo", tt.Schema);
        Assert.Equal(UserTypeKind.TableType, tt.Kind);
        Assert.Equal(
            new (string, string, bool, int?, int?, int?)[]
            {
                ("sku", "varchar", false, 20, null, null),
                ("label", "nvarchar", true, 30, null, null),
                ("price", "decimal", false, 9, 10, 2),
                ("ratio", "numeric", true, 5, 6, 3),
                ("amount", "money", true, 8, 19, 4),
                ("fee", "smallmoney", true, 4, 10, 4),
                ("qty", "int", false, 4, null, null),
                ("body", "nvarchar", true, -1, null, null)
            },
            tt.Members.Select(m => (m.Name, m.DbType, m.IsNullable, m.MaxLength, m.Precision, m.Scale)));
    }

    [SkippableFact]
    public async Task ScalarFunctions_CarryReturnParamsAndSignatureKeys()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fns = (await Extract()).Functions;

        Assert.Equal(
            new[] { "f_alias", "f_dec", "f_label(nvarchar,decimal,varchar)", "f_noargs" },
            fns.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var label = fns["f_label(nvarchar,decimal,varchar)"];
        Assert.Equal("f_label", label.Name);
        Assert.Equal("dbo", label.Schema);
        Assert.Equal(("nvarchar", 50, (int?)null, (int?)null), (label.Return.DbType, label.Return.MaxLength, label.Return.Precision, label.Return.Scale));
        Assert.Equal(
            new (string, string, int?, int?, int?, string?)[]
            {
                ("name", "nvarchar", 30, null, null, null),
                ("amt", "decimal", null, 8, 3, null),
                ("c", "varchar", 20, null, null, "code_t")
            },
            label.Params.Select(p => (p.Name, p.DbType, p.MaxLength, p.Precision, p.Scale, p.ResolvedFromUserType)));

        var dec = fns["f_dec"];
        Assert.Equal(("decimal", (int?)null, 10, 2), (dec.Return.DbType, dec.Return.MaxLength, dec.Return.Precision, dec.Return.Scale));

        var noargs = fns["f_noargs"];
        Assert.Equal("f_noargs", noargs.Name);
        Assert.Empty(noargs.Params);
        Assert.Equal(("int", (int?)null, (int?)null, (int?)null), (noargs.Return.DbType, noargs.Return.MaxLength, noargs.Return.Precision, noargs.Return.Scale));
    }
}
