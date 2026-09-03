using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// The JNT3003 gate on -- @first. @stream and @each are both gated to SELECT
/// because a directive that cannot be applied must be reported rather than
/// dropped; @first was not, so a plain INSERT/UPDATE/DELETE silently ignored
/// it. It IS honored on a CRUD statement carrying RETURNING, which is why the
/// gate reads "non-SELECT and no RETURNING" rather than simply "non-SELECT".
/// </summary>
public class FirstDirectiveGateTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""text"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string path = "db/Products/FirstQuery.sql")
    {
        var compilation = CSharpCompilation.Create("FirstGateTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static bool HasFirstGate(GeneratorDriverRunResult result) =>
        result.Results[0].Diagnostics.Any(
            d => d.Id == "JNT3003" && d.GetMessage().Contains("-- @first"));

    [Fact]
    public void First_OnPlainInsert_ReportsJNT3003()
    {
        string sql = "-- @first\ninsert into products (product_name) values (@productName)";
        Assert.True(HasFirstGate(Run(sql)));
    }

    [Fact]
    public void First_OnPlainUpdate_ReportsJNT3003()
    {
        string sql = "-- @first\nupdate products set product_name = @productName where product_id = @productId";
        Assert.True(HasFirstGate(Run(sql, "db/Products/FirstUpdate.sql")));
    }

    [Fact]
    public void First_OnPlainDelete_ReportsJNT3003()
    {
        string sql = "-- @first\ndelete from products where product_id = @productId";
        Assert.True(HasFirstGate(Run(sql, "db/Products/FirstDelete.sql")));
    }

    // ── False-positive guards: the two shapes where @first is meaningful ──

    [Fact]
    public void First_OnInsertWithReturning_IsClean()
    {
        // RETURNING gives the statement a result shape, so @first reduces it to
        // one row exactly as it does on a SELECT. This is the arm that stops
        // the gate from being a plain "non-SELECT" test.
        string sql = "-- @first\ninsert into products (product_name) values (@productName) returning product_id, product_name";
        var result = Run(sql, "db/Products/FirstInsertReturning.sql");

        Assert.False(HasFirstGate(result));
    }

    [Fact]
    public void First_OnSelect_IsClean()
    {
        string sql = "-- @first\nselect product_id, product_name from products where product_id = @productId";
        var result = Run(sql, "db/Products/FirstSelect.sql");

        Assert.False(HasFirstGate(result));
    }

    [Fact]
    public void PlainInsert_WithoutFirst_IsClean()
    {
        string sql = "insert into products (product_name) values (@productName)";
        var result = Run(sql, "db/Products/PlainInsert.sql");

        Assert.False(HasFirstGate(result));
    }
}
