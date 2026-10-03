using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class NPlusOneParameterScopeTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""realms"": { ""name"": ""realms"", ""columns"": {
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 } },
      ""indexes"": [] },
    ""memberships"": { ""name"": ""memberships"", ""columns"": {
        ""membership_id"": { ""name"": ""membership_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""email"": { ""name"": ""email"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 } },
      ""indexes"": [] }
  },
  ""foreignKeys"": [ { ""fromTable"": ""memberships"", ""fromColumn"": ""realm_id"", ""toTable"": ""realms"", ""toColumn"": ""realm_id"" } ]
}";

    [Theory]
    [InlineData("select email\nfrom memberships\nwhere realm_id = @realm_id", true)]
    [InlineData("select email\nfrom memberships\nwhere exists (select 1 from realms where realm_id = @realm_id)", false)]
    [InlineData("select email\nfrom memberships\nwhere exists (select 1 from realms r where r.realm_id = @realm_id)", false)]
    public void OnlyAnOuterForeignKeyFilter_MakesAChildLookup(string childSql, bool fires)
    {
        Assert.Equal(fires, Fires(SchemaJson, childSql));
    }

    [Fact]
    public void AnOuterForeignKeyFilterWrittenInsideASubquery_MakesAChildLookup()
    {
        string schema = SchemaJson.Replace(@"""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true }",
            @"""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true }")
            .Replace(@"""toColumn"": ""realm_id""", @"""toColumn"": ""id""");

        Assert.True(Fires(schema, "select email\nfrom memberships\nwhere exists (select 1 from realms where realm_id = @realm_id and name = 'x')", "select id, name\nfrom realms"));
    }

    private static bool Fires(string schema, string childSql, string parentSql = "select realm_id, name\nfrom realms")
    {
        var compilation = CSharpCompilation.Create("NPlusOneParameterScopeAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var texts = new List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", schema),
            new InMemoryAdditionalText("db/Realms/GetAll.sql", parentSql),
            new InMemoryAdditionalText("db/Memberships/GetByRealm.sql", childSql),
        };

        var result = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false))
            .RunGenerators(compilation).GetRunResult();

        return result.Diagnostics.Any(d => d.Id == "JNT8008");
    }
}
