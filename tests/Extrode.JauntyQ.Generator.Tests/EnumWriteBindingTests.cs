using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Spec 013 T12/T12a: every path that hands an enum to the database.
///
/// Writes are not one site, and a missed one is silent — the enum binds as its
/// underlying integer and either the server rejects it with a type error
/// naming nothing useful, or (worse) some provider coerces it. Each site gets
/// its own assertion here rather than one end-to-end check, because a single
/// happy-path test passes while four of the six sites are still wrong.
/// </summary>
public class EnumWriteBindingTests
{
    private static string SchemaJson(string dialect)
    {
        bool pg = dialect == "postgres";
        return $@"{{
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
        ""id"": {{ ""name"": ""id"", ""dbType"": ""{(pg ? "integer" : "int")}"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""status"": {{ ""name"": ""status"", ""dbType"": ""{(pg ? "order_status" : "enum")}"", ""isNullable"": false, ""enumName"": ""order_status"" }},
        ""prior_status"": {{ ""name"": ""prior_status"", ""dbType"": ""{(pg ? "order_status" : "enum")}"", ""isNullable"": true, ""enumName"": ""order_status"" }}
      }}
    }}
  }}
}}";
    }

    private static GeneratorDriverRunResult Run(string dialect, params (string path, string sql)[] sqlFiles)
    {
        var compilation = CSharpCompilation.Create("EnumWriteTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson(dialect)));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string Sources(string dialect, params (string path, string sql)[] sqlFiles)
        => string.Join("\n", Run(dialect, sqlFiles).Results[0].GeneratedSources
            .Select(s => s.SourceText.ToString()));

    // ---- T12a: Postgres parameters ---------------------------------------

    /// <summary>
    /// The T0 probe finding. A Postgres enum column rejects a plain string
    /// parameter with 42804, and rejects DbType.String the same way.
    /// NpgsqlDbType.Unknown with no DbType is the one combination that works,
    /// because it defers the decision to the server's text-to-enum coercion.
    /// </summary>
    [Fact]
    public void PostgresScalarParameter_UsesNpgsqlDbTypeUnknown_AndNoDbType()
    {
        string src = Sources("postgres",
            ("db/Orders/ByStatus.sql", "select id, status from orders where status = @status"));

        Assert.Contains("NpgsqlDbType = NpgsqlDbType.Unknown;", src.Replace(".NpgsqlDbType = ", "NpgsqlDbType = "));
        Assert.Contains("OrderStatusValues.ToWire(status)", src);
        // DbType alongside NpgsqlDbType is exactly what the server rejects.
        Assert.DoesNotContain("__p0.DbType =", src);
    }

    [Fact]
    public void PostgresEmission_ImportsNpgsqlTypesNamespace()
    {
        string src = Sources("postgres",
            ("db/Orders/ByStatus.sql", "select id, status from orders where status = @status"));

        Assert.Contains("using NpgsqlTypes;", src);
    }

    /// <summary>
    /// A nullable enum parameter must still coalesce to DBNull, or the
    /// parameter goes out with no value at all.
    /// </summary>
    [Fact]
    public void PostgresNullableEnumParameter_CoalescesToDbNull()
    {
        string src = Sources("postgres",
            ("db/Orders/ByPrior.sql", "select id, status from orders where prior_status = @prior_status"));

        Assert.Contains("prior_status is null ? (object)DBNull.Value : OrderStatusValues.ToWire(prior_status.Value)", src);
    }

    // ---- T12: the non-Postgres scalar path -------------------------------

    /// <summary>
    /// The most-reached write site of all: every MySQL query parameter and
    /// every CRUD write parameter on any non-Postgres dialect.
    /// </summary>
    [Fact]
    public void MySqlScalarParameter_BindsWireValueAsString()
    {
        string src = Sources("mysql",
            ("db/Orders/ByStatus.sql", "select id, status from orders where status = @status"));

        Assert.Contains("OrderStatusValues.ToWire(status)", src);
        Assert.Contains("DbType.String;", src);
    }

    // ---- T12: -- @each IN-list -------------------------------------------

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    public void EachParameter_OverEnumColumn_BindsWireValues(string dialect)
    {
        string src = Sources(dialect,
            ("db/Orders/ByStatuses.sql",
             "-- @each status\nselect id, status from orders where status in (@status)"));

        Assert.Contains("OrderStatusValues.ToWire(status[", src);
    }

    /// <summary>
    /// Scoped to the each loop's own parameter variable. The bare literal
    /// "NpgsqlDbType.Unknown;" also appears in the auto-CRUD scalar binding,
    /// so asserting on it alone passes even if the each path never sets it.
    /// </summary>
    [Fact]
    public void EachParameter_OnPostgres_AlsoUsesNpgsqlDbTypeUnknown()
    {
        string src = Sources("postgres",
            ("db/Orders/ByStatuses.sql",
             "-- @each status\nselect id, status from orders where status in (@status)"));

        Assert.Matches(
            @"ParameterName = ""@status"" \+ __ib_status \};\s*\r?\n\s*\w+\.NpgsqlDbType = NpgsqlDbType\.Unknown;",
            src);
    }

    /// <summary>
    /// The MySQL half of the each split. Same looseness problem: "DbType.String;"
    /// alone is also emitted by the auto-CRUD bulk-insert prelude.
    /// </summary>
    [Fact]
    public void EachParameter_OnMySql_SetsDbTypeString()
    {
        string src = Sources("mysql",
            ("db/Orders/ByStatuses.sql",
             "-- @each status\nselect id, status from orders where status in (@status)"));

        Assert.Matches(
            @"ParameterName = ""@status"" \+ __ib_status;\s*\r?\n\s*\w+\.DbType = DbType\.String;",
            src);
    }

    /// <summary>
    /// A nullable enum column on the right of IN. The element type is
    /// OrderStatus?, which ToWire cannot accept: the unwrap has to happen
    /// inside a null branch, exactly as the scalar and COPY paths do. Without
    /// it the consumer's own compile fails with CS1503, so this is a test the
    /// generator's string output can catch before a consumer does.
    /// </summary>
    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    public void EachParameter_OverNullableEnumColumn_UnwrapsInsideNullBranch(string dialect)
    {
        string src = Sources(dialect,
            ("db/Orders/ByPriors.sql",
             "-- @each prior_status\nselect id, status from orders where prior_status in (@prior_status)"));

        Assert.Contains(
            "prior_status[__ib_prior_status] is null ? (object)DBNull.Value : OrderStatusValues.ToWire(prior_status[__ib_prior_status].Value)",
            src);
        // The bare form is what does not compile.
        Assert.DoesNotContain(
            "OrderStatusValues.ToWire(prior_status[__ib_prior_status]);", src);
    }

    // ---- T12: POCO insert and bulk paths ---------------------------------

    [Fact]
    public void PocoInsert_BindsWireValue()
    {
        string src = Sources("sqlite");

        Assert.Contains("OrderStatusValues.ToWire(row.Status)", src);
    }

    [Fact]
    public void PostgresBinaryCopy_WritesWireValue()
    {
        string src = Sources("postgres");

        Assert.Contains("__importer.Write(OrderStatusValues.ToWire(row.Status))", src);
        // Nullable column keeps WriteNull, and unwraps before converting.
        Assert.Contains("__importer.Write(OrderStatusValues.ToWire(row.PriorStatus.Value))", src);
        Assert.Contains("__importer.WriteNull();", src);
    }

    /// <summary>
    /// GetValue, GetFieldType and GetString must agree. An adapter reporting
    /// typeof(OrderStatus) while handing back a string gives MySqlBulkCopy
    /// contradictory metadata — the failure the plan called out specifically.
    /// </summary>
    [Fact]
    public void MySqlBulkReaderAdapter_ValueAndFieldTypeAgree()
    {
        string src = Sources("mysql");

        Assert.Contains("return OrderStatusValues.ToWire(_current.Status);", src);
        Assert.Contains("return typeof(string);", src);
        Assert.DoesNotContain("typeof(OrderStatus)", src);
    }

    // ---- Nothing binds the enum itself -----------------------------------

    /// <summary>
    /// The catch-all for a site this file forgot. Every generated assignment
    /// of an enum-typed property or parameter to a provider must go through
    /// ToWire; a bare one is a missed write site.
    /// </summary>
    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void NoWriteSite_BindsTheEnumItself(string dialect)
    {
        string src = Sources(dialect,
            ("db/Orders/ByStatus.sql", "select id, status from orders where status = @status"));

        Assert.DoesNotContain(".Value = row.Status;", src);
        Assert.DoesNotContain(".Value = status;", src);
        Assert.DoesNotContain("TypedValue = status", src);
        Assert.DoesNotContain("__importer.Write(row.Status)", src);
        // The nullable spellings of the same four, which the non-nullable
        // checks above do not cover -- a site that forgot ToWire on a nullable
        // enum emits the ordinary reference-type coalesce instead.
        Assert.DoesNotContain(".Value = (object?)row.PriorStatus ?? DBNull.Value;", src);
        Assert.DoesNotContain(".Value = (object?)prior_status ?? DBNull.Value;", src);
        Assert.DoesNotContain("__importer.Write(row.PriorStatus.Value)", src);
        Assert.DoesNotContain("return _current.Status;", src);
        Assert.DoesNotContain("return _current.PriorStatus;", src);
    }
}
