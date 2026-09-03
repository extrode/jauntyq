using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class FunctionGenerationTests
{
    private const string TablesAndKeys = @"
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [],";

    private static string Schema(string dialect, string functions, string userTypes = "{}", string? tables = null) => @"{
  ""dialect"": """ + dialect + @"""," + (tables ?? TablesAndKeys) + @"
  ""userTypes"": " + userTypes + @",
  ""functions"": " + functions + @"
}";

    private const string CalcTax = @"{
    ""calc_tax(decimal,decimal)"": {
      ""name"": ""calc_tax"",
      ""schema"": ""dbo"",
      ""params"": [
        { ""name"": ""amount"", ""dbType"": ""decimal"", ""isNullable"": false },
        { ""name"": ""rate"", ""dbType"": ""decimal"", ""isNullable"": false }
      ],
      ""returnType"": { ""dbType"": ""decimal"", ""isNullable"": true }
    }
  }";

    private static (GeneratorDriverRunResult result, Compilation compilation) Run(string schemaJson)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
        };

        var compilation = CSharpCompilation.Create("FunctionTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)
        };

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static string? TryGetSource(GeneratorDriverRunResult result, string hintSuffix)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            if (tree.FilePath.EndsWith(hintSuffix, StringComparison.OrdinalIgnoreCase))
                return tree.ToString();
        }
        return null;
    }

    private static void AssertCompiles(Compilation compilation) =>
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

    [Fact]
    public void SqlServer_EmitsFunctionAccessor_InFourOverloads()
    {
        var (result, compilation) = Run(Schema("sqlserver", CalcTax));

        var db = TryGetSource(result, "JauntyDb.g.cs");
        Assert.NotNull(db);
        Assert.Contains("public FunctionAccessor Functions =>", db);
        Assert.Contains("public sealed class FunctionAccessor", db);

        Assert.Contains("public decimal? CalcTax(decimal amount, decimal rate)", db);
        Assert.Contains("public static decimal? CalcTax(DbConnection conn, decimal amount, decimal rate, DbTransaction? transaction = null)", db);
        Assert.Contains("public async Task<decimal?> CalcTaxAsync(decimal amount, decimal rate, CancellationToken cancellationToken = default)", db);
        Assert.Contains("public static async Task<decimal?> CalcTaxAsync(DbConnection conn, decimal amount, decimal rate, DbTransaction? transaction = null, CancellationToken cancellationToken = default)", db);

        AssertCompiles(compilation);
    }

    [Fact]
    public void TheCallGoesOutAsTextThroughTheAbstractCommand()
    {
        // The property the whole P1 design rests on (014-plan.md §1.1): a
        // generated function call must stay usable from a consumer holding
        // only a DbConnection out of a DbProviderFactory. A provider type
        // reaching this path is what deferred TVPs, so its absence here is
        // asserted rather than assumed.
        var (result, compilation) = Run(Schema("sqlserver", CalcTax));

        var db = TryGetSource(result, "JauntyDb.g.cs")!;
        int accessor = db.IndexOf("public sealed class FunctionAccessor", StringComparison.Ordinal);
        Assert.True(accessor >= 0);
        string body = db.Substring(accessor);

        Assert.Contains("CommandType.Text", body);
        Assert.DoesNotContain("CommandType.StoredProcedure", body);
        Assert.DoesNotContain("SqlParameter", body);
        Assert.DoesNotContain("SqlCommand", body);
        Assert.DoesNotContain("NpgsqlParameter", body);
        Assert.DoesNotContain("MySqlParameter", body);
        Assert.DoesNotContain("SqlDbType", body);

        AssertCompiles(compilation);
    }

    [Fact]
    public void SqlServerAndPostgres_QualifyTheCallWithTheSchema()
    {
        var (sqlServer, sqlServerCompilation) = Run(Schema("sqlserver", CalcTax));
        var (postgres, postgresCompilation) = Run(Schema("postgres", CalcTax.Replace(@"""dbo""", @"""public""")));

        Assert.Contains("SELECT dbo.calc_tax(@amount, @rate)", TryGetSource(sqlServer, "JauntyDb.g.cs")!);
        Assert.Contains("SELECT public.calc_tax(@amount, @rate)", TryGetSource(postgres, "JauntyDb.g.cs")!);

        AssertCompiles(sqlServerCompilation);
        AssertCompiles(postgresCompilation);
    }

    [Fact]
    public void MySql_DoesNotQualifyTheCall()
    {
        // MySQL's "schema" IS the database the connection is already on, so a
        // qualifier bakes the capture-time database name into every call site
        // and breaks the moment the same snapshot runs against a differently
        // named one -- which is the normal dev/staging/prod case, not an edge.
        var (result, compilation) = Run(Schema("mysql", CalcTax.Replace(@"""dbo""", @"""jauntyq_live""")));

        var db = TryGetSource(result, "JauntyDb.g.cs")!;
        Assert.Contains("SELECT calc_tax(@amount, @rate)", db);
        Assert.DoesNotContain("jauntyq_live.calc_tax", db);

        AssertCompiles(compilation);
    }

    [Fact]
    public void AZeroArgumentFunction_EmitsAParameterlessMethod()
    {
        var (result, compilation) = Run(Schema("sqlserver", @"{
    ""answer"": {
      ""name"": ""answer"",
      ""schema"": ""dbo"",
      ""params"": [],
      ""returnType"": { ""dbType"": ""int"", ""isNullable"": false }
    }
  }"));

        var db = TryGetSource(result, "JauntyDb.g.cs")!;
        Assert.Contains("public int Answer()", db);
        Assert.Contains("public static int Answer(DbConnection conn, DbTransaction? transaction = null)", db);
        Assert.Contains("SELECT dbo.answer()", db);

        AssertCompiles(compilation);
    }

    [Fact]
    public void SqliteNeverGetsAFunctionAccessor()
    {
        // Belt to the extractor's braces. SQLite populates no functions, so a
        // snapshot carrying some is a hand-edited or mislabelled one -- and
        // emitting a SELECT in a call syntax nobody verified against SQLite
        // would be worse than emitting nothing.
        var (result, compilation) = Run(Schema("sqlite", CalcTax));

        Assert.DoesNotContain("FunctionAccessor", TryGetSource(result, "JauntyDb.g.cs")!);

        AssertCompiles(compilation);
    }

    [Fact]
    public void CollidingFunctionNames_ReportJNT2023_AndEmitNeither()
    {
        // Unlike JNT2009's rule for sequences, NEITHER survives. Two sequence
        // accessors that collide are both nullary and differ only in which
        // sequence advances, so keeping the first is a defensible arbitrary
        // choice. Two function overloads differ in their PARAMETERS, so
        // keeping the first would bind every call site to one arbitrary
        // signature and compile clean while calling the wrong function.
        var (result, compilation) = Run(Schema("postgres", @"{
    ""calc_tax(numeric)"": {
      ""name"": ""calc_tax"",
      ""schema"": ""public"",
      ""params"": [ { ""name"": ""amount"", ""dbType"": ""numeric"", ""isNullable"": false } ],
      ""returnType"": { ""dbType"": ""numeric"", ""isNullable"": true }
    },
    ""calc_tax(numeric,numeric)"": {
      ""name"": ""calc_tax"",
      ""schema"": ""public"",
      ""params"": [
        { ""name"": ""amount"", ""dbType"": ""numeric"", ""isNullable"": false },
        { ""name"": ""rate"", ""dbType"": ""numeric"", ""isNullable"": false }
      ],
      ""returnType"": { ""dbType"": ""numeric"", ""isNullable"": true }
    }
  }"));

        var collision = Assert.Single(result.Diagnostics, d => d.Id == "JNT2023");
        string message = collision.GetMessage();
        Assert.Contains("calc_tax(numeric)", message);
        Assert.Contains("calc_tax(numeric, numeric)", message);
        Assert.Contains("db.Functions.CalcTax()", message);

        Assert.DoesNotContain("CalcTax(", TryGetSource(result, "JauntyDb.g.cs")!);

        AssertCompiles(compilation);
    }

    [Fact]
    public void AnUnmappableReturnType_ReportsJNT2024_AndEmitsNoMethod()
    {
        var (result, compilation) = Run(Schema("postgres", @"{
    ""shape_of(int)"": {
      ""name"": ""shape_of"",
      ""schema"": ""public"",
      ""params"": [ { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false } ],
      ""returnType"": { ""dbType"": ""some_unmapped_thing"", ""isNullable"": true }
    }
  }"));

        var unmappable = Assert.Single(result.Diagnostics, d => d.Id == "JNT2024");
        Assert.Contains("shape_of(int)", unmappable.GetMessage());
        Assert.Contains("some_unmapped_thing", unmappable.GetMessage());

        Assert.DoesNotContain("ShapeOf(", TryGetSource(result, "JauntyDb.g.cs")!);

        AssertCompiles(compilation);
    }

    [Fact]
    public void AnUnmappableParameterType_ReportsJNT2024_AndEmitsNoMethod()
    {
        // Separate from the return case because the two go through different
        // branches: a bad return is caught before any parameter is mapped, so
        // a test on one proves nothing about the other.
        var (result, compilation) = Run(Schema("postgres", @"{
    ""takes_odd(some_unmapped_thing)"": {
      ""name"": ""takes_odd"",
      ""schema"": ""public"",
      ""params"": [ { ""name"": ""thing"", ""dbType"": ""some_unmapped_thing"", ""isNullable"": false } ],
      ""returnType"": { ""dbType"": ""int"", ""isNullable"": true }
    }
  }"));

        var unmappable = Assert.Single(result.Diagnostics, d => d.Id == "JNT2024");
        Assert.Contains("parameter 'thing'", unmappable.GetMessage());

        Assert.DoesNotContain("TakesOdd(", TryGetSource(result, "JauntyDb.g.cs")!);

        AssertCompiles(compilation);
    }

    [Fact]
    public void ATableValuedParameter_ReportsJNT2025_NamingTheTypesColumns()
    {
        // JNT2025 replaces the attractive nuisance the spec would otherwise
        // have shipped: a method taking `object` that fails at runtime with
        // JNT2007's generic "unmapped type" story. The column list is why the
        // extractors capture a type the generator will never emit for -- a
        // refusal that names the shape is enough to write the temporary table
        // the message suggests.
        var (result, compilation) = Run(Schema("sqlserver", @"{
    ""sum_ids(IdList)"": {
      ""name"": ""sum_ids"",
      ""schema"": ""dbo"",
      ""params"": [ { ""name"": ""ids"", ""dbType"": ""IdList"", ""isNullable"": false } ],
      ""returnType"": { ""dbType"": ""int"", ""isNullable"": true }
    }
  }", userTypes: @"{
    ""IdList"": {
      ""name"": ""IdList"",
      ""schema"": ""dbo"",
      ""kind"": ""TableType"",
      ""members"": [
        { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""label"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": 50 }
      ]
    }
  }"));

        var refusal = Assert.Single(result.Diagnostics, d => d.Id == "JNT2025");
        string message = refusal.GetMessage();
        Assert.Contains("sum_ids(IdList)", message);
        Assert.Contains("(id int, label nvarchar)", message);

        Assert.DoesNotContain("SumIds(", TryGetSource(result, "JauntyDb.g.cs")!);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2024");

        AssertCompiles(compilation);
    }

    [Fact]
    public void AnAliasResolvedColumnMapsToThePrimitive_WithoutJNT2007()
    {
        // The generator half of FR-004, which is small precisely because
        // UserTypeResolution rewrites DbType at CAPTURE time. Asserting it
        // anyway: "small because it happens elsewhere" and "not happening"
        // produce the same diff, and only this test tells them apart.
        var (result, compilation) = Run(Schema("sqlserver", "{}", userTypes: @"{
    ""NationalId"": {
      ""name"": ""NationalId"",
      ""schema"": ""dbo"",
      ""kind"": ""Alias"",
      ""underlyingDbType"": ""varchar"",
      ""maxLength"": 11
    }
  }", tables: @"
  ""tables"": {
    ""people"": {
      ""name"": ""people"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""national_id"": { ""name"": ""national_id"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 11, ""resolvedFromUserType"": ""NationalId"" }
      }
    }
  },
  ""foreignKeys"": [],"));

        var poco = TryGetSource(result, "People.Row.g.cs");
        Assert.NotNull(poco);
        Assert.Contains("string NationalId", poco);
        Assert.DoesNotContain("object NationalId", poco);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2007");

        AssertCompiles(compilation);
    }

    [Fact]
    public void AFunctionParameterNamedConn_DoesNotCollideWithTheStaticOverloadsOwn()
    {
        // AUD-R68-01's defect family, on the new path: "conn",  "transaction"
        // and "cancellationToken" are JauntyQ-introduced formal parameters, so
        // a database function whose own parameter is named one of them
        // produces a duplicate-parameter failure INSIDE generated code, which
        // is the worst place for it to surface. Here every function parameter
        // is required and there is no overload without the bookkeeping ones,
        // so the schema-derived name is the one that moves; the SQL
        // placeholder keeps the database's name so the call still binds.
        var (result, compilation) = Run(Schema("sqlserver", @"{
    ""echo(int,int,int)"": {
      ""name"": ""echo"",
      ""schema"": ""dbo"",
      ""params"": [
        { ""name"": ""conn"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""transaction"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""cancellationToken"", ""dbType"": ""int"", ""isNullable"": false }
      ],
      ""returnType"": { ""dbType"": ""int"", ""isNullable"": true }
    }
  }"));

        var db = TryGetSource(result, "JauntyDb.g.cs")!;
        Assert.Contains("public int? Echo(int __conn, int __transaction, int __cancellationToken)", db);
        Assert.Contains("SELECT dbo.echo(@conn, @transaction, @cancellationToken)", db);

        AssertCompiles(compilation);
    }

    [Fact]
    public void AReservedFunctionName_IsSkippedRatherThanEmittedUnquoted()
    {
        // Same trust boundary EmitSequenceAccessor guards: the function name
        // crosses into both a C# method name and an unquoted SQL identifier.
        var (result, compilation) = Run(Schema("postgres", @"{
    ""select(int)"": {
      ""name"": ""select"",
      ""schema"": ""public"",
      ""params"": [ { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false } ],
      ""returnType"": { ""dbType"": ""int"", ""isNullable"": true }
    }
  }"));

        var db = TryGetSource(result, "JauntyDb.g.cs")!;
        Assert.DoesNotContain("SELECT public.select(", db);

        AssertCompiles(compilation);
    }

    [Fact]
    public void NoFunctions_EmitsNoAccessorAtAll()
    {
        // Back-compat: every snapshot written before spec 014 has no functions
        // key at all, and must generate byte-identically to before.
        var (result, compilation) = Run(Schema("sqlserver", "{}"));

        Assert.DoesNotContain("FunctionAccessor", TryGetSource(result, "JauntyDb.g.cs")!);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "JNT2023" or "JNT2024" or "JNT2025");

        AssertCompiles(compilation);
    }

    [Fact]
    public void AddingAFunctionChangesJauntyDbAndNothingElse()
    {
        // FR-008 stated as something a mutant can break.
        // NoFunctionsEmitsNoAccessorAtAll asserts the accessor is absent, which
        // says nothing about the files spec 014 was not supposed to touch.
        // Comparing a pre-014 snapshot against the same one with an explicit
        // empty "functions": {} would say even less -- the two deserialize to
        // the same object, so the comparison could not fail.
        //
        // The claim with content is that adding a function to a snapshot is
        // INERT everywhere except JauntyDb.g.cs. The row types, the operation
        // surface and the shape guard must come out identical, so a change that
        // leaked into materialization or auto-CRUD is caught here.
        string legacy = @"{
  ""dialect"": ""sqlserver""," + TablesAndKeys + @"
  ""procedures"": {}
}";

        var without = SourcesByName(Run(legacy).result);
        var with = SourcesByName(Run(Schema("sqlserver", CalcTax)).result);

        Assert.Equal(without.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     with.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var name in without.Keys)
        {
            if (name.EndsWith("JauntyDb.g.cs", StringComparison.Ordinal))
                continue;
            Assert.Equal(without[name], with[name]);
        }

        Assert.NotEqual(without["JauntyDb.g.cs"], with["JauntyDb.g.cs"]);
    }

    private static Dictionary<string, string> SourcesByName(GeneratorDriverRunResult result)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tree in result.GeneratedTrees)
            map[System.IO.Path.GetFileName(tree.FilePath)] = tree.ToString();
        return map;
    }
}
