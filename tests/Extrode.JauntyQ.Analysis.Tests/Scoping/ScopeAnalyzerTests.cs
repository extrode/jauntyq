using Extrode.JauntyQ.Analysis.Scoping;
using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests.Scoping;

public class ScopeAnalyzerTests
{
    private static readonly ScopeColumn[] Scopes =
    {
        new("orders", "tenant_id"),
        new("order_lines", "tenant_id"),
    };

    private static List<UnprovenReach> Find(string sql) =>
        ScopeAnalyzer.FindUnproven(SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Q"), Scopes);

    private static string Only(string sql) => Assert.Single(Find(sql)).Message;

    [Theory]
    [InlineData("select o.id from orders o where o.tenant_id = @tenantId")]
    [InlineData("select o.id from orders o where @t = o.tenant_id and o.id = @id")]
    [InlineData("select id from orders where tenant_id = @t")]
    [InlineData("select id from orders where orders.tenant_id = @t")]
    [InlineData("select o.id from orders o join customers c on c.id = o.id where o.tenant_id = @t")]
    [InlineData("select c.id from customers c join orders o on o.id = c.id and o.tenant_id = @t")]
    [InlineData("select o.id from orders o join customers c on c.id = o.id and o.tenant_id = @t")]
    [InlineData("select c.id from customers c left join orders o on o.id = c.id and o.tenant_id = @t")]
    [InlineData("select c.id from orders o right join customers c on c.id = o.id and o.tenant_id = @t")]
    [InlineData("select c.id from customers c full join orders o on o.id = c.id where o.tenant_id = @t")]
    [InlineData("select c.id from orders o full join customers c on c.id = o.id where o.tenant_id = @t")]
    [InlineData("select c.id from customers c join orders o on o.id = c.id and o.tenant_id = @t right join customers k on k.id = o.id")]
    [InlineData("with Orders as (select c.id from customers c) select o.id from orders o")]
    [InlineData("select id from customers where id = @id")]
    [InlineData("update orders set note = @note where tenant_id = @t and id = @id")]
    [InlineData("update orders set note = @note where orders.tenant_id = @t")]
    [InlineData("delete from orders where id = @id and tenant_id = @t")]
    [InlineData("delete from orders o where o.tenant_id = @t")]
    [InlineData("update orders as o set note = @note where o.tenant_id = @t")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note)")]
    [InlineData("insert into order_lines (id, tenant_id) select @id, @t from orders o where o.tenant_id = @t")]
    [InlineData("with recent as (select o.id from orders o where o.tenant_id = @t) select r.id from recent r")]
    [InlineData("select c.id from customers c where exists (select 1 from orders o where o.id = c.id and o.tenant_id = @t)")]
    [InlineData("select c.id from customers c where c.id in (select o.id from orders o where o.tenant_id = @t)")]
    [InlineData("select c.id, exists(select 1 from orders o where o.id = c.id and o.tenant_id = @t) as has_orders from customers c")]
    [InlineData("select id from orders where tenant_id = @t limit 10")]
    [InlineData("select id from orders where tenant_id = @t limit 10 offset 5")]
    [InlineData("select id from orders where tenant_id = @t;")]
    [InlineData("select id from orders where tenant_id = @t fetch first 10 rows only")]
    [InlineData("select id from orders where tenant_id = @t option (recompile)")]
    [InlineData("delete from orders where id = @id and tenant_id = @t returning id")]
    [InlineData("update orders set note = @note where id = @id and tenant_id = @t returning id")]
    [InlineData("select id from orders where tenant_id = @t for update")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = excluded.tenant_id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where tenant_id = EXCLUDED.tenant_id and orders.id > 0")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where excluded.tenant_id = orders.tenant_id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = @tenantId")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = excluded.tenant_id returning id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = excluded.tenant_id;")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do nothing")]
    [InlineData("insert into orders (id, tenant_id) select @id, @t from order_lines l where l.tenant_id = @t on conflict (id) do update set note = 'x' where orders.tenant_id = excluded.tenant_id")]
    public void AProvenReach_IsAccepted(string sql)
    {
        Assert.Empty(Find(sql));
    }

    [Fact]
    public void AnUnfilteredFromTable_IsRefused_WithTheFix()
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is read in FROM without a filter on its scope column 'tenant_id'. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("select o.id from orders o where o.id = @id"));
    }

    [Fact]
    public void AnUnfilteredInnerJoin_IsRefused()
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is read in a JOIN without a filter on its scope column 'tenant_id'. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("select c.id from customers c join orders o on o.id = c.id"));
    }

    [Theory]
    [InlineData("select o.id from orders o where o.tenant_id = @t or o.id = @id")]
    [InlineData("select o.id from orders o where not o.tenant_id = @t")]
    [InlineData("select o.id from orders o where o.tenant_id = 42")]
    [InlineData("select o.id from orders o where o.tenant_id = o.id")]
    [InlineData("select o.id from orders o where o.tenant_id is null")]
    [InlineData("select o.id from orders o where o.tenant_id in (@ids)")]
    [InlineData("select o.id from orders o where o.tenant_id <> @t")]
    [InlineData("select o.id from orders o where (o.tenant_id = @t)")]
    [InlineData("select o.id from orders o join customers c on c.id = o.id where c.id = @t")]
    [InlineData("select o.id from orders o join order_lines l on l.order_id = o.id where l.tenant_id = @t")]
    [InlineData("select o.id from orders o join customers c on c.id = o.id where tenant_id = @t")]
    [InlineData("select o.id from orders o where x.tenant_id = @t")]
    public void AnythingButAnEqualityToAParameterOnThisTable_IsNotProof(string sql)
    {
        Assert.Contains(Find(sql), r => r.Table == "orders");
    }

    [Fact]
    public void ANullableSideProofInWhere_IsRefused_AndPointsAtOn()
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is on the nullable side of a LEFT JOIN without a filter on its scope column 'tenant_id' in that join's ON clause. "
            + "Add o.tenant_id = @tenantId to the ON clause; in WHERE it would turn the outer join into an inner join. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("select c.id from customers c left join orders o on o.id = c.id where o.tenant_id = @t"));
    }

    [Fact]
    public void ARightJoinsNullableSide_NeedsAnOnProof()
    {
        Assert.Contains("is on the nullable side of a RIGHT JOIN ",
            Only("select c.id from orders o right join customers c on c.id = o.id where o.tenant_id = @t"));
    }

    [Fact]
    public void ARightJoinsOwnOn_DoesNotFilterTheTableItKeeps()
    {
        Assert.Contains("is read in a JOIN without",
            Only("select c.id from customers c right join orders o on o.id = c.id and o.tenant_id = @t"));
        Assert.Contains("is on the nullable side of a RIGHT JOIN ",
            Only("select c.id from customers c right join orders o on o.id = c.id and o.tenant_id = @t right join customers k on k.id = o.id"));
    }

    [Theory]
    [InlineData("select c.id from customers c full join orders o on o.id = c.id and o.tenant_id = @t")]
    [InlineData("select c.id from orders o full join customers c on c.id = o.id and o.tenant_id = @t")]
    public void AFullJoinsOn_IsNeverProof(string sql)
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is in a FULL JOIN without a filter on its scope column 'tenant_id' in the WHERE clause. "
            + "A FULL JOIN keeps unmatched rows from both sides, so an ON condition filters nothing. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only(sql));
    }

    [Fact]
    public void AnOuterJoinsOn_DoesNotFilterTheTablesBeforeIt()
    {
        Assert.Contains("is read in FROM without",
            Only("select o.id from orders o left join customers c on c.id = o.id and o.tenant_id = @t"));
    }

    [Fact]
    public void AProofInAnotherOuterJoinsOn_DoesNotCount()
    {
        Assert.Single(Find(
            "select c.id from customers c left join orders o on o.id = c.id left join order_lines l on l.order_id = o.id and o.tenant_id = @t and l.tenant_id = @t"));
    }

    [Fact]
    public void AnOuterProof_DoesNotCoverACte()
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is read in FROM in CTE 'recent' without a filter on its scope column 'tenant_id'. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause of CTE 'recent'. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("with recent as (select o.id, o.tenant_id from orders o) select r.id from recent r where r.tenant_id = @t"));
    }

    [Fact]
    public void ACteProvenInside_ButAScopedTableReadOutside_IsRefusedOutside()
    {
        Assert.Contains("is read in a JOIN without",
            Only("with recent as (select o.id from orders o where o.tenant_id = @t) select r.id from recent r join order_lines l on l.order_id = r.id"));
    }

    [Theory]
    [InlineData("select c.id from customers c where exists (select 1 from orders o where o.id = c.id)", "an EXISTS subquery")]
    [InlineData("select c.id from customers c where c.id in (select o.id from orders o)", "an IN subquery")]
    [InlineData("select c.id, exists(select 1 from orders o where o.id = c.id) as has_orders from customers c where c.id = @id", "an EXISTS expression")]
    [InlineData("select c.id, exists(select 1 from customers k where exists (select 1 from orders o where o.id = k.id)) as x from customers c", "an EXISTS subquery")]
    public void ASubquery_NeedsItsOwnProof(string sql, string where)
    {
        Assert.Contains($" in {where} without a filter", Only(sql));
    }

    [Fact]
    public void AnOuterProof_DoesNotCoverAnExistsExpression()
    {
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is read in FROM in an EXISTS expression without a filter on its scope column 'tenant_id'. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause of an EXISTS expression. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("select l.id, exists(select 1 from orders o where o.id = l.order_id) as x from order_lines l where l.tenant_id = @t"));
    }

    [Fact]
    public void TwoUnprovenReaches_AreTwoFindings()
    {
        var found = Find("select o.id from orders o join order_lines l on l.order_id = o.id");

        Assert.Equal(new[] { "orders", "order_lines" }, found.ConvertAll(r => r.Table));
    }

    [Fact]
    public void AnUnfilteredUpdateTarget_IsRefused()
    {
        Assert.Equal(
            "Scoped table 'orders' is the UPDATE target without a filter on its scope column 'tenant_id'. "
            + "Add orders.tenant_id = @tenantId as a top-level AND condition of the WHERE clause. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only("update orders set note = @note where id = @id"));
    }

    [Fact]
    public void AnUnfilteredDeleteTarget_IsRefused()
    {
        Assert.StartsWith("Scoped table 'orders' is the DELETE target without",
            Only("delete from orders where id = @id"));
    }

    [Theory]
    [InlineData("delete from orders where id in (select c.id from customers c where c.id = @t)")]
    public void ATargetProofMustNameTheTarget(string sql)
    {
        Assert.Contains("is the DELETE target", Only(sql));
    }

    [Theory]
    [InlineData("update orders set note = @n from customers c where c.tenant_id = @t")]
    [InlineData("update orders o set note = @n from customers c where c.tenant_id = @t")]
    [InlineData("delete from orders o using customers c where c.tenant_id = @t")]
    public void ATargetFilterQualifiedByAnotherTable_IsNotProof(string sql)
    {
        Assert.Contains(Find(sql), r => r.Table == "orders");
    }

    [Theory]
    [InlineData("insert into orders (id, note) values (@id, @note)")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, 42, @note)")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, -1, @note)")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, 'a', @note)")]
    [InlineData("insert into orders (id, note) values (@id, @note) on conflict (id) do update set tenant_id = @tenantId")]
    [InlineData("insert into orders (id, note) values (@id, @note) on duplicate key update tenant_id = @tenantId")]
    public void AnInsertWithoutAParameterScopeValue_IsRefused(string sql)
    {
        Assert.Equal(
            "Scoped table 'orders' is the INSERT target, and its scope column 'tenant_id' is not given a parameter value. "
            + "List 'tenant_id' in the column list with a parameter such as @tenantId; a literal or an omitted column is not proof. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only(sql));
    }

    [Theory]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.note = excluded.note")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = excluded.tenant_id or orders.id = 1")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = excluded.note")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where order_lines.tenant_id = excluded.tenant_id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id <> excluded.tenant_id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.tenant_id = orders.tenant_id")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where orders.note = @note")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) do update set note = @note where 'tenant_id' = @tenantId")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on conflict (id) where tenant_id = @tenantId do update set note = @note")]
    public void AnUpsertWithoutAnUpdateBranchProof_IsRefused(string sql)
    {
        Assert.Equal(
            "Scoped table 'orders' is the target of ON CONFLICT ... DO UPDATE without a filter on its scope column 'tenant_id' in the DO UPDATE's WHERE, so a conflict with another scope's row would update that row. "
            + "Add WHERE orders.tenant_id = EXCLUDED.tenant_id as a top-level AND condition. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only(sql));
    }

    [Theory]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on duplicate key update note = @note")]
    [InlineData("insert into orders (id, tenant_id, note) values (@id, @tenantId, @note) on duplicate key update tenant_id = @tenantId")]
    public void AnOnDuplicateKeyUpdate_IsAlwaysRefused(string sql)
    {
        Assert.Equal(
            "Scoped table 'orders' is the target of ON DUPLICATE KEY UPDATE. It takes no WHERE, so a key collision with another scope's row would update that row. "
            + "Write an INSERT and an UPDATE that each prove 'tenant_id' instead. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            Only(sql));
    }

    [Fact]
    public void AnInsertSelect_ChecksTheTargetAndTheSourceApart()
    {
        var found = Find("insert into orders (id, tenant_id) select o.id, o.tenant_id from orders o");

        Assert.Equal(2, found.Count);
        Assert.Contains("is the INSERT target,", found[0].Message);
        Assert.Contains("is read in FROM without", found[1].Message);
    }

    [Fact]
    public void AScopeParameterInAnInsertSelectsWhere_IsNotTheInsertedValue()
    {
        Assert.Contains("is the INSERT target,",
            Only("insert into orders (id, note) select l.id, l.note from order_lines l where l.tenant_id = @t"));
    }

    [Fact]
    public void AnUnaliasedTable_IsNamedOnce()
    {
        Assert.StartsWith("Scoped table 'orders' is read in FROM without", Only("select id from orders"));
    }

    [Fact]
    public void AColumnWithNoLetters_IsSuggestedAsItIs()
    {
        var model = SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize("select id from orders"), "Q");

        Assert.Contains("Add orders._ = @_ as",
            Assert.Single(ScopeAnalyzer.FindUnproven(model, new[] { new ScopeColumn("orders", "_") })).Message);
    }

    [Fact]
    public void ACteNamedLikeAScopedTable_IsNotTheTable()
    {
        Assert.Empty(Find("with orders as (select c.id from customers c) select o.id from orders o"));
    }

    [Fact]
    public void NoScopes_FindsNothing()
    {
        var model = SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize("select id from orders"), "Q");

        Assert.Empty(ScopeAnalyzer.FindUnproven(model, Array.Empty<ScopeColumn>()));
    }

    [Fact]
    public void TableAndColumnNames_MatchWithoutRegardToCase()
    {
        Assert.Empty(Find("select O.ID from ORDERS O where o.TENANT_ID = @t"));
        Assert.Single(Find("select O.ID from ORDERS O"));
    }
}
