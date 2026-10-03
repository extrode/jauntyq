# Loading parents with their children

You have a list of parent rows (orders) and want each one's children (order
items). This guide shows the pattern JauntyQ supports for that, and why it does
not offer eager loading.

## The problem: one query per parent

The obvious code calls a child lookup once for every parent:

```csharp
var orders = db.Orders.GetRecent(since);
foreach (var order in orders)
{
    var items = db.OrderItems.GetByOrderId(order.OrderId);   // one round trip per order
}
```

For 200 orders that is 201 queries. This is the N+1 pattern, and `JNT8008`
warns about it when your project has both a query returning many parents and a
child lookup by the foreign key to that parent.

## The fix: two queries and a stitch

Read the parents, then read **all** their children in one query, then group the
children in C#.

The child query takes a list of parent ids. `-- @each` turns the parameter into
a list and expands it at call time:

```sql
-- db/OrderItems/GetByOrderIds.sql
-- @each ParentIds
SELECT order_item_id, order_id, sku, quantity
FROM order_items
WHERE order_items.order_id IN (@ParentIds)
```

Select the foreign key column (`order_id` here) so you can group on it.

```csharp
var orders = db.Orders.GetRecent(since);
var items = db.OrderItems.GetByOrderIds(orders.Select(o => o.OrderId).ToList());
var itemsByOrder = items.ToLookup(i => i.OrderId);

foreach (var order in orders)
{
    foreach (var item in itemsByOrder[order.OrderId])
    {
        // ...
    }
}
```

That is two queries however many orders there are. `JNT8008` does not fire on
a child query that filters its foreign key with `IN`.

Things to know:

- **An empty list costs nothing.** With no parents, the child query returns an
  empty result without opening a connection.
- **Very long lists have a limit.** Each list element is one parameter, and each
  database caps parameters per command (2,000 on SQL Server). Past that the call
  fails fast with `ArgumentException`; split the ids into chunks. See
  [`-- @each`](../06-reference/directives.md#-each) for the per-dialect numbers.
- **Composite foreign keys** do not fit this pattern, because `-- @each` expands
  one parameter, not a tuple. Join the child into the parent query instead.
- **The other fix is a join.** One query that joins children to parents returns
  each parent once per child. That suits small, flat reports; the two-query form
  suits building a parent-with-children structure.
- **Counting children** (`COUNT(*)` per parent) is better as one grouped query:
  `JOIN order_items ... GROUP BY order_id`.

## Why there is no eager loading

JauntyQ does not plan to add eager loading (`Include`, `select_related`). Eager
loading means taking one flat result set and rebuilding an object graph from
it: removing the repeated parent rows, keying by primary key and filling child
collections. That is the core of an ORM, and JauntyQ's model is that the SQL in
your file is the SQL that runs, returned as flat typed rows. The two-query
stitch above gets the same result with SQL you can read and a few lines of C#.

If an N+1 lookup is deliberate (one customer at a time on a detail screen,
never in a loop), accept it for that query with
[`-- @allow-n-plus-one <reason>`](../06-reference/directives.md#-allow-n-plus-one).
