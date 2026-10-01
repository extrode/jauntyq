using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class ProcessFileMutationCoverageTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""score"": { ""name"": ""score"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ix_widgets_name"", ""columns"": [""name""], ""isUnique"": false }
      ]
    },
    ""odd"": {
      ""name"": ""odd"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        """": { ""name"": """", ""dbType"": ""int"", ""isNullable"": false }
      }
    }
  },
  ""procedures"": {
    ""ListWidgets"": {
      ""name"": ""ListWidgets"",
      ""params"": [ { ""name"": ""min_score"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false } ],
      ""results"": [ { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false } ]
    },
    ""BadResult"": {
      ""name"": ""BadResult"",
      ""params"": [],
      ""results"": [ { ""name"": """", ""dbType"": ""int"", ""isNullable"": false } ]
    },
    ""DupResult"": {
      ""name"": ""DupResult"",
      ""params"": [],
      ""results"": [
        { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        { ""name"": ""OrderId"", ""dbType"": ""int"", ""isNullable"": false }
      ]
    },
    ""DupParam"": {
      ""name"": ""DupParam"",
      ""params"": [
        { ""name"": ""order_number"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false },
        { ""name"": ""OrderNumber"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }
      ],
      ""results"": []
    },
    ""ThreeReturns"": {
      ""name"": ""ThreeReturns"",
      ""params"": [
        { ""name"": ""r1"", ""dbType"": ""int"", ""direction"": ""ReturnValue"", ""isNullable"": false },
        { ""name"": ""r2"", ""dbType"": ""int"", ""direction"": ""ReturnValue"", ""isNullable"": false },
        { ""name"": ""r3"", ""dbType"": ""int"", ""direction"": ""ReturnValue"", ""isNullable"": false }
      ],
      ""results"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(bool autoCrud, string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("ProcessFileTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToList();
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files) => Run(false, SchemaJson, files);

    private static string Message(GeneratorDriverRunResult result, string id)
    {
        var matches = result.Diagnostics.Where(d => d.Id == id).ToList();
        Assert.True(matches.Count == 1,
            string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.GetMessage())));
        return matches[0].GetMessage();
    }

    private static string? Source(GeneratorDriverRunResult result, string suffix) =>
        result.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith(suffix))?.ToString();

    [Fact]
    public void AWhitespaceOnlyFile_DoesNotClaimTheSyntheticSlot()
    {
        var result = Run(true, SchemaJson, ("db/Widgets/GetAll.sql", "  \n "));

        Assert.NotNull(Source(result, "Widgets.GetAll.auto.g.cs"));
    }

    [Theory]
    [InlineData("db/Widgets/Get-All.sql", "Widgets", "Get-All")]
    [InlineData("db/Wid-gets/GetAll.sql", "Wid-gets", "GetAll")]
    public void AnIllegalPathName_ReportsJNT2004(string path, string entity, string method)
        => Assert.Equal(
            $"SQL file path yields an illegal C# name (entity '{entity}', method '{method}'). Rename the file/folder to a valid identifier (letters, digits, underscore; not starting with a digit).",
            Message(Run((path, "select id from widgets")), "JNT2004"));

    [Theory]
    [InlineData("db/Widgets/class.sql", "class")]
    [InlineData("db/int/GetAll.sql", "int")]
    public void AKeywordPathName_NamesTheKeyword(string path, string offender)
        => Assert.Equal(
            $"SQL file path yields the C# reserved keyword '{offender}' as an entity/method name, which cannot be emitted as-is. Rename the file/folder (e.g. '{offender}Query').",
            Message(Run((path, "select id from widgets")), "JNT2004"));

    [Theory]
    [InlineData("-- @first", "@first")]
    [InlineData("-- @identity", "@identity")]
    [InlineData("-- @stream", "@stream")]
    [InlineData("-- @each ids", "@each")]
    [InlineData("-- @type v int", "@type")]
    [InlineData("-- @params id:int", "@params")]
    [InlineData("-- @result Foo", "@result")]
    [InlineData("-- @result void", "@result")]
    [InlineData("-- @result (int Id)", "@result")]
    public void CallWithAnotherDirective_NamesTheConflict(string directive, string name)
        => Assert.Equal(
            $"-- @call cannot be combined with -- {name}: @call has no SQL body of its own -- its parameter and result shape come entirely from the schema snapshot's procedure signature, so -- {name} would be silently ignored. Remove one directive.",
            Message(Run(("db/Procs/List.sql", $"-- @call ListWidgets\n{directive}\n")), "JNT3003"));

    [Fact]
    public void CallWithAConflict_StillReportsDirectiveTypos()
        => Assert.Contains(
            Run(("db/Procs/List.sql", "-- @call ListWidgets\n-- @frist\n-- @stream\n")).Diagnostics,
            d => d.Id == "JNT3008");

    [Fact]
    public void CallWithoutAConflict_StillReportsDirectiveTypos()
        => Assert.Contains(
            Run(("db/Procs/List.sql", "-- @call ListWidgets\n-- @frist\n")).Diagnostics,
            d => d.Id == "JNT3008");

    [Fact]
    public void CallToAMissingProcedure_JNT2005Message()
        => Assert.Equal(
            "Stored procedure 'Nope' was not found in the schema snapshot. Re-run 'jaunty schema pull' after the procedure exists, or check the name.",
            Message(Run(("db/Procs/List.sql", "-- @call Nope\n")), "JNT2005"));

    [Fact]
    public void CallWithAnIllegalResultColumn_JNT2004Message()
        => Assert.Equal(
            "Stored procedure 'BadResult' returns a column '' that maps to an illegal C# identifier.",
            Message(Run(("db/Procs/Bad.sql", "-- @call BadResult\n")), "JNT2004"));

    [Fact]
    public void CallWithFoldingResultColumns_JNT2011Message()
        => Assert.Equal(
            "Stored procedure 'DupResult' has two result columns that map to the same generated property 'OrderId'. "
            + "The Result DTO cannot declare 'OrderId' twice; alias one column distinctly in the procedure's own SELECT.",
            Message(Run(("db/Procs/Dup.sql", "-- @call DupResult\n")), "JNT2011"));

    [Fact]
    public void CallWithFoldingParameters_JNT2013Message()
        => Assert.Equal(
            "Stored procedure 'DupParam' has two parameters, 'order_number' and 'OrderNumber', that map to the same generated parameter 'orderNumber'. "
            + "The generated method cannot declare 'orderNumber' twice; rename one of them in the procedure definition.",
            Message(Run(("db/Procs/Dup.sql", "-- @call DupParam\n")), "JNT2013"));

    [Fact]
    public void CallWithThreeReturnValues_NamesTheFirstTwo()
        => Assert.Equal(
            "Stored procedure 'ThreeReturns' declares 3 parameters with direction 'ReturnValue' ('r1', 'r2', ...). "
            + "A procedure has exactly one return status, so at most one of them could ever carry a value and nothing defines which. "
            + "Keep one and give the others direction 'Out'.",
            Message(Run(("db/Procs/Three.sql", "-- @call ThreeReturns\n")), "JNT2026"));

    [Fact]
    public void ACallFile_RegistersItsEntityAndFingerprint()
    {
        var result = Run(
            ("db/Procs/List.sql", "-- @call ListWidgets\n"),
            ("db/Procs/Again.sql", "-- @call ListWidgets\n"));

        Assert.Contains("Procs", Source(result, "JauntyDb.g.cs"));
        Assert.Equal(
            "Queries Procs.Again, Procs.List compile to identical SQL; consolidate them to keep one plan and one maintenance point.",
            Message(result, "JNT8005"));
    }

    [Fact]
    public void TooLargeInput_JNT1003Message()
    {
        string sql = "select id from widgets -- " + new string('x', SqlParser.SqlTokenizer.MaxInputLength);

        Assert.Equal(
            $"SQL text is {sql.Length} characters, exceeding the {SqlParser.SqlTokenizer.MaxInputLength}-character limit; refusing to tokenize.",
            Message(Run(("db/Widgets/Big.sql", sql)), "JNT1003"));
    }

    [Fact]
    public void TooDeepInput_JNT1005Message()
    {
        int depth = SqlParser.SqlTokenizer.MaxNestingDepth + 1;
        string sql = "select " + new string('(', depth) + "1" + new string(')', depth) + " as v";

        Assert.Equal(
            $"SQL nests parentheses more than {SqlParser.SqlTokenizer.MaxNestingDepth} levels deep; refusing to parse. Simplify the query — nesting this deep is almost always a generated-SQL or copy-paste error.",
            Message(Run(("db/Widgets/Deep.sql", sql)), "JNT1005"));
    }

    [Fact]
    public void AnUnterminatedFirstToken_JNT1002Message()
        => Assert.Equal(
            "Unterminated ' ... ': reached end of file before finding its closing delimiter.",
            Message(Run(("db/Widgets/Open.sql", "'abc")), "JNT1002"));

    [Fact]
    public void AnUnterminatedLiteralWithABackslashQuote_AddsTheMySqlHint()
        => Assert.Equal(
            "Unterminated ' ... ': reached end of file before finding its closing delimiter."
            + @" This file contains \', which is MySQL's backslash escape — JauntyQ's tokenizer"
            + " implements only the ANSI '' (doubled-quote) escape, so if this is MySQL SQL the"
            + @" literal is not unterminated, it is unreadable. Write '' in place of \'.",
            Message(Run(("db/Widgets/Open.sql", @"select id from widgets where name = 'it\'s'")), "JNT1002"));

    [Fact]
    public void AnUnknownFirstCharacter_ReportsJNT1004()
        => Assert.Contains(Run(("db/Widgets/Odd.sql", "€select id from widgets")).Diagnostics, d => d.Id == "JNT1004");

    [Fact]
    public void AllowUnindexedOnAnIndexedFilter_JNT8012Message()
        => Assert.Equal(
            "-- @allow-unindexed is declared but no filter column on this query is unindexed, so it "
            + "suppresses nothing. The index it was waiting for probably exists now: remove the directive. "
            + "(Stated reason: legacy)",
            Message(Run(("db/Widgets/ByName.sql", "-- @allow-unindexed legacy\nselect id from widgets where name = @name")), "JNT8012"));

    [Fact]
    public void AllowSortOnAnIndexedOrder_JNT8012Message()
        => Assert.Equal(
            "-- @allow-sort is declared but no ORDER BY column on this query is unindexed, so it "
            + "suppresses nothing. The index it was waiting for probably exists now: remove the directive. "
            + "(Stated reason: small)",
            Message(Run(("db/Widgets/Sorted.sql", "-- @allow-sort small\nselect id from widgets order by name")), "JNT8012"));

    [Fact]
    public void IdentityWithReturning_JNT7001Message()
        => Assert.Equal(
            "-- @identity cannot be combined with a user-written RETURNING clause; use one or the other",
            Message(Run(false, SchemaJson.Replace("sqlserver", "postgres"),
                ("db/Widgets/Add.sql", "-- @identity\ninsert into widgets (name, score) values (@name, @score) returning id")), "JNT7001"));

    [Fact]
    public void IdentityWithoutADialect_JNT7001Message()
        => Assert.Equal(
            "-- @identity requires a dialect in the schema snapshot; re-run 'jaunty schema pull' with the current CLI",
            Message(Run(false, SchemaJson.Replace(@"""dialect"": ""sqlserver"",", ""),
                ("db/Widgets/Add.sql", "-- @identity\ninsert into widgets (name, score) values (@name, @score)")), "JNT7001"));

    [Theory]
    [InlineData("-- @stream\ninsert into widgets (name, score) values (@name, @score)", "-- @stream is only valid on SELECT queries")]
    [InlineData("-- @stream\n-- @first\nselect id from widgets", "-- @stream cannot be combined with -- @first (streaming is for multi-row results)")]
    [InlineData("-- @each ids\ninsert into widgets (name, score) values (@name, @score)",
        "-- @each is only supported on SELECT queries; runtime IN-list expansion is not implemented for INSERT/UPDATE/DELETE")]
    [InlineData("-- @each ids\n-- @proc\nselect id from widgets where id in (@ids)",
        "-- @each cannot be combined with -- @proc (a stored procedure call has a fixed parameter list; IN-list expansion rewrites the SQL text)")]
    [InlineData("-- @each a\n-- @each b\nselect id from widgets",
        "-- @each names unknown parameter 'a': the query has no @a parameter. Check the spelling; left as-is the directive would be silently ignored.")]
    [InlineData("-- @first\nupdate widgets set score = @score",
        "-- @first is only valid on a SELECT or on a statement with a RETURNING clause: a plain UPDATE produces no rows for it to reduce, so the directive would be silently ignored. Remove it, or add RETURNING.")]
    public void DirectivePreconditions_JNT3003Message(string sql, string expected)
        => Assert.Equal(expected, Message(Run(("db/Widgets/Q.sql", sql)), "JNT3003"));

    [Fact]
    public void AnIllegalProcName_JNT2004Message()
        => Assert.Equal(
            "-- @proc name '1bad' is not a valid C# identifier. Use letters, digits, and underscores, not starting with a digit.",
            Message(Run(("db/Widgets/Q.sql", "-- @proc 1bad\nselect id from widgets")), "JNT2004"));

    [Fact]
    public void AnIllegalCrudParameter_ReportsJNT2004()
        => Assert.StartsWith(
            "Parameter '@__x' in Widgets.Q uses the reserved '__' prefix.",
            Message(Run(("db/Widgets/Q.sql", "update widgets set score = @__x")), "JNT2012"));

    [Fact]
    public void AnUntypedReturningExpression_ReportsJNT3005()
        => Assert.Equal(
            "Expression 'v' has no inferable type. Declare it with: -- @type v <dbtype>.",
            Message(Run(false, SchemaJson.Replace("sqlserver", "postgres"),
                ("db/Widgets/Q.sql", "insert into widgets (name, score) values (@name, @score) returning mystery(id) as v")), "JNT3005"));

    [Fact]
    public void AnUnnamedReturningColumn_JNT2004Message()
        => Assert.Equal(
            "RETURNING column or alias maps to an illegal C# identifier ''. Use a valid identifier.",
            Message(Run(false, SchemaJson.Replace("sqlserver", "postgres"),
                ("db/Odd/Q.sql", "delete from odd where id = @id returning *")), "JNT2004"));

    [Fact]
    public void AnUnnamedSelectColumn_JNT2004Message()
        => Assert.Equal(
            "Column or alias maps to an illegal C# identifier ''. Use a SQL alias that is a valid identifier (letters, digits, underscore; not starting with a digit).",
            Message(Run(false, SchemaJson.Replace("sqlserver", "postgres"), ("db/Odd/Q.sql", "select \"\" from odd")), "JNT2004"));

    [Fact]
    public void FoldingResultColumns_JNT3009Message()
        => Assert.Equal(
            "Two result columns map to the same generated property 'MyCol'. Give each SELECT/RETURNING item a distinct alias (AS) — the generated row type cannot declare 'MyCol' twice.",
            Message(Run(("db/Widgets/Q.sql", "select id as my_col, score as myCol from widgets")), "JNT3009"));
}
