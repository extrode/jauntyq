using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests;

/// <summary>
/// Reframed concurrency stress point (see the test log's Part 1
/// scope decisions): the source app has zero optimistic-concurrency or
/// RowVersion machinery, so there is no conflict-detection behavior to
/// preserve. Instead this asserts "no corruption/deadlock under concurrent
/// writes" and documents SQL Server's default isolation-level behavior as a
/// finding rather than a pass/fail assertion.
/// </summary>
public class ConcurrencyTests : IClassFixture<EShopOnWebSqlServerFixture>
{
    private readonly EShopOnWebSqlServerFixture _fx;

    public ConcurrencyTests(EShopOnWebSqlServerFixture fx) => _fx = fx;

    private (SqlConnection Conn, JauntyDb Db) OpenIndependentConnection()
    {
        var conn = new SqlConnection(_fx.ConnectionString);
        conn.Open();
        return (conn, new JauntyDb(conn));
    }

    [SkippableFact]
    public async Task ConcurrentUpdates_NoTransaction_NeverCorruptOrDeadlock()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var basketId = _fx.Db.Basket.Insert("buyer-concurrency-1");
        var itemId = _fx.Db.BasketItem.Insert(basketId, CatalogItemId: 1, UnitPrice: 8.5m, Quantity: 1);

        var (connA, dbA) = OpenIndependentConnection();
        var (connB, dbB) = OpenIndependentConnection();
        using var _a = connA;
        using var _b = connB;

        var taskA = Task.Run(() => dbA.BasketItem.UpdateQuantity(Id: itemId, Quantity: 10));
        var taskB = Task.Run(() => dbB.BasketItem.UpdateQuantity(Id: itemId, Quantity: 20));

        await Task.WhenAll(taskA, taskB);

        var items = _fx.Db.BasketItem.GetByBasketId(basketId);
        Assert.Single(items);
        Assert.True(items[0].Quantity == 10 || items[0].Quantity == 20,
            $"Expected quantity to be exactly one writer's value (10 or 20), got corrupted value {items[0].Quantity}.");
    }

    [SkippableFact]
    public async Task ConcurrentUpdates_UnderTransactions_DoNotDeadlock()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var basketId = _fx.Db.Basket.Insert("buyer-concurrency-2");
        var itemId = _fx.Db.BasketItem.Insert(basketId, CatalogItemId: 1, UnitPrice: 8.5m, Quantity: 1);

        var (connA, dbA) = OpenIndependentConnection();
        var (connB, dbB) = OpenIndependentConnection();
        using var _a = connA;
        using var _b = connB;

        // SQL Server's default READ COMMITTED isolation blocks the second
        // writer's UPDATE until the first transaction commits/rolls back,
        // rather than deadlocking or corrupting - documented as a finding
        // in the test log, not asserted here since blocking
        // duration/order is not deterministic across engines.
        var taskA = Task.Run(() =>
        {
            using var tx = dbA.BeginTransaction();
            dbA.BasketItem.UpdateQuantity(Id: itemId, Quantity: 30);
            tx.Commit();
        });
        var taskB = Task.Run(() =>
        {
            using var tx = dbB.BeginTransaction();
            dbB.BasketItem.UpdateQuantity(Id: itemId, Quantity: 40);
            tx.Commit();
        });

        var completed = await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(30))
            .ContinueWith(t => !t.IsFaulted && !t.IsCanceled);
        Assert.True(completed, "Concurrent transactional updates deadlocked or failed to complete within 30s.");

        var items = _fx.Db.BasketItem.GetByBasketId(basketId);
        Assert.Single(items);
        Assert.True(items[0].Quantity == 30 || items[0].Quantity == 40);
    }
}
