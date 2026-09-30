using System.Linq;
using System.Threading.Tasks;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public sealed class PostgresMutationCoverageFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public DatabaseSchema Schema { get; private set; } = new();
    public DatabaseSchema SalesSchema { get; private set; } = new();
    public DatabaseSchema WhitespaceSchema { get; private set; } = new();

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.Postgres;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        var connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_pg_mutation");

        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TYPE mood AS ENUM ('sad','happy');
                CREATE DOMAIN ssn AS VARCHAR(11);
                CREATE TYPE money_pair AS (amount NUMERIC(12,2), currency CHAR(3));

                CREATE TABLE items (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(40) NOT NULL,
                    price NUMERIC(10,2) NULL,
                    qty INTEGER NULL,
                    national_id ssn NULL,
                    feeling mood NULL
                );
                CREATE INDEX items_name_idx ON items (name);

                CREATE VIEW item_names AS SELECT id, name FROM items;

                CREATE MATERIALIZED VIEW item_mv AS
                    SELECT name, price, qty, feeling FROM items;

                CREATE SEQUENCE counter_seq START 5 INCREMENT 3 MINVALUE 2 MAXVALUE 900;
                SELECT nextval('counter_seq');

                CREATE PROCEDURE noop_proc() LANGUAGE sql AS $$ SELECT 1 $$;
                CREATE PROCEDURE io_proc(IN a INTEGER, INOUT b INTEGER, OUT c TEXT)
                    LANGUAGE plpgsql AS $$ BEGIN b := b + a; c := 'x'; END $$;

                CREATE FUNCTION named_fn(amount NUMERIC, rate NUMERIC) RETURNS NUMERIC
                    AS $$ SELECT amount * rate $$ LANGUAGE sql;
                CREATE FUNCTION unnamed_fn(INTEGER, TEXT) RETURNS INTEGER
                    AS $$ SELECT $1 $$ LANGUAGE sql;
                CREATE FUNCTION domain_fn(v ssn) RETURNS ssn
                    AS $$ SELECT v $$ LANGUAGE sql;

                CREATE SCHEMA sales;
                CREATE TABLE sales.orders (id INTEGER);";
            await cmd.ExecuteNonQueryAsync();
        }

        Schema = await new PostgresExtractor().ExtractAsync(connectionString);
        SalesSchema = await new PostgresExtractor("sales").ExtractAsync(connectionString);
        WhitespaceSchema = await new PostgresExtractor("   ").ExtractAsync(connectionString);
        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class PostgresExtractorMutationCoverageTests : IClassFixture<PostgresMutationCoverageFixture>
{
    private readonly PostgresMutationCoverageFixture _fx;
    public PostgresExtractorMutationCoverageTests(PostgresMutationCoverageFixture fx) => _fx = fx;

    [SkippableFact]
    public void ExplicitSchema_ExtractsOnlyThatSchema()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Equal(new[] { "orders" }, _fx.SalesSchema.Tables.Keys.ToArray());
    }

    [SkippableFact]
    public void WhitespaceSchema_FallsBackToPublic()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.True(_fx.WhitespaceSchema.Tables.ContainsKey("items"));
        Assert.False(_fx.WhitespaceSchema.Tables.ContainsKey("orders"));
        Assert.Equal("public", _fx.WhitespaceSchema.UserTypes["ssn"].Schema);
    }

    [SkippableFact]
    public void Enum_CarriesItsName()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Equal("mood", _fx.Schema.Enums["mood"].Name);
    }

    [SkippableFact]
    public void BaseTable_CarriesNameAndIsNotAView()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var table = _fx.Schema.Tables["items"];
        Assert.Equal("items", table.Name);
        Assert.False(table.IsView);
        Assert.True(_fx.Schema.Tables["item_names"].IsView);
    }

    [SkippableFact]
    public void BaseTableColumns_CarryEveryFacet()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var cols = _fx.Schema.Tables["items"].Columns;

        var id = cols["id"];
        Assert.True(id.IsIdentity);
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);
        Assert.Null(id.MaxLength);
        Assert.Null(id.IsUnicode);

        var name = cols["name"];
        Assert.False(name.IsNullable);
        Assert.False(name.IsIdentity);
        Assert.False(name.IsPrimaryKey);
        Assert.Equal(40, name.MaxLength);
        Assert.True(name.IsUnicode);
        Assert.Null(name.Precision);
        Assert.Null(name.ResolvedFromUserType);

        var price = cols["price"];
        Assert.True(price.IsNullable);
        Assert.Equal(10, price.Precision);
        Assert.Equal(2, price.Scale);

        var nationalId = cols["national_id"];
        Assert.Equal("ssn", nationalId.ResolvedFromUserType);
        Assert.Equal(11, nationalId.MaxLength);
    }

    [SkippableFact]
    public void MaterializedView_CapturedAsViewWithEveryFacet()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var mv = _fx.Schema.Tables["item_mv"];
        Assert.Equal("item_mv", mv.Name);
        Assert.True(mv.IsView);

        var name = mv.Columns["name"];
        Assert.Equal("name", name.Name);
        Assert.Equal("character varying", name.DbType);
        Assert.True(name.IsNullable);
        Assert.Equal(40, name.MaxLength);
        Assert.True(name.IsUnicode);
        Assert.Null(name.EnumName);

        var price = mv.Columns["price"];
        Assert.Equal(10, price.Precision);
        Assert.Equal(2, price.Scale);

        var qty = mv.Columns["qty"];
        Assert.Equal("integer", qty.DbType);
        Assert.Null(qty.MaxLength);
        Assert.Null(qty.Precision);
        Assert.Null(qty.Scale);
        Assert.Null(qty.IsUnicode);
        Assert.Null(qty.EnumName);

        Assert.Equal("mood", mv.Columns["feeling"].EnumName);
    }

    [SkippableFact]
    public void Index_NeverClaimsAPrefixKeyPart()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var index = _fx.Schema.Tables["items"].Indexes.Single(i => i.Name == "items_name_idx");
        Assert.Equal(new[] { "name" }, index.Columns);
        Assert.False(index.HasPrefixKeyPart);
    }

    [SkippableFact]
    public void Sequence_CarriesBoundsAndCurrentValue()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var seq = _fx.Schema.Sequences["counter_seq"];
        Assert.Equal(5, seq.StartValue);
        Assert.Equal(3, seq.Increment);
        Assert.Equal(2, seq.MinValue);
        Assert.Equal(900, seq.MaxValue);
        Assert.Equal(5, seq.CurrentValue);
    }

    [SkippableFact]
    public void Procedures_CarryNameAndParameterDirections()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var noop = _fx.Schema.Procedures["noop_proc"];
        Assert.Equal("noop_proc", noop.Name);
        Assert.Empty(noop.Params);

        var io = _fx.Schema.Procedures["io_proc"];
        Assert.Equal(new[] { "a", "b", "c" }, io.Params.Select(p => p.Name));
        Assert.Equal(
            new[] { ProcedureParamDirection.In, ProcedureParamDirection.InOut, ProcedureParamDirection.Out },
            io.Params.Select(p => p.Direction));
    }

    [SkippableFact]
    public void Domain_CapturedWithBaseTypeAndFacets()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var ssn = _fx.Schema.UserTypes["ssn"];
        Assert.Equal("ssn", ssn.Name);
        Assert.Equal("public", ssn.Schema);
        Assert.Equal(UserTypeKind.Domain, ssn.Kind);
        Assert.True(ssn.IsNullable);
        Assert.Equal("character varying", ssn.UnderlyingDbType);
        Assert.Equal(11, ssn.MaxLength);
        Assert.Empty(ssn.Members);
    }

    [SkippableFact]
    public void Composite_CapturedUnresolvedWithMembers()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var pair = _fx.Schema.UserTypes["money_pair"];
        Assert.Equal(UserTypeKind.Composite, pair.Kind);
        Assert.Null(pair.UnderlyingDbType);
        Assert.Equal(new[] { "amount", "currency" }, pair.Members.Select(m => m.Name));

        var amount = pair.Members[0];
        Assert.Equal("numeric", amount.DbType);
        Assert.Equal(12, amount.Precision);
        Assert.Equal(2, amount.Scale);
        Assert.True(amount.IsNullable);

        Assert.Equal(3, pair.Members[1].MaxLength);
    }

    [SkippableFact]
    public void NamedFunction_CarriesSignature()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = _fx.Schema.Functions.Values.Single(f => f.Name == "named_fn");
        Assert.Equal("public", fn.Schema);
        Assert.Equal("numeric", fn.Return.DbType);
        Assert.Equal(new[] { "amount", "rate" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "numeric", "numeric" }, fn.Params.Select(p => p.DbType));
    }

    [SkippableFact]
    public void UnnamedFunction_ParamsArePositional()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = _fx.Schema.Functions.Values.Single(f => f.Name == "unnamed_fn");
        Assert.Equal(new[] { "$1", "$2" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "integer", "text" }, fn.Params.Select(p => p.DbType));
    }

    [SkippableFact]
    public void DomainTypedFunction_ResolvesParamAndReturn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = _fx.Schema.Functions.Values.Single(f => f.Name == "domain_fn");
        var param = Assert.Single(fn.Params);
        Assert.Equal("character varying", param.DbType);
        Assert.Equal("ssn", param.ResolvedFromUserType);
        Assert.Equal(11, param.MaxLength);
        Assert.Equal("ssn", fn.Return.ResolvedFromUserType);
    }
}

public class PostgresResolveDbTypeMutationTests
{
    [Fact]
    public void NonArrayWithUnderscoreUdtName_KeepsDataType()
    {
        Assert.Equal("_ssn", PostgresExtractor.ResolveDbType("_ssn", "_ssn"));
    }

    [Fact]
    public void ArrayWithoutUnderscoreUdtName_KeepsDataType()
    {
        Assert.Equal("ARRAY", PostgresExtractor.ResolveDbType("ARRAY", "int4"));
    }

    [Fact]
    public void ArrayWithUnderscoreUdtName_BecomesElementArray()
    {
        Assert.Equal("int4[]", PostgresExtractor.ResolveDbType("ARRAY", "_int4"));
    }
}

public class IndexCaptureMissingTableTests
{
    [Fact]
    public void AddIndexColumn_UnknownTable_ChangesNothing()
    {
        var schema = new DatabaseSchema();
        schema.Tables["t"] = new TableSchema { Name = "t" };

        IndexCapture.AddIndexColumn(schema, "missing", "ix", isUnique: true, columnName: "c");

        Assert.Single(schema.Tables);
        Assert.Empty(schema.Tables["t"].Indexes);
    }
}
