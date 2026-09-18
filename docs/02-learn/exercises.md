# Exercises

These build directly on the tutorial database from [README.md](README.md):
`Categories`, `Products`, and `Shippers` in `tutorial.db`, with the schema
snapshot at `db/schema/jaunty.schema.json`. Work through them in order - each
one assumes the previous one's schema changes are in place and re-pulled.

## Exercise 1: Add a table, get CRUD for free

**Goal:** Add a `Suppliers` table and use its full auto-CRUD surface without
writing a single `.sql` file.

**Starter:**

```sql
-- Add to your schema (e.g. run against tutorial.db with sqlite3):
CREATE TABLE Suppliers (
    SupplierId  INTEGER PRIMARY KEY,
    CompanyName VARCHAR(40) NOT NULL,
    City        VARCHAR(15) NULL
    -- TODO: no other columns needed yet
);

-- TODO: give Products a foreign key to Suppliers so a loader gets generated:
-- ALTER TABLE Products ADD COLUMN SupplierId INTEGER REFERENCES Suppliers(SupplierId);
```

```csharp
// TODO: re-pull the schema snapshot, rebuild, then:
// - insert a supplier and capture its id
// - fetch it back with GetById
// - insert a product referencing it
// - call the FK loader JauntyQ generated for you
```

**Hints:**

- The command is the same one from the tutorial:
  `dotnet run --project src/Extrode.JauntyQ.Cli -f net8.0 -- schema pull --provider sqlite --connection "Data Source=tutorial.db" --output db/schema/jaunty.schema.json`.
- A foreign key only shows up in the snapshot if it is a real `REFERENCES`
  (or table-level `FOREIGN KEY`) constraint in the DDL - a same-named column
  alone is not enough.
- The loader is named `GetBy<FkColumn>`, so a `SupplierId` foreign key on
  `Products` gives you `db.Products.GetBySupplierId(int)`.

<details>
<summary>Solution</summary>

```sql
CREATE TABLE Suppliers (
    SupplierId  INTEGER PRIMARY KEY,
    CompanyName VARCHAR(40) NOT NULL,
    City        VARCHAR(15) NULL
);

ALTER TABLE Products ADD COLUMN SupplierId INTEGER REFERENCES Suppliers(SupplierId);
```

```bash
dotnet run --project src/Extrode.JauntyQ.Cli -f net8.0 -- schema pull \
  --provider sqlite \
  --connection "Data Source=tutorial.db" \
  --output db/schema/jaunty.schema.json
```

```csharp
int supplierId = db.Suppliers.Insert("Exotic Liquids", "London");
Supplier? supplier = db.Suppliers.GetById(supplierId);

int productId = db.Products.Insert("Chai", 1, 18.00m, supplierId);

List<Product> fromSupplier = db.Products.GetBySupplierId(supplierId);
```

No `.sql` file was written for any of this: `Insert`, `GetById`, and the
`GetBySupplierId` FK loader are all synthesized from the snapshot alone.

</details>

## Exercise 2: Override a synthetic

**Goal:** Confirm that a user `.sql` file with the same entity and method
name as a synthetic wins, silently.

**Starter:**

```sql
-- db/tables/Products/GetAll.sql
-- TODO: select the same columns as the synthetic GetAll, but order the
-- rows by UnitPrice descending instead of the synthetic's default order.
select ProductId, ProductName, CategoryId, UnitPrice
from Products
-- TODO: add an ORDER BY here
```

**Hints:**

- The rule from the tutorial: "A user `.sql` file with the matching entity +
  method name overrides the synthetic." Folder name = entity, file name
  (without `.sql`) = method name - `db/tables/Products/GetAll.sql` overrides
  `Products.GetAll`.
- You do not need to change any C# call site: `db.Products.GetAll()` now
  runs your SQL instead of the generated one.
- Prove the override took effect by asserting the returned order, not just
  that the call compiles.

<details>
<summary>Solution</summary>

```sql
-- db/tables/Products/GetAll.sql
select ProductId, ProductName, CategoryId, UnitPrice
from Products
order by UnitPrice desc
```

```csharp
List<Product> products = db.Products.GetAll();

// If the synthetic (unordered by price) were still in effect, this would
// not reliably hold. With the override in place, it always does:
for (int i = 1; i < products.Count; i++)
{
    Debug.Assert(products[i - 1].UnitPrice >= products[i].UnitPrice);
}
```

</details>

## Exercise 3: Fix a JOIN the analyzer flags

**Goal:** Write a join between `Products` and `Categories`, trigger a
`JNT8002` warning by wrapping a filter column in a function, then fix it.

**Starter:**

```sql
-- db/tables/Products/GetByCategoryName.sql
select p.ProductId, p.ProductName, p.UnitPrice, c.CategoryName
from Products p
join Categories c on c.CategoryId = p.CategoryId
where upper(c.CategoryName) = @CategoryName
-- TODO: this compiles, but rebuild and read the warning JauntyQ prints.
-- TODO: rewrite the WHERE clause so the column is not wrapped in a function.
```

**Hints:**

- This is `JNT8002` from the performance analyzer: "a function on the
  column defeats any index." It fires because `upper(...)` wraps
  `c.CategoryName`, not because of the join itself.
- The fix direction from the analyzer's own message: "Compute on the
  parameter side instead." Keep the column bare in the `WHERE` clause and
  normalize the value before you bind it as a parameter (or pass a
  correctly-cased value directly, since the exercise data is small enough
  that exact-case matching is fine).
- JNT8002 is a warning, not an error - the query still compiles either way.
  The point of the exercise is to make the warning go away, not to work
  around a build failure.

<details>
<summary>Solution</summary>

```sql
-- db/tables/Products/GetByCategoryName.sql
select p.ProductId, p.ProductName, p.UnitPrice, c.CategoryName
from Products p
join Categories c on c.CategoryId = p.CategoryId
where c.CategoryName = @CategoryName
```

```csharp
// Normalize on the call side instead of wrapping the column in SQL:
List<Result> beverages = db.Products.GetByCategoryName("Beverages");
```

The column (`c.CategoryName`) is now bare in the `WHERE` clause, so nothing
prevents an index seek on it. `JNT8002` no longer fires because the check
looks for a function wrapping the column, not the parameter.

</details>

## Exercise 4: Atomic parent + children insert

**Goal:** Insert a new `Category` and several `Products` that reference it,
all inside one transaction, using the identity JauntyQ hands back from the
parent insert.

**Starter:**

```csharp
public static void AddCategoryWithProducts(JauntyDb db)
{
    // TODO: start a transaction on db
    // TODO: insert a new Category, capture its identity
    // TODO: insert two or more Products using that CategoryId
    // TODO: commit - and make sure a thrown exception rolls everything back
}
```

**Hints:**

- `Categories.Insert` already returns the new identity as an `int` - that is
  what `-- @identity` (implicit on auto-CRUD inserts, or explicit if you
  write it yourself) gives you.
- Stay on `db.*` for every call inside the transaction. Static methods
  (`Products.Insert(conn, ...)`) do not see the `JauntyDb`-held transaction
  and will not auto-enlist.
- Disposing the transaction without calling `Commit()` rolls back - you do
  not need a manual `try/catch` to undo a partial insert on failure, just
  don't call `Commit()` on the failing path.

<details>
<summary>Solution</summary>

```csharp
public static void AddCategoryWithProducts(JauntyDb db)
{
    using var tx = db.BeginTransaction();

    int categoryId = db.Categories.Insert("Grains/Cereals");

    db.Products.Insert("Gnocchi di Nonna Alice", categoryId, 38.00m);
    db.Products.Insert("Singaporean Hokkien Fried Mee", categoryId, 14.00m);

    tx.Commit();
}
```

If anything above throws before `tx.Commit()` runs, disposing `tx` at the
end of the `using` block rolls back the category insert along with both
product inserts - there is no partially-created category left behind.

</details>

## Exercise 5: Simulate schema drift

**Goal:** Change the live database without re-pulling, run `schema verify`,
read the reported drift, then reconcile it.

**Before you start:** `schema verify` is gated by the `contract-testing`
entitlement (see
[Licensing and activation](../03-guides/licensing-and-activation.md)). Without
an activated license, it exits `3` with an entitlement-required message
instead of the `0`/`2` outcomes below, that is expected, not a setup mistake.
If you have an evaluation license, activate it first
(`jauntyq activate --license path/to/jaunty.license.json`, from an installed
`Extrode.JauntyQ.Cli.Premium`; that tool is not built from this repository);
otherwise, read through this exercise to understand the behavior rather than
running it directly.

**Starter:**

```bash
# TODO: alter tutorial.db directly, without touching db/schema/jaunty.schema.json
sqlite3 tutorial.db "ALTER TABLE Products ADD COLUMN Notes VARCHAR(100);"

# TODO: run schema verify against the now-drifted database and read its output
jauntyq schema verify \
  --provider sqlite \
  --connection "Data Source=tutorial.db" \
  --output db/schema/jaunty.schema.json

# TODO: what exit code did that produce? Check it, then fix the drift.
```

**Hints:**

- `schema verify` never touches your `.sql` files or the generator - it
  only compares the live database against the committed snapshot and
  reports. Exit code `0` means match, exit code `2` means drift, with the
  differences listed.
- A brand-new table would print as an informational "new table... not in
  snapshot" line rather than counting as drift; a changed column on an
  existing table (like this one) counts as drift and produces exit code `2`.
- The fix is the same command you already know: re-pull, then rebuild so
  the generator picks up the new column if you want to query it.

<details>
<summary>Solution</summary>

```bash
sqlite3 tutorial.db "ALTER TABLE Products ADD COLUMN Notes VARCHAR(100);"

jauntyq schema verify \
  --provider sqlite \
  --connection "Data Source=tutorial.db" \
  --output db/schema/jaunty.schema.json
# exit code 2 - "SCHEMA DRIFT: 1 difference(s) between snapshot ... and the
# live database", listing the new Products.Notes column.

# Reconcile the snapshot with reality:
dotnet run --project src/Extrode.JauntyQ.Cli -f net8.0 -- schema pull \
  --provider sqlite \
  --connection "Data Source=tutorial.db" \
  --output db/schema/jaunty.schema.json

# Re-running verify now exits 0: "Snapshot ... matches the live database."
```

Auto-CRUD's synthetic `GetAll`/`Insert`/etc. for `Products` now include
`Notes` too, without any `.sql` file changes - the snapshot is the only
thing that changed.

</details>
