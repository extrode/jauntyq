using System.Text.Json;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class SchemaModelMutationCoverageTests
{
    public static TheoryData<string, Func<string>> EmptyStringDefaults => new()
    {
        { "ColumnSchema.Name", () => new ColumnSchema().Name },
        { "ColumnSchema.DbType", () => new ColumnSchema().DbType },
        { "DatabaseSchema.Dialect", () => new DatabaseSchema().Dialect },
        { "EnumSchema.Name", () => new EnumSchema().Name },
        { "EnumMember.Value", () => new EnumMember().Value },
        { "EnumMember.CSharpName", () => new EnumMember().CSharpName },
        { "ForeignKeySchema.FromTable", () => new ForeignKeySchema().FromTable },
        { "ForeignKeySchema.FromColumn", () => new ForeignKeySchema().FromColumn },
        { "ForeignKeySchema.ToTable", () => new ForeignKeySchema().ToTable },
        { "ForeignKeySchema.ToColumn", () => new ForeignKeySchema().ToColumn },
        { "FunctionSchema.Name", () => new FunctionSchema().Name },
        { "FunctionParam.Name", () => new FunctionParam().Name },
        { "FunctionParam.DbType", () => new FunctionParam().DbType },
        { "FunctionReturn.DbType", () => new FunctionReturn().DbType },
        { "ProcedureSchema.Name", () => new ProcedureSchema().Name },
        { "ProcedureParam.Name", () => new ProcedureParam().Name },
        { "ProcedureParam.DbType", () => new ProcedureParam().DbType },
        { "SequenceSchema.Name", () => new SequenceSchema().Name },
        { "TableSchema.Name", () => new TableSchema().Name },
        { "IndexSchema.Name", () => new IndexSchema().Name },
        { "UserTypeSchema.Name", () => new UserTypeSchema().Name },
    };

    [Theory]
    [MemberData(nameof(EmptyStringDefaults))]
    public void StringMembers_DefaultToEmpty(string member, Func<string> read) =>
        Assert.True(read() == string.Empty, $"{member} default was '{read()}'");

    [Fact]
    public void UserType_DefaultsToNullable() =>
        Assert.True(new UserTypeSchema().IsNullable);

    [Fact]
    public void ASnapshotOfLiteralNull_NamesTheSnapshotInItsError()
    {
        var ex = Assert.Throws<JsonException>(() => SchemaLoader.Load("null"));
        Assert.Equal("schema snapshot JSON was the literal 'null', not a snapshot object", ex.Message);
    }

    [Fact]
    public void AnAcceptanceFileOfLiteralNull_NamesTheAcceptanceFileInItsError()
    {
        var ex = Assert.Throws<JsonException>(() => AcceptanceLoader.Load("null"));
        Assert.Equal("acceptance file JSON was the literal 'null', not an acceptance object", ex.Message);
    }

    [Theory]
    [InlineData("z", "Z")]
    [InlineData("A", "A")]
    [InlineData("a0", "A0")]
    [InlineData("a9", "A9")]
    [InlineData("0", "_0")]
    [InlineData("9", "_9")]
    public void Fold_TreatsEveryRangeBoundaryCharacterAsInRange(string value, string expected) =>
        Assert.Equal(expected, EnumMemberNaming.Fold(value));
}
