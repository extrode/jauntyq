using Extrode.JauntyQ.Generated;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

/// <summary>
/// Live coverage for Tier 3 migration intelligence: the JQGadgets API used
/// here was generated entirely from db/migrations/0001_create_jq_gadgets.sql
/// (the table does not exist in Northwind). Each test creates the real table
/// from the same DDL, runs the generated code against it, and drops it -
/// including the first live optimistic-concurrency conflict proof.
/// </summary>
[Collection("Northwind")]
public class Tier3LiveTests : IDisposable
{
    // Keep in sync with db/migrations/0001_create_jq_gadgets.sql.
    private const string MigrationDdl = @"
create table JQ_Gadgets (
    gadget_id int not null primary key identity(1,1),
    name nvarchar(40) not null,
    price decimal(10,2) null,
    row_version rowversion not null
)";

    private readonly NorthwindFixture _fixture;
    private readonly SqlConnection _conn = null!;
    private readonly JauntyDb _db = null!;

    public Tier3LiveTests(NorthwindFixture fixture)
    {
        _fixture = fixture;
        if (!fixture.Available) return;
        _conn = new SqlConnection(NorthwindFixture.ConnectionString);
        _conn.Open();
        Execute("drop table if exists JQ_Gadgets");
        Execute(MigrationDdl);
        _db = new JauntyDb(_conn);
    }

    public void Dispose()
    {
        if (!_fixture.Available) return;
        Execute("drop table if exists JQ_Gadgets");
        _conn.Dispose();
    }

    private void Execute(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void MigrationGeneratedApi_FullCrudRoundTrip()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        int id = _db.JQGadgets.Insert("Widget", 19.99m);
        Assert.True(id > 0);

        JQGadget? gadget = _db.JQGadgets.GetById(id);
        Assert.NotNull(gadget);
        Assert.Equal("Widget", gadget.Name);
        Assert.Equal(19.99m, gadget.Price);
        Assert.NotNull(gadget.RowVersion); // database-assigned token came back

        gadget.Name = "Widget v2";
        Assert.Equal(1, _db.JQGadgets.Update(gadget));

        var reread = _db.JQGadgets.GetById(id);
        Assert.NotNull(reread);
        Assert.Equal("Widget v2", reread.Name);
        Assert.Equal(1, _db.JQGadgets.Delete(reread));
        Assert.Null(_db.JQGadgets.GetById(id));
    }

    [SkippableFact]
    public void OptimisticConcurrency_StaleUpdateAndDelete_Conflict()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        int id = _db.JQGadgets.Insert("Contested", null);

        var first = _db.JQGadgets.GetById(id);
        var second = _db.JQGadgets.GetById(id);
        Assert.NotNull(first);
        Assert.NotNull(second);

        // first writer wins and bumps the rowversion
        first.Name = "First writer";
        Assert.Equal(1, _db.JQGadgets.Update(first));

        // second writer holds a stale token: conflict, nothing written
        second.Name = "Second writer";
        Assert.Equal(0, _db.JQGadgets.Update(second));
        Assert.Equal(0, _db.JQGadgets.Delete(second));

        var current = _db.JQGadgets.GetById(id);
        Assert.NotNull(current);
        Assert.Equal("First writer", current.Name);

        // re-read gives a fresh token: the write goes through
        current.Name = "Second writer, retried";
        Assert.Equal(1, _db.JQGadgets.Update(current));
    }

    [SkippableFact]
    public void ValueSafety_AppliesToMigrationDefinedColumns()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var ex = Assert.Throws<ArgumentException>(
            () => _db.JQGadgets.Insert(new string('X', 41), null));

        Assert.Contains("JQ_Gadgets.name", ex.Message);
        Assert.Contains("max length (40)", ex.Message);
    }
}
