using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Analysis;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 T4: a view is captured so it can be READ. Reads generate exactly as
/// they do for a table; writes are refused rather than skipped (JNT2021), and
/// auto-CRUD synthesizes no write for one.
///
/// Refused rather than skipped, and an Error rather than a Warning, because the
/// consumer wrote the INSERT by hand and means it — unlike JNT2014/JNT2015,
/// which report methods the generator declined to invent and nobody asked for.
/// </summary>
public class ViewWriteRefusalTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""inbound_messages"": {
      ""name"": ""inbound_messages"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""subject"": { ""name"": ""subject"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""ix_inbound_user"", ""columns"": [""user_id""], ""isUnique"": false }
      ]
    },
    ""v_message_outcomes"": {
      ""name"": ""v_message_outcomes"",
      ""isView"": true,
      ""columns"": {
        ""message_id"": { ""name"": ""message_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""outcome"": { ""name"": ""outcome"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 20 }
      },
      ""indexes"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, bool autoCrud = false)
    {
        var compilation = CSharpCompilation.Create("ViewWriteTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Outcomes/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // ── Reads: a view behaves exactly like a table ─────────────────────────

    [Fact]
    public void SelectFromView_IsAccepted_WithNoSchemaDiagnostic()
    {
        var result = Run("select v_message_outcomes.message_id, v_message_outcomes.outcome\n" +
                         "from v_message_outcomes");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2021");
    }

    [Fact]
    public void JoinToView_IsAccepted()
    {
        var result = Run(
            "select inbound_messages.id, v_message_outcomes.outcome\n" +
            "from inbound_messages\n" +
            "join v_message_outcomes on v_message_outcomes.message_id = inbound_messages.id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2021");
    }

    // ── Writes: refused, by verb ───────────────────────────────────────────

    [Theory]
    [InlineData("insert into v_message_outcomes (message_id, outcome) values (@id, @outcome)")]
    [InlineData("update v_message_outcomes set outcome = @outcome where message_id = @id")]
    [InlineData("delete from v_message_outcomes where message_id = @id")]
    public void WriteToView_IsRefused_WithJNT2021(string sql)
    {
        var result = Run(sql);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2021");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("v_message_outcomes", diag.GetMessage());
    }

    [Fact]
    public void WriteToBaseTable_IsNotRefused()
    {
        var result = Run("insert into inbound_messages (user_id, subject) values (@userId, @subject)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2021");
    }

    /// <summary>
    /// Only the relation being WRITTEN matters. An UPDATE against a real table
    /// whose predicate reads a view is legitimate, and firing on it would make
    /// the diagnostic useless for the shape views exist to enable.
    /// </summary>
    [Fact]
    public void UpdateTableFilteredByView_IsNotRefused()
    {
        var result = Run(
            "update inbound_messages set subject = @subject\n" +
            "where inbound_messages.id in (select v_message_outcomes.message_id from v_message_outcomes)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2021");
    }

    // ── Auto-CRUD: reads yes, writes never ─────────────────────────────────

    [Fact]
    public void AutoCrud_SynthesizesNoWriteForAView_EvenIfItsColumnsClaimAPrimaryKey()
    {
        // A hand-edited snapshot can mark a view column isPrimaryKey. The pkCols
        // gate alone would then let every write through, which is why the guard
        // is on IsView rather than on what the columns happen to say.
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["v_outcomes"] = new TableSchema
        {
            Name = "v_outcomes",
            IsView = true,
            Columns =
            {
                ["message_id"] = new ColumnSchema { Name = "message_id", DbType = "int", IsPrimaryKey = true },
                ["outcome"] = new ColumnSchema { Name = "outcome", DbType = "varchar" },
            },
        };

        var synthesized = AutoCrud.Synthesize(schema);

        Assert.DoesNotContain(synthesized, q =>
            q.MethodName is "Insert" or "Update" or "Delete" or "Upsert" or "BulkInsert");
    }

    [Fact]
    public void AutoCrud_StillSynthesizesReadsForAView()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["v_outcomes"] = new TableSchema
        {
            Name = "v_outcomes",
            IsView = true,
            Columns =
            {
                ["message_id"] = new ColumnSchema { Name = "message_id", DbType = "int" },
                ["outcome"] = new ColumnSchema { Name = "outcome", DbType = "varchar" },
            },
        };

        var synthesized = AutoCrud.Synthesize(schema);

        Assert.Contains(synthesized, q => q.MethodName == "GetAll");
    }

    [Fact]
    public void AutoCrud_StillSynthesizesWritesForABaseTable()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["inbound_messages"] = new TableSchema
        {
            Name = "inbound_messages",
            Columns =
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["subject"] = new ColumnSchema { Name = "subject", DbType = "varchar" },
            },
        };

        var synthesized = AutoCrud.Synthesize(schema);

        Assert.Contains(synthesized, q => q.MethodName == "Insert");
    }
}
