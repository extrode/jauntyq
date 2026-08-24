using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.SqlParser.Tests;

[Trait("Category", "AllocationBudget")]
public class AllocationBudgetTests
{
    private const string RepresentativeQuery = """
        select p.product_id, p.product_name, c.category_name, sum(od.quantity) as total
        from products p
        join categories c on c.category_id = p.category_id
        join order_details od on od.product_id = p.product_id
        where p.discontinued = 0 and p.unit_price between @min and @max
        group by p.product_id, p.product_name, c.category_name
        having sum(od.quantity) > @threshold
        order by total desc
        """;

    private const int Iterations = 200;

    private static long Measure(Action action, int iterations)
    {
        for (int i = 0; i < 20; i++)
            action();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    [Fact]
    public void Tokenize_StaysWithinItsPerCallAllocationBudget()
    {
        long perCall = Measure(() => SqlTokenizer.Tokenize(RepresentativeQuery), Iterations);

        Assert.InRange(perCall, 0, 7_000);
    }

    [Fact]
    public void ParseOfAlreadyTokenizedInput_StaysWithinItsPerCallAllocationBudget()
    {
        var tokens = SqlTokenizer.Tokenize(RepresentativeQuery);

        long perCall = Measure(() => SqlParser.Parse(tokens, "BudgetQuery"), Iterations);

        Assert.InRange(perCall, 0, 12_000);
    }

    [Fact]
    public void TokenizeAllocation_ScalesLinearlyNotQuadratically_WithInputLength()
    {
        string single = RepresentativeQuery;
        string tenfold = string.Join(";\n", Enumerable.Repeat(RepresentativeQuery, 10));

        long one = Measure(() => SqlTokenizer.Tokenize(single), Iterations);
        long ten = Measure(() => SqlTokenizer.Tokenize(tenfold), Iterations);

        Assert.InRange(ten, one, one * 20);
    }
}
