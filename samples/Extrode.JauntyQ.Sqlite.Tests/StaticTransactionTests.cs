using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Sqlite.Tests;

/// <summary>
/// End-to-end proof that the STATIC generated variants can participate in a
/// caller-managed transaction via the optional trailing DbTransaction
/// parameter (instance variants flow the ambient JauntyDb transaction
/// instead). Rollback discards the write; commit persists it; reads inside
/// the transaction see its uncommitted rows.
/// </summary>
public class StaticTransactionTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public StaticTransactionTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void StaticInsert_InRolledBackTransaction_IsDiscarded()
    {
        using var conn = _fx.NewConnection();
        using (var tx = conn.BeginTransaction())
        {
            int id = Shippers.Insert(conn, "Tx Rollback Shipper", "(000) 000-0000", tx);

            // Visible inside the same transaction...
            var inside = Shippers.GetById(conn, id, tx);
            Assert.NotNull(inside);

            tx.Rollback();

            // ...gone after rollback.
            Assert.Null(Shippers.GetById(conn, id));
        }
    }

    [Fact]
    public void StaticInsert_InCommittedTransaction_Persists()
    {
        using var conn = _fx.NewConnection();
        int id;
        using (var tx = conn.BeginTransaction())
        {
            id = Shippers.Insert(conn, "Tx Commit Shipper", "(111) 111-1111", tx);
            tx.Commit();
        }

        var fetched = Shippers.GetById(conn, id);
        Assert.NotNull(fetched);
        Assert.Equal("Tx Commit Shipper", fetched!.CompanyName);
    }

    [Fact]
    public async Task StaticInsertAsync_InRolledBackTransaction_IsDiscarded()
    {
        using var conn = _fx.NewConnection();
        using (var tx = conn.BeginTransaction())
        {
            int id = await Shippers.InsertAsync(conn, "Tx Async Shipper", "(222) 222-2222", tx);
            tx.Rollback();

            Assert.Null(await Shippers.GetByIdAsync(conn, id));
        }
    }

    [Fact]
    public void StaticMethod_WithoutTransaction_StillWorks()
    {
        // The parameter is optional: existing no-transaction call sites are
        // source-compatible.
        using var conn = _fx.NewConnection();
        var all = Shippers.GetAll(conn);
        Assert.True(all.Count >= 2);
    }
}
