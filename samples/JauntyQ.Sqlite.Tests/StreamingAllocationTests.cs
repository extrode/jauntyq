using Xunit;

namespace JauntyQ.Sqlite.Tests;

[Trait("Category", "AllocationBudget")]
public class StreamingAllocationTests : IClassFixture<StreamingScaleFixture>
{
    private readonly StreamingScaleFixture _fx;
    public StreamingAllocationTests(StreamingScaleFixture fx) => _fx = fx;

    private long BytesPerStreamedRow()
    {
        int warmup = 0;
        foreach (var p in _fx.Db.Products.StreamAll())
            warmup++;

        long before = GC.GetAllocatedBytesForCurrentThread();
        int rows = 0;
        foreach (var p in _fx.Db.Products.StreamAll())
            rows++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(4 + StreamingScaleFixture.SeededProductCount, rows);
        return allocated / rows;
    }

    [Fact]
    public void StreamingReadPath_StaysWithinItsPerRowAllocationBudget()
    {
        Assert.InRange(BytesPerStreamedRow(), 0, 250);
    }

    [Fact]
    public void StreamingAllocatesLessPerRowThanMaterializing()
    {
        foreach (var p in _fx.Db.Products.StreamAll()) { }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var list = _fx.Db.Products.GetAll();
        long materialized = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(4 + StreamingScaleFixture.SeededProductCount, list.Count);
        Assert.InRange(BytesPerStreamedRow() * list.Count, 0, materialized);
    }
}
