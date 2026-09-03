using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Spec 013 T5/T6/T7 — the mapping layer resolves a column's captured
/// <c>enumName</c> to the generated enum type, on every path a column type can
/// be decided: the ordinary mapper, the ProjectionBuilder force-nullable
/// bypass, and the non-nullable-value-type predicate the write sites branch on.
/// </summary>
public class EnumMappingTests
{
    private const string PgEnum = "order_status";

    /// <summary>
    /// A PostgreSQL-shaped schema: a native enum type captured in
    /// <c>Enums</c>, with two columns pointing at it (one nullable) plus a
    /// same-named-type column on a second table, so the shared-type case is
    /// exercised alongside the join cases.
    /// </summary>
    private static DatabaseSchema PostgresSchema()
    {
        return new DatabaseSchema
        {
            Dialect = "postgres",
            Enums = new Dictionary<string, EnumSchema>
            {
                [PgEnum] = new EnumSchema
                {
                    Name = PgEnum,
                    Members = new List<EnumMember>
                    {
                        new EnumMember { Value = "pending", CSharpName = "Pending" },
                        new EnumMember { Value = "shipped", CSharpName = "Shipped" },
                        new EnumMember { Value = "canceled", CSharpName = "Canceled" }
                    }
                }
            },
            Tables = new Dictionary<string, TableSchema>
            {
                ["orders"] = new TableSchema
                {
                    Name = "orders",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsNullable = false },
                        ["status"] = new ColumnSchema { Name = "status", DbType = PgEnum, IsNullable = false, EnumName = PgEnum },
                        ["prior_status"] = new ColumnSchema { Name = "prior_status", DbType = PgEnum, IsNullable = true, EnumName = PgEnum }
                    }
                },
                ["shipments"] = new TableSchema
                {
                    Name = "shipments",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsNullable = false },
                        ["ship_status"] = new ColumnSchema { Name = "ship_status", DbType = PgEnum, IsNullable = false, EnumName = PgEnum }
                    }
                }
            }
        };
    }

    /// <summary>
    /// The MySQL shape: an inline column enum, so the entity is minted per
    /// column as {Table}{Column} and the column's DbType is the bare word
    /// "enum" that DialectMapper already has an arm for. That arm is what the
    /// enum resolution has to win against.
    /// </summary>
    private static DatabaseSchema MySqlSchema()
    {
        return new DatabaseSchema
        {
            Dialect = "mysql",
            Enums = new Dictionary<string, EnumSchema>
            {
                ["OrdersStatus"] = new EnumSchema
                {
                    Name = "OrdersStatus",
                    Members = new List<EnumMember>
                    {
                        new EnumMember { Value = "pending", CSharpName = "Pending" },
                        new EnumMember { Value = "shipped", CSharpName = "Shipped" }
                    }
                }
            },
            Tables = new Dictionary<string, TableSchema>
            {
                ["orders"] = new TableSchema
                {
                    Name = "orders",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["order_id"] = new ColumnSchema { Name = "order_id", DbType = "int", IsNullable = false },
                        ["status"] = new ColumnSchema { Name = "status", DbType = "enum", IsNullable = false, EnumName = "OrdersStatus" },
                        ["prior_status"] = new ColumnSchema { Name = "prior_status", DbType = "enum", IsNullable = true, EnumName = "OrdersStatus" }
                    }
                }
            }
        };
    }

    private static ColumnSchema Column(DatabaseSchema schema, string table, string column)
        => schema.Tables[table].Columns[column];

    private static QueryModel ParseSql(string sql, string name)
        => SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ---- T5: MapColumnToCSharp -------------------------------------------

    [Fact]
    public void MapColumnToCSharp_PostgresEnumColumn_MapsToEnumNotObject()
    {
        var schema = PostgresSchema();
        string mapped = DialectMapper.MapColumnToCSharp(Column(schema, "orders", "status"), "postgres", schema);

        Assert.Equal("OrderStatus", mapped);
    }

    [Fact]
    public void MapColumnToCSharp_PostgresNullableEnumColumn_MapsToNullableEnum()
    {
        var schema = PostgresSchema();
        string mapped = DialectMapper.MapColumnToCSharp(Column(schema, "orders", "prior_status"), "postgres", schema);

        Assert.Equal("OrderStatus?", mapped);
    }

    [Fact]
    public void MapColumnToCSharp_MySqlEnumColumn_MapsToEnumNotString()
    {
        var schema = MySqlSchema();
        string mapped = DialectMapper.MapColumnToCSharp(Column(schema, "orders", "status"), "mysql", schema);

        Assert.Equal("OrdersStatus", mapped);
    }

    [Fact]
    public void MapColumnToCSharp_MySqlNullableEnumColumn_MapsToNullableEnum()
    {
        var schema = MySqlSchema();
        string mapped = DialectMapper.MapColumnToCSharp(Column(schema, "orders", "prior_status"), "mysql", schema);

        Assert.Equal("OrdersStatus?", mapped);
    }

    /// <summary>
    /// The failure mode the sweep exists to prevent: the schema-less overload
    /// still compiles at every un-updated call site, and silently keeps the
    /// pre-013 mapping. Pinning it here makes the difference visible rather
    /// than merely present.
    /// </summary>
    [Fact]
    public void MapColumnToCSharp_WithoutSchema_KeepsPre013Mapping()
    {
        var pg = PostgresSchema();
        var my = MySqlSchema();

        Assert.Equal("object", DialectMapper.MapColumnToCSharp(Column(pg, "orders", "status"), "postgres"));
        Assert.Equal("string", DialectMapper.MapColumnToCSharp(Column(my, "orders", "status"), "mysql"));
    }

    [Fact]
    public void MapColumnToCSharp_NonEnumColumn_Unaffected()
    {
        var schema = PostgresSchema();

        Assert.Equal("int", DialectMapper.MapColumnToCSharp(Column(schema, "orders", "order_id"), "postgres", schema));
    }

    /// <summary>
    /// A snapshot written before 013, or one whose enum entity was dropped,
    /// leaves a dangling reference. It must fall through to the old mapping,
    /// not produce a type name nothing emits.
    /// </summary>
    [Fact]
    public void MapColumnToCSharp_EnumNameWithNoEntity_FallsBack()
    {
        var schema = PostgresSchema();
        schema.Enums.Clear();

        Assert.Equal("object", DialectMapper.MapColumnToCSharp(Column(schema, "orders", "status"), "postgres", schema));
    }

    [Fact]
    public void ResolveEnumTypeName_NoSchema_ReturnsNull()
    {
        var schema = PostgresSchema();

        Assert.Null(DialectMapper.ResolveEnumTypeName(Column(schema, "orders", "status"), null));
    }

    // ---- T6: ProjectionBuilder -------------------------------------------

    [Fact]
    public void Build_EnumColumn_ProjectsAsEnum()
    {
        var schema = PostgresSchema();
        var query = ParseSql("select o.status from orders o", "GetStatus");
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Equal("OrderStatus", Assert.Single(projection.Columns).Type);
    }

    /// <summary>
    /// The force-nullable bypass (ProjectionBuilder's MapProjectedColumnType):
    /// the optional side of a LEFT JOIN never reaches MapColumnToCSharp, so
    /// without its own enum resolution this column stays object?.
    /// </summary>
    [Fact]
    public void Build_EnumColumnOnOptionalSideOfLeftJoin_ProjectsAsNullableEnum()
    {
        var schema = PostgresSchema();
        var query = ParseSql(
            "select o.order_id, s.ship_status from orders o left join shipments s on s.order_id = o.order_id",
            "GetOrderShipments");
        var projection = ProjectionBuilder.Build(query, schema);

        var shipStatus = Assert.Single(projection.Columns, c => c.Name == "ShipStatus");
        Assert.Equal("OrderStatus?", shipStatus.Type);
    }

    [Fact]
    public void Build_NullableEnumColumnOnOptionalSide_DoesNotDoubleNullable()
    {
        var schema = PostgresSchema();
        var query = ParseSql(
            "select s.order_id, o.prior_status from shipments s left join orders o on o.order_id = s.order_id",
            "GetShipmentOrders");
        var projection = ProjectionBuilder.Build(query, schema);

        var priorStatus = Assert.Single(projection.Columns, c => c.Name == "PriorStatus");
        Assert.Equal("OrderStatus?", priorStatus.Type);
    }

    // ---- T7: IsNonNullableValueType --------------------------------------

    [Fact]
    public void IsEmittedEnumType_PostgresEnum_True()
    {
        Assert.True(CodeEmitter.IsEmittedEnumType("OrderStatus", PostgresSchema()));
    }

    [Fact]
    public void IsEmittedEnumType_MySqlEnum_True()
    {
        Assert.True(CodeEmitter.IsEmittedEnumType("OrdersStatus", MySqlSchema()));
    }

    /// <summary>
    /// Nullable is a reference-shaped spelling everywhere else in the emitter;
    /// callers trim the '?' themselves (CodeEmitter.Part13's bulk-copy
    /// nullableValueType check), so the predicate must say false for it.
    /// </summary>
    [Fact]
    public void IsEmittedEnumType_NullableSpelling_False()
    {
        Assert.False(CodeEmitter.IsEmittedEnumType("OrderStatus?", PostgresSchema()));
    }

    [Fact]
    public void IsEmittedEnumType_UnknownName_False()
    {
        Assert.False(CodeEmitter.IsEmittedEnumType("OrderStatusValues", PostgresSchema()));
        Assert.False(CodeEmitter.IsEmittedEnumType("string", PostgresSchema()));
    }

    [Fact]
    public void IsEmittedEnumType_NoSchema_False()
    {
        Assert.False(CodeEmitter.IsEmittedEnumType("OrderStatus", null));
    }

    /// <summary>
    /// A captured type with no members is not emitted, so nothing may treat
    /// its name as a value type.
    /// </summary>
    [Fact]
    public void IsEmittedEnumType_MemberlessEnum_False()
    {
        var schema = PostgresSchema();
        schema.Enums[PgEnum].Members.Clear();

        Assert.False(CodeEmitter.IsEmittedEnumType("OrderStatus", schema));
    }

    // ---- T7 through the emitter ------------------------------------------

    private static string BulkSchemaJson(string dialect) => $@"{{
  ""dialect"": ""{dialect}"",
  ""enums"": {{
    ""order_status"": {{
      ""name"": ""order_status"",
      ""members"": [
        {{ ""value"": ""pending"", ""csharpName"": ""Pending"" }},
        {{ ""value"": ""shipped"", ""csharpName"": ""Shipped"" }}
      ]
    }}
  }},
  ""tables"": {{
    ""orders"": {{
      ""name"": ""orders"",
      ""columns"": {{
        ""order_id"": {{ ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""status"": {{ ""name"": ""status"", ""dbType"": ""{(dialect == "postgres" ? "order_status" : "enum")}"", ""isNullable"": false, ""enumName"": ""order_status"" }}
      }}
    }}
  }}
}}";

    private static string GenerateAll(string schemaJson)
    {
        var compilation = CSharpCompilation.Create("EnumMappingTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return string.Join("\n", driver.GetRunResult().Results[0].GeneratedSources
            .Select(g => g.SourceText.ToString()));
    }

    /// <summary>
    /// The concrete failure T7 exists to prevent. PostgreSQL bulk insert is a
    /// binary COPY that writes properties directly, and the branch is chosen
    /// by IsNonNullableValueType. Left unaware of emitted enums it emits
    /// <c>if (row.Status is null)</c> against a struct property, which is
    /// CS0037 territory -- the generated code does not compile at all.
    /// </summary>
    [Fact]
    public void Emitted_PostgresBulkCopy_NonNullableEnum_TakesValueTypeBranch()
    {
        string src = GenerateAll(BulkSchemaJson("postgres"));

        // T12 wraps the value in ToWire; T7's invariant is the absence of the
        // null test, which would not compile against a struct property.
        Assert.Contains("__importer.Write(OrderStatusValues.ToWire(row.Status))", src);
        Assert.DoesNotContain("if (row.Status is null)", src);
    }

    /// <summary>
    /// Same predicate, the other bulk shape: MySQL goes through the emitted
    /// DbDataReader adapter, whose GetValue switch picks between a direct
    /// return and the DBNull coalesce.
    /// </summary>
    [Fact]
    public void Emitted_MySqlBulkReaderAdapter_NonNullableEnum_TakesValueTypeBranch()
    {
        string src = GenerateAll(BulkSchemaJson("mysql"));

        Assert.Contains("return OrderStatusValues.ToWire(_current.Status);", src);
        Assert.DoesNotContain("(object?)_current.Status ?? DBNull.Value", src);
        Assert.DoesNotContain("_current.Status is null", src);
    }

    /// <summary>
    /// The portable INSERT loop (CodeEmitter.Part12), which every dialect
    /// without a native bulk path takes -- and which binds the property to a
    /// DbParameter through the same predicate.
    /// </summary>
    [Fact]
    public void Emitted_PortableInsertLoop_NonNullableEnum_TakesValueTypeBranch()
    {
        string src = GenerateAll(BulkSchemaJson("sqlite"));

        Assert.Contains(".Value = OrderStatusValues.ToWire(row.Status);", src);
        Assert.DoesNotContain("(object?)row.Status ?? DBNull.Value", src);
        Assert.DoesNotContain("row.Status is null", src);
    }
}
