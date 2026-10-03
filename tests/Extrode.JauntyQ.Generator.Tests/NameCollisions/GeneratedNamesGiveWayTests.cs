using Xunit;
using static Extrode.JauntyQ.Generator.Tests.NameCollisions.NameCollisionHarness;

namespace Extrode.JauntyQ.Generator.Tests.NameCollisions;

public class GeneratedNamesGiveWayTests
{
    [Theory]
    [InlineData("column", "read", "postgres")]
    [InlineData("column", "read", "sqlite")]
    [InlineData("pk", "Read", "sqlite")]
    [InlineData("scope", "read", "postgres")]
    [InlineData("column", "await", "sqlite")]
    [InlineData("pk", "await", "mysql")]
    [InlineData("scope", "await", "postgres")]
    [InlineData("qparam", "await", "sqlite")]
    [InlineData("procparam", "await", "postgres")]
    [InlineData("qparam", "Order", "sqlite")]
    [InlineData("qparam", "Order", "postgres")]
    [InlineData("qparam", "DBNull", "sqlite")]
    [InlineData("qparam", "DBNull", "postgres")]
    [InlineData("qparam", "ConnectionState", "mysql")]
    [InlineData("qparam", "CommandBehavior", "sqlite")]
    [InlineData("qparam", "JauntyQShapeGuard", "sqlite")]
    [InlineData("qparam", "Volatile", "sqlite")]
    [InlineData("qparam", "DbType", "sqlite")]
    [InlineData("column", "DBNull", "sqlite")]
    [InlineData("column", "ConnectionState", "mysql")]
    [InlineData("pk", "CommandBehavior", "sqlite")]
    [InlineData("scope", "InsertAsync", "sqlite")]
    [InlineData("scope", "InsertAsync", "mysql")]
    [InlineData("scope", "Upsert", "sqlite")]
    [InlineData("table", "date_time", "sqlite")]
    [InlineData("table", "guid", "mysql")]
    public void AName_TheGeneratedCodeAlsoUses_StillCompiles(string site, string name, string dialect)
    {
        Assert.Empty(CompileErrors(site, name, dialect));
    }

    [Fact]
    public void AnIdentityInsert_WithAColumnNamedCommandBehavior_QualifiesTheTypeAndCompiles()
    {
        var result = GenerateIdentityTable("CommandBehavior", "sqlite");

        Assert.Empty(CompileErrors(result));
        Assert.Contains("global::System.Data.CommandBehavior.SingleRow", SourceOf(result, "Orders.Insert.auto.g.cs"));
    }

    [Fact]
    public void AParameterNamedLikeAType_GetsTheTypeQualified()
    {
        string find = Source("qparam", "ConnectionState", "sqlite", "Orders.Find.g.cs");

        Assert.Contains("global::System.Data.ConnectionState.Open", find);
        Assert.Contains("CommandBehavior.SingleResult", find);
        Assert.DoesNotContain("global::System.Data.CommandBehavior", find);
    }

    [Fact]
    public void AParameterNamedLikeTheRowType_GetsTheRowTypeQualified()
    {
        string find = Source("qparam", "Order", "sqlite", "Orders.Find.g.cs");

        Assert.Contains("global::Extrode.JauntyQ.Generated.Order.Read(__reader)", find);
    }

    [Fact]
    public void ParametersWithOrdinaryNames_KeepTheShortTypeNames()
    {
        string find = Source("qparam", "note", "sqlite", "Orders.Find.g.cs");

        Assert.Contains(" != ConnectionState.Open", find);
        Assert.Contains("?? DBNull.Value", find);
        Assert.Contains("JauntyQShapeGuard.Validate(", find);
        Assert.Contains("Order.Read(__reader)", find);
        Assert.DoesNotContain("global::", find);
    }

    [Fact]
    public void AColumnNamedRead_RenamesTheRowMaterializer()
    {
        var result = Generate("column", "read", "sqlite");
        string row = SourceOf(result, "Orders.Row.g.cs");
        string getAll = SourceOf(result, "Orders.GetAll.auto.g.cs");

        Assert.Contains("public int Read { get; set; }", row);
        Assert.Contains("public static Order __Read(DbDataReader reader)", row);
        Assert.Contains("Order.__Read(__reader)", getAll);
    }

    [Fact]
    public void ARowWithoutAReadColumn_KeepsTheReadMaterializer()
    {
        string row = Source("column", "status", "sqlite", "Orders.Row.g.cs");

        Assert.Contains("public static Order Read(DbDataReader reader)", row);
    }

    [Fact]
    public void AScopeNamedLikeTheForwardedMethod_QualifiesTheForwardingCall()
    {
        string overloads = Source("scope", "Upsert", "sqlite", "Orders.Poco.auto.g.cs");

        Assert.Contains("=> this.Upsert(Upsert, row.Id, row.Note);", overloads);
        Assert.Contains("=> Orders.Upsert(conn, Upsert, row.Id, row.Note, transaction);", overloads);
        Assert.Contains("=> UpsertAsync(Upsert, row.Id, row.Note, cancellationToken);", overloads);
    }

    [Fact]
    public void AScopeNamedLikeTheAsyncMethod_QualifiesOnlyTheAsyncCalls()
    {
        string overloads = Source("scope", "InsertAsync", "sqlite", "Orders.Poco.auto.g.cs");

        Assert.Contains("=> this.InsertAsync(InsertAsync, row.Id, row.Note, cancellationToken);", overloads);
        Assert.Contains("=> Orders.InsertAsync(conn, InsertAsync, row.Id, row.Note, transaction, cancellationToken);", overloads);
        Assert.Contains("=> Insert(InsertAsync, row.Id, row.Note);", overloads);
    }

    [Fact]
    public void AnAwaitParameter_IsEscaped()
    {
        string find = Source("qparam", "await", "sqlite", "Orders.Find.g.cs");

        Assert.Contains("string? @await", find);
        Assert.Contains("(object?)@await ?? DBNull.Value", find);
    }

    [Fact]
    public void ATableWhoseRowTypeIsDateTime_QualifiesTheBulkReaderOverride()
    {
        string bulk = Source("table", "date_time", "mysql", "DateTime.BulkInsert.auto.g.cs");

        Assert.Contains("public override global::System.DateTime GetDateTime(int ordinal) => (global::System.DateTime)GetValue(ordinal);", bulk);
    }

    [Fact]
    public void ATableWhoseRowTypeIsGuid_QualifiesTheBulkReaderOverride()
    {
        string bulk = Source("table", "guid", "mysql", "Guid.BulkInsert.auto.g.cs");

        Assert.Contains("public override global::System.Guid GetGuid(int ordinal) => (global::System.Guid)GetValue(ordinal);", bulk);
    }

    private static string Source(string site, string name, string dialect, string file) =>
        SourceOf(Generate(site, name, dialect), file);
}
