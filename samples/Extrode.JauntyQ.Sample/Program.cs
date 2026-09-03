using Microsoft.Data.Sqlite;
using Extrode.JauntyQ.Generated;

// Create an in-memory SQLite database and seed it
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

SeedDatabase(connection);

// Create the db object — connection is already open, JauntyQ won't close it
var db = new JauntyDb(connection);

// ── Products.GetAll ────────────────────────────────────────
Console.WriteLine("=== All Products ===");
var products = db.Products.GetAll();
foreach (var p in products)
    Console.WriteLine($"  [{p.ProductId}] {p.ProductName} — ${p.UnitPrice} (discontinued: {p.Discontinued})");

// ── Products.GetByCategory ─────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== Products in 'Beverages' (category 1) ===");
var beverages = db.Products.GetByCategory(1);
foreach (var p in beverages)
    Console.WriteLine($"  [{p.ProductId}] {p.ProductName} — ${p.UnitPrice} ({p.CategoryName})");

// ── Employees.GetWithManagers ──────────────────────────────
Console.WriteLine();
Console.WriteLine("=== Employees & Managers ===");
var employees = db.Employees.GetWithManagers();
foreach (var e in employees)
{
    string manager = string.IsNullOrEmpty(e.ManagerFirstName)
        ? "(no manager)"
        : $"{e.ManagerFirstName} {e.ManagerLastName}";
    Console.WriteLine($"  [{e.EmployeeId}] {e.FirstName} {e.LastName} → reports to {manager}");
}

// ── Static fallback (no db object needed) ──────────────────
Console.WriteLine();
Console.WriteLine("=== Static Fallback: All Products ===");
var products2 = Products.GetAll(connection);
foreach (var p in products2)
    Console.WriteLine($"  [{p.ProductId}] {p.ProductName}");

Console.WriteLine();
Console.WriteLine("Done.");

// ── Seed helpers ───────────────────────────────────────────

static void SeedDatabase(SqliteConnection conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE categories (
            category_id   INTEGER PRIMARY KEY,
            category_name TEXT NOT NULL
        );

        CREATE TABLE products (
            product_id   INTEGER PRIMARY KEY,
            product_name TEXT    NOT NULL,
            category_id  INTEGER REFERENCES categories(category_id),
            unit_price   REAL    NOT NULL,
            discontinued INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE employees (
            employee_id INTEGER PRIMARY KEY,
            first_name  TEXT    NOT NULL,
            last_name   TEXT    NOT NULL,
            manager_id  INTEGER REFERENCES employees(employee_id)
        );

        -- Categories
        INSERT INTO categories VALUES (1, 'Beverages');
        INSERT INTO categories VALUES (2, 'Condiments');
        INSERT INTO categories VALUES (3, 'Confections');

        -- Products
        INSERT INTO products VALUES (1, 'Chai',            1, 18.00, 0);
        INSERT INTO products VALUES (2, 'Chang',           1, 19.00, 0);
        INSERT INTO products VALUES (3, 'Aniseed Syrup',   2, 10.00, 0);
        INSERT INTO products VALUES (4, 'Chocolade',       3, 12.75, 1);
        INSERT INTO products VALUES (5, 'Maxilaku',        3, 20.00, 0);

        -- Employees
        INSERT INTO employees VALUES (1, 'Andrew', 'Fuller',    NULL);
        INSERT INTO employees VALUES (2, 'Nancy',  'Davolio',   1);
        INSERT INTO employees VALUES (3, 'Janet',  'Leverling', 1);
        INSERT INTO employees VALUES (4, 'Steven', 'Buchanan',  2);
        """;
    cmd.ExecuteNonQuery();
}
