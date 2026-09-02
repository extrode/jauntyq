#!/usr/bin/env python3
"""
Generates schema.sqlserver.sql for the Northwind Testcontainers fixture.

DDL is emitted from the committed generator snapshot (jaunty.schema.json) so the
live SQL Server exactly matches the types the generated reader expects (e.g.
EmployeeId is smallint, not canonical Northwind's int). Seed data is reused
verbatim from Microsoft's canonical instnwnd.sql (MIT), split on GO batches and
kept only for the tables we load. Views are ported from the canonical bodies to
the PascalCase-no-spaces names this repo's db/views/*.sql queries reference.

Canonical source (MIT), download once:
    https://raw.githubusercontent.com/microsoft/sql-server-samples/master/samples/databases/northwind-pubs/instnwnd.sql

Run from the repo root:
    python samples/JauntyQ.Northwind.Tests/db/build_sqlserver_seed.py <path-to-instnwnd.sql>
"""
import json
import re
import sys
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SNAPSHOT = os.path.join(HERE, "schema", "jaunty.schema.json")
OUT = os.path.join(os.path.dirname(HERE), "schema.sqlserver.sql")

# Tables to materialize, in FK-dependency order (parents before children).
TABLE_ORDER = [
    "Region", "Categories", "Suppliers", "Shippers", "CustomerDemographics",
    "Customers", "Employees", "Products", "Territories", "Orders",
    "Order Details", "EmployeeTerritories", "CustomerCustomerDemo",
]
# Which of those carry seed data in canonical instnwnd.sql.
DATA_TABLES = {
    "Categories", "Customers", "Employees", "Products", "Shippers",
    "Suppliers", "Orders", "Order Details", "Region", "Territories",
    "EmployeeTerritories",
}
# Identity tables whose canonical inserts already carry SET IDENTITY_INSERT.
CANONICAL_IDENTITY_WRAPPED = {
    "Categories", "Employees", "Orders", "Products", "Shippers", "Suppliers",
}


def tsql_type(col):
    t = col["dbType"].lower()
    ln = col.get("maxLength")
    if t in ("nvarchar", "nchar", "varchar", "char"):
        return f"{t}({ln})"
    # ntext / image / int / smallint / money / real / bit / datetime carry no length
    return t


def emit_ddl(snapshot):
    tables = snapshot["tables"]
    out = []
    for name in TABLE_ORDER:
        tbl = tables[name]
        cols = tbl["columns"]
        pk = [c for c, meta in cols.items() if meta.get("isPrimaryKey")]
        lines = [f'CREATE TABLE [{name}] (']
        col_lines = []
        for cn, meta in cols.items():
            parts = [f'    [{cn}] {tsql_type(meta)}']
            if meta.get("isIdentity"):
                parts.append("IDENTITY(1,1)")
            parts.append("NOT NULL" if not meta["isNullable"] else "NULL")
            col_lines.append(" ".join(parts))
        if pk:
            pk_cols = ", ".join(f"[{c}]" for c in pk)
            col_lines.append(f'    CONSTRAINT [PK_{name.replace(" ", "_")}] PRIMARY KEY ({pk_cols})')
        lines.append(",\n".join(col_lines))
        lines.append(")")
        out.append("\n".join(lines))
        out.append("GO")

        pk_index_name = f'PK_{name.replace(" ", "_")}'
        for idx in tbl.get("indexes", []):
            if idx["name"] == pk_index_name:
                continue  # already declared as the table's PK constraint above
            unique = "UNIQUE " if idx.get("isUnique") else ""
            idx_cols = ", ".join(f'[{c}]' for c in idx["columns"])
            out.append(f'CREATE {unique}INDEX [{idx["name"]}] ON [{name}] ({idx_cols})')
            out.append("GO")
    return "\n".join(out)


def split_batches(sql):
    batches, cur = [], []
    for line in sql.splitlines():
        if re.match(r"^\s*go\s*$", line, re.IGNORECASE):
            if cur:
                batches.append("\n".join(cur))
                cur = []
        else:
            cur.append(line)
    if cur:
        batches.append("\n".join(cur))
    return batches


def batch_table(batch):
    """Return the target table of a data/identity batch, or None to skip."""
    m = re.search(r'set\s+identity_insert\s+"?([^"\s]+)"?\s+(on|off)', batch, re.IGNORECASE)
    if m:
        return m.group(1)
    m = re.search(r'INSERT\s+(?:INTO\s+)?"([^"]+)"', batch, re.IGNORECASE)
    if m:
        return m.group(1)
    m = re.search(r'Insert\s+Into\s+(\w+)\s+Values', batch, re.IGNORECASE)
    if m:
        return m.group(1)
    return None


# Canonical defines Region/Territories/EmployeeTerritories at the file tail with
# their CREATE TABLE and first inserts sharing one GO-batch, so batch extraction
# would drag in DDL. These three are simple single-line positional inserts, so we
# lift them line-by-line instead.
TAIL_TABLES = {"Region", "Territories", "EmployeeTerritories"}


def collect_data(canonical):
    per_table = {t: [] for t in DATA_TABLES}
    # Early, cleanly GO-separated tables: extract whole insert batches.
    for batch in split_batches(canonical):
        t = batch_table(batch)
        if t in per_table and t not in TAIL_TABLES:
            per_table[t].append(batch.strip())
    # Tail tables: one batch of the matching Insert lines only.
    tail_lines = {t: [] for t in TAIL_TABLES}
    for line in canonical.splitlines():
        m = re.match(r'\s*Insert\s+Into\s+(\w+)\s+Values', line, re.IGNORECASE)
        if m and m.group(1) in tail_lines:
            tail_lines[m.group(1)].append(line.strip())
    for t, lines in tail_lines.items():
        if lines:
            per_table[t].append("\n".join(lines))
    return per_table


def emit_data(per_table):
    out = []
    for name in TABLE_ORDER:
        if name not in DATA_TABLES:
            continue
        batches = per_table.get(name, [])
        if not batches:
            continue
        out.append(f"-- ---- data: {name} ----")
        wrap = name == "Region"  # snapshot makes RegionId an identity; canonical does not wrap it
        if wrap:
            out.append(f"SET IDENTITY_INSERT [{name}] ON")
            out.append("GO")
        for b in batches:
            if wrap:
                # IDENTITY_INSERT requires an explicit column list; canonical Region
                # inserts are positional (RegionId, Description).
                b = re.sub(r"(?i)Insert\s+Into\s+Region\s+Values",
                           "Insert Into [Region] ([RegionId], [Description]) Values", b)
            out.append(b)
            out.append("GO")
        if wrap:
            out.append(f"SET IDENTITY_INSERT [{name}] OFF")
            out.append("GO")
    return "\n".join(out)


def emit_fks(snapshot):
    out = ["-- ---- foreign keys (WITH NOCHECK: relationships only, no load-order coupling) ----"]
    for i, fk in enumerate(snapshot["foreignKeys"]):
        ft, fc = fk["fromTable"], fk["fromColumn"]
        tt, tc = fk["toTable"], fk["toColumn"]
        cname = f'FK_{ft.replace(" ", "_")}_{tt.replace(" ", "_")}_{i}'
        out.append(
            f'ALTER TABLE [{ft}] WITH NOCHECK ADD CONSTRAINT [{cname}] '
            f'FOREIGN KEY ([{fc}]) REFERENCES [{tt}] ([{tc}])'
        )
        out.append("GO")
    return "\n".join(out)


# Views ported from canonical instnwnd.sql to PascalCase-no-spaces names, ordered
# so inter-view dependencies (OrderSubtotals, OrderDetailsExtended,
# ProductSalesFor1997) are created before their dependents.
VIEWS = [
    ("CustomerAndSuppliersByCity", """
SELECT City, CompanyName, ContactName, 'Customers' AS Relationship
FROM Customers
UNION SELECT City, CompanyName, ContactName, 'Suppliers'
FROM Suppliers"""),
    ("AlphabeticalListOfProducts", """
SELECT Products.*, Categories.CategoryName
FROM Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID
WHERE (((Products.Discontinued)=0))"""),
    ("CurrentProductList", """
SELECT Product_List.ProductID, Product_List.ProductName
FROM Products AS Product_List
WHERE (((Product_List.Discontinued)=0))"""),
    ("OrdersQry", """
SELECT Orders.OrderID, Orders.CustomerID, Orders.EmployeeID, Orders.OrderDate, Orders.RequiredDate,
    Orders.ShippedDate, Orders.ShipVia, Orders.Freight, Orders.ShipName, Orders.ShipAddress, Orders.ShipCity,
    Orders.ShipRegion, Orders.ShipPostalCode, Orders.ShipCountry,
    Customers.CompanyName, Customers.Address, Customers.City, Customers.Region, Customers.PostalCode, Customers.Country
FROM Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID"""),
    ("ProductsAboveAveragePrice", """
SELECT Products.ProductName, Products.UnitPrice
FROM Products
WHERE Products.UnitPrice>(SELECT AVG(UnitPrice) From Products)"""),
    ("ProductsByCategory", """
SELECT Categories.CategoryName, Products.ProductName, Products.QuantityPerUnit, Products.UnitsInStock, Products.Discontinued
FROM Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID
WHERE Products.Discontinued <> 1"""),
    ("QuarterlyOrders", """
SELECT DISTINCT Customers.CustomerID, Customers.CompanyName, Customers.City, Customers.Country
FROM Customers RIGHT JOIN Orders ON Customers.CustomerID = Orders.CustomerID
WHERE Orders.OrderDate BETWEEN '19970101' And '19971231'"""),
    ("OrderDetailsExtended", """
SELECT "Order Details".OrderID, "Order Details".ProductID, Products.ProductName,
    "Order Details".UnitPrice, "Order Details".Quantity, "Order Details".Discount,
    (CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS ExtendedPrice
FROM Products INNER JOIN "Order Details" ON Products.ProductID = "Order Details".ProductID"""),
    ("OrderSubtotals", """
SELECT "Order Details".OrderID, Sum(CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS Subtotal
FROM "Order Details"
GROUP BY "Order Details".OrderID"""),
    ("ProductSalesFor1997", """
SELECT Categories.CategoryName, Products.ProductName,
Sum(CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS ProductSales
FROM (Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID)
    INNER JOIN (Orders
        INNER JOIN "Order Details" ON Orders.OrderID = "Order Details".OrderID)
    ON Products.ProductID = "Order Details".ProductID
WHERE (((Orders.ShippedDate) Between '19970101' And '19971231'))
GROUP BY Categories.CategoryName, Products.ProductName"""),
    ("CategorySalesFor1997", """
SELECT ProductSalesFor1997.CategoryName, Sum(ProductSalesFor1997.ProductSales) AS CategorySales
FROM ProductSalesFor1997
GROUP BY ProductSalesFor1997.CategoryName"""),
    ("SalesByCategory", """
SELECT Categories.CategoryID, Categories.CategoryName, Products.ProductName,
    Sum(OrderDetailsExtended.ExtendedPrice) AS ProductSales
FROM Categories INNER JOIN
    (Products INNER JOIN
        (Orders INNER JOIN OrderDetailsExtended ON Orders.OrderID = OrderDetailsExtended.OrderID)
    ON Products.ProductID = OrderDetailsExtended.ProductID)
    ON Categories.CategoryID = Products.CategoryID
WHERE Orders.OrderDate BETWEEN '19970101' And '19971231'
GROUP BY Categories.CategoryID, Categories.CategoryName, Products.ProductName"""),
    ("SalesTotalsByAmount", """
SELECT OrderSubtotals.Subtotal AS SaleAmount, Orders.OrderID, Customers.CompanyName, Orders.ShippedDate
FROM Customers INNER JOIN
    (Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID)
    ON Customers.CustomerID = Orders.CustomerID
WHERE (OrderSubtotals.Subtotal >2500) AND (Orders.ShippedDate BETWEEN '19970101' And '19971231')"""),
    ("SummaryOfSalesByQuarter", """
SELECT Orders.ShippedDate, Orders.OrderID, OrderSubtotals.Subtotal
FROM Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID
WHERE Orders.ShippedDate IS NOT NULL"""),
    ("SummaryOfSalesByYear", """
SELECT Orders.ShippedDate, Orders.OrderID, OrderSubtotals.Subtotal
FROM Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID
WHERE Orders.ShippedDate IS NOT NULL"""),
]


def emit_views():
    out = ["-- ---- views (PascalCase-no-spaces to match db/views/*.sql) ----"]
    for name, body in VIEWS:
        out.append(f"CREATE VIEW [{name}] AS{body}")
        out.append("GO")
    return "\n".join(out)


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: build_sqlserver_seed.py <path-to-instnwnd.sql>")
    canonical = open(sys.argv[1], encoding="utf-8", errors="replace").read()
    snapshot = json.load(open(SNAPSHOT, encoding="utf-8"))

    header = (
        "-- Northwind SQL Server subset: schema + seed for the Testcontainers MsSql\n"
        "-- fixture. DDL is generated from db/schema/jaunty.schema.json (so column\n"
        "-- types match what the generated reader expects). Seed data is reused from\n"
        "-- Microsoft's canonical instnwnd.sql (MIT). Views are ported to the\n"
        "-- PascalCase-no-spaces names this repo's db/views/*.sql queries reference.\n"
        "-- Regenerate with db/build_sqlserver_seed.py. Batches are separated by GO;\n"
        "-- NorthwindFixture splits on GO before executing.\n"
    )
    per_table = collect_data(canonical)
    parts = [
        header,
        "-- ==== schema ====",
        emit_ddl(snapshot),
        "-- ==== data ====",
        emit_data(per_table),
        emit_fks(snapshot),
        "-- ==== views ====",
        emit_views(),
    ]
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(parts) + "\n")

    counts = {t: sum(len(re.findall(r'INSERT|Insert Into', b)) for b in per_table[t]) for t in per_table}
    print("wrote", OUT)
    for t in TABLE_ORDER:
        if t in counts:
            print(f"  {t}: {counts[t]} insert rows")


if __name__ == "__main__":
    main()
