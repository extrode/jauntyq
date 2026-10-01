using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class EnumEmissionEdgeTests
{
    private static DatabaseSchema Schema(params EnumMember[] members)
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        var e = new EnumSchema { Name = "mood" };
        e.Members.AddRange(members);
        schema.Enums["mood"] = e;
        var table = new TableSchema { Name = "people" };
        table.Columns["mood"] = new ColumnSchema { Name = "mood", DbType = "mood", EnumName = "mood" };
        schema.Tables["people"] = table;
        return schema;
    }

    [Fact]
    public void ReferencedEnumWithoutMembers_IsNotEmitted()
    {
        Assert.Empty(CodeEmitter.ReferencedEnumNames(Schema()));
        Assert.Null(CodeEmitter.EmitEnums(Schema()));
    }

    [Fact]
    public void WireValue_IsXmlEscapedInTheDocComment()
    {
        string code = CodeEmitter.EmitEnums(Schema(new EnumMember { Value = "a&b<c>", CSharpName = "Odd" }))!;

        Assert.Contains("/// <summary>Wire value <c>a&amp;b&lt;c&gt;</c>.</summary>", code);
    }

    [Fact]
    public void NullWireValue_DocumentsAnEmptyValue()
    {
        string code = CodeEmitter.EmitEnums(Schema(new EnumMember { Value = null!, CSharpName = "Blank" }))!;

        Assert.Contains("/// <summary>Wire value <c></c>.</summary>", code);
    }

    [Fact]
    public void MemberName_FallsBackToTheFoldedValueOnlyWhenNoCSharpNameIsGiven()
    {
        string code = CodeEmitter.EmitEnums(Schema(
            new EnumMember { Value = "very happy" },
            new EnumMember { Value = "sad", CSharpName = "Gloomy" }))!;

        Assert.Contains("        VeryHappy,", code);
        Assert.Contains("        Gloomy,", code);
        Assert.DoesNotContain("Sad", code);
    }

    [Fact]
    public void IsEnumParameterType_RecognisesOnlyGeneratedEnums()
    {
        var schema = Schema(new EnumMember { Value = "ok", CSharpName = "Ok" });

        Assert.True(CodeEmitter.IsEnumParameterType("Mood", schema));
        Assert.False(CodeEmitter.IsEnumParameterType("string", schema));
    }
}
