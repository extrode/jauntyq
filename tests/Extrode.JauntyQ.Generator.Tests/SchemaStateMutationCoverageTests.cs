using System;
using System.Collections.Immutable;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class SchemaStateMutationCoverageTests
{
    private static readonly ImmutableArray<(string Name, string Text)> None =
        ImmutableArray<(string Name, string Text)>.Empty;

    private static ImmutableArray<(string Name, string Text)> Files(params (string Name, string Text)[] files) =>
        ImmutableArray.Create(files);

    [Fact]
    public void Failed_CarriesTheMessageAndNoSchema()
    {
        var state = SchemaState.Failed("boom");

        Assert.Null(state.Schema);
        Assert.False(state.ParseFailed);
        Assert.Equal("boom", state.InternalError);
    }

    [Fact]
    public void Load_MalformedJson_IsParseFailed()
    {
        var state = SchemaState.Load("{ not json", None, None, null);

        Assert.Null(state.Schema);
        Assert.True(state.ParseFailed);
    }

    [Fact]
    public void Load_NothingAtAll_HasNoSchemaAndDidNotFail()
    {
        var state = SchemaState.Load(null, None, None, null);

        Assert.Null(state.Schema);
        Assert.False(state.ParseFailed);
        Assert.Null(state.InternalError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oracle")]
    public void Load_DdlWithoutAValidDialect_ReportsJNT9003(string? dialect)
    {
        var state = SchemaState.Load(null, None, Files(("a.sql", "create table t (id int)")), dialect);

        Assert.Null(state.Schema);
        Assert.False(state.ParseFailed);
        var diagnostic = Assert.Single(state.MigrationDiagnostics).ToDiagnostic();
        Assert.Equal("JNT9003", diagnostic.Id);
        Assert.Equal(
            "db/ddl/*.sql schema source found but no valid dialect is set. With no JSON snapshot there is " +
            "nothing to infer the dialect from; declare it in the consuming project's .csproj, e.g. " +
            "<JauntyQDialect>sqlserver</JauntyQDialect> (or postgres, mysql, sqlite).",
            diagnostic.GetMessage());
    }

    [Fact]
    public void Load_DdlOnly_BuildsTheSchemaInTheDeclaredDialectWithNoDelta()
    {
        var state = SchemaState.Load(null, default, Files(("a.sql", "create table t (id int)")), "sqlite");

        Assert.NotNull(state.Schema);
        Assert.Equal("sqlite", state.Schema!.Dialect);
        Assert.Contains("t", state.Schema.Tables.Keys);
        Assert.False(state.ParseFailed);
        Assert.Null(state.MigrationDelta);
    }

    [Fact]
    public void Load_DdlThenMigrations_AppliesThemInNaturalOrderAndRecordsTheDelta()
    {
        var state = SchemaState.Load(null,
            Files(("V10__drop.sql", "alter table t drop column extra"), ("V2__add.sql", "alter table t add extra int")),
            Files(("a.sql", "create table t (id int)")),
            "sqlite");

        Assert.Empty(state.MigrationDiagnostics);
        Assert.NotNull(state.MigrationDelta);
        Assert.DoesNotContain("extra", state.Schema!.Tables["t"].Columns.Keys);
    }

    [Fact]
    public void Load_AnInvalidMigration_ReportsJNT9002()
    {
        var state = SchemaState.Load(null,
            Files(("V1__bad.sql", "alter table missing add c int")),
            Files(("a.sql", "create table t (id int)")),
            "sqlite");

        Assert.Equal("JNT9002", Assert.Single(state.MigrationDiagnostics).ToDiagnostic().Id);
    }
    private const string SnapshotJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""t"": {
      ""name"": ""t"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true }
      }
    }
  }
}";

    [Fact]
    public void Load_JsonWithoutMigrations_LoadsTheSnapshotWithNoDelta()
    {
        var state = SchemaState.Load(SnapshotJson, None, None, null);

        Assert.NotNull(state.Schema);
        Assert.False(state.ParseFailed);
        Assert.Null(state.MigrationDelta);
    }

    [Fact]
    public void Load_JsonWithMigrations_AppliesThem()
    {
        var state = SchemaState.Load(SnapshotJson, Files(("V1__add.sql", "alter table t add extra int")), None, null);

        Assert.Contains("extra", state.Schema!.Tables["t"].Columns.Keys);
        Assert.False(state.ParseFailed);
        Assert.NotNull(state.MigrationDelta);
    }
}
