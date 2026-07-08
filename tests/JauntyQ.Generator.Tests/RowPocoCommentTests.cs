using JauntyQ.Generator;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Consumer-gaps-report gap #4: when Inflector.RowTypeName falls back to the
/// "&lt;Entity&gt;Row" suffix (an already-singular table name, e.g. "basket"),
/// the row POCO and the query accessor sit right next to each other with
/// easily confused names. A one-line comment on the POCO should call that
/// out; every other table (whose row type actually differs from the entity
/// name) needs no such disambiguation and gets no comment.
/// </summary>
public class RowPocoCommentTests
{
    private static TableSchema MakeTable(string name)
    {
        var table = new TableSchema { Name = name };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsNullable = false };
        return table;
    }

    [Fact]
    public void FallbackRowSuffix_EmitsDisambiguatingComment()
    {
        // "basket" singularizes to itself, so Inflector.RowTypeName falls
        // back to "BasketRow" -- the exact condition this comment guards.
        var source = CodeEmitter.EmitRowPoco("BasketRow", MakeTable("basket"));

        Assert.Contains("// Row type; see JauntyDb.Basket for the query accessor.", source);
    }

    [Fact]
    public void NonFallbackRowType_NoComment()
    {
        // "orders" singularizes to "Order", which differs from the entity's
        // own PascalCase form ("Orders") -- no ambiguity, no comment.
        var source = CodeEmitter.EmitRowPoco("Order", MakeTable("orders"));

        Assert.DoesNotContain("Row type; see JauntyDb.", source);
    }
}
