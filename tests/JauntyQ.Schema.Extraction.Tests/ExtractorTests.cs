using JauntyQ.Schema.Extraction;
using JauntyQ.Schema;
using Microsoft.Data.Sqlite;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

[Trait("Category", "AuditRegression")]
public class ExtractorTests : IDisposable
{
    private readonly string _dbName = $"extractor_test_{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public ExtractorTests()
    {
        _connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE categories (
                category_id INTEGER PRIMARY KEY,
                category_name TEXT NOT NULL
            );

            CREATE TABLE products (
                product_id INTEGER PRIMARY KEY,
                product_name TEXT NOT NULL,
                category_id INTEGER,
                unit_price REAL NOT NULL,
                discontinued INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (category_id) REFERENCES categories(category_id)
            );

            CREATE TABLE employees (
                employee_id INTEGER PRIMARY KEY,
                first_name TEXT NOT NULL,
                last_name TEXT NOT NULL,
                manager_id INTEGER,
                hire_date TEXT NOT NULL,
                FOREIGN KEY (manager_id) REFERENCES employees(employee_id)
            );

            CREATE TABLE orders (
                order_id INTEGER PRIMARY KEY,
                employee_id INTEGER,
                order_date TEXT NOT NULL,
                total REAL NOT NULL,
                FOREIGN KEY (employee_id) REFERENCES employees(employee_id)
            );

            CREATE TABLE order_details (
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                unit_price REAL NOT NULL,
                PRIMARY KEY (order_id, product_id),
                FOREIGN KEY (order_id) REFERENCES orders(order_id),
                FOREIGN KEY (product_id) REFERENCES products(product_id)
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task SqliteExtractor_ExtractsAllTables()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        Assert.Equal(5, schema.Tables.Count);
        Assert.True(schema.Tables.ContainsKey("categories"));
        Assert.True(schema.Tables.ContainsKey("products"));
        Assert.True(schema.Tables.ContainsKey("employees"));
        Assert.True(schema.Tables.ContainsKey("orders"));
        Assert.True(schema.Tables.ContainsKey("order_details"));
    }

    [Fact]
    public async Task SqliteExtractor_ExtractsColumns()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        var products = schema.Tables["products"];
        Assert.Equal(5, products.Columns.Count);
        Assert.True(products.Columns.ContainsKey("product_id"));
        Assert.True(products.Columns.ContainsKey("product_name"));
        Assert.True(products.Columns.ContainsKey("category_id"));
        Assert.True(products.Columns.ContainsKey("unit_price"));
        Assert.True(products.Columns.ContainsKey("discontinued"));
    }

    [Fact]
    public async Task SqliteExtractor_ColumnTypesNormalized()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        var products = schema.Tables["products"];
        Assert.Equal("int", products.Columns["product_id"].DbType);      // INTEGER -> int
        Assert.Equal("varchar", products.Columns["product_name"].DbType); // TEXT -> varchar
        Assert.Equal("float", products.Columns["unit_price"].DbType);     // REAL -> float
    }

    [Fact]
    public async Task SqliteExtractor_NullabilityDetected()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        var products = schema.Tables["products"];
        Assert.False(products.Columns["product_name"].IsNullable);  // NOT NULL
        Assert.True(products.Columns["category_id"].IsNullable);    // nullable FK
    }

    [Fact]
    public async Task SqliteExtractor_ForeignKeysExtracted()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        // products.category_id -> categories.category_id
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "products" && fk.FromColumn == "category_id" &&
            fk.ToTable == "categories" && fk.ToColumn == "category_id");

        // employees.manager_id -> employees.employee_id (self-join)
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "employees" && fk.FromColumn == "manager_id" &&
            fk.ToTable == "employees" && fk.ToColumn == "employee_id");

        // orders.employee_id -> employees.employee_id
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "orders" && fk.FromColumn == "employee_id" &&
            fk.ToTable == "employees" && fk.ToColumn == "employee_id");

        // order_details has 2 FKs
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "order_details" && fk.FromColumn == "order_id" &&
            fk.ToTable == "orders" && fk.ToColumn == "order_id");
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "order_details" && fk.FromColumn == "product_id" &&
            fk.ToTable == "products" && fk.ToColumn == "product_id");
    }

    [Fact]
    public async Task SqliteExtractor_SelfJoinForeignKey()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        var selfFk = schema.ForeignKeys.FirstOrDefault(fk =>
            fk.FromTable == "employees" && fk.ToTable == "employees");

        Assert.NotNull(selfFk);
        Assert.Equal("manager_id", selfFk.FromColumn);
        Assert.Equal("employee_id", selfFk.ToColumn);
    }

    [Fact]
    public async Task SqliteExtractor_SchemaRoundTrips()
    {
        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(_connectionString);

        var json = SchemaLoader.Serialize(schema);
        var roundTrip = SchemaLoader.Load(json);

        Assert.Equal(schema.Tables.Count, roundTrip.Tables.Count);
        Assert.Equal(schema.ForeignKeys.Count, roundTrip.ForeignKeys.Count);

        foreach (var tableName in schema.Tables.Keys)
        {
            Assert.True(roundTrip.Tables.ContainsKey(tableName));
            Assert.Equal(
                schema.Tables[tableName].Columns.Count,
                roundTrip.Tables[tableName].Columns.Count);
        }
    }

    [Fact]
    public async Task SqliteExtractor_EmptyDatabase_NoTables()
    {
        var emptyDbName = $"empty_test_{Guid.NewGuid():N}";
        var emptyConnStr = $"Data Source={emptyDbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(emptyConnStr);
        await keepAlive.OpenAsync();

        var extractor = new SqliteExtractor();
        var schema = await extractor.ExtractAsync(emptyConnStr);

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.ForeignKeys);
    }

    [Fact]
    public async Task SqliteExtractor_PartialUniqueIndex_IsNotReportedAsGloballyUnique()
    {
        // PRAGMA index_list's 5th column ("partial") is 1 for a partial index
        // (CREATE UNIQUE INDEX ... WHERE ...), which only enforces uniqueness
        // among the rows matching its WHERE clause, not the whole table. The
        // old code read the "unique" column but never the "partial" one.
        var dbName = $"sqlite_partial_index_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                create table users (id integer primary key, email text, deleted_at text);
                create unique index ux_users_email_active on users(email) where deleted_at is null;
                create table plain_users (id integer primary key, email text not null);
                create unique index ux_plain_users_email on plain_users(email);";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);

        var partial = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_email_active");
        Assert.False(partial.IsUnique);
        Assert.Equal(new[] { "email" }, partial.Columns);

        var plain = Assert.Single(schema.Tables["plain_users"].Indexes, i => i.Name == "ux_plain_users_email");
        Assert.True(plain.IsUnique);
        Assert.Equal(new[] { "email" }, plain.Columns);
    }

    [Fact]
    public async Task SqliteExtractor_CompositeExpressionIndex_IsRepresentedFlagged_NotTruncated()
    {
        // index_info reports a NULL column name for an expression key part
        // (e.g. ON t (customer_id, lower(email))). The original bug truncated
        // such an index to cols=[customer_id] while still reporting it
        // IsUnique=true -- wrong and dangerous, since customer_id alone is
        // never unique by itself. The first fix excluded the whole index,
        // which left a UNIQUE expression index invisible as a competing
        // constraint; since 2026-07-30 it is captured with
        // HasExpressionKeyPart set and Columns holding the real-column
        // subset, and must never appear as an UNFLAGGED unique singleton.
        var dbName = $"sqlite_expr_index_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                create table users (id integer primary key, email text, customer_id integer not null);
                create unique index ux_users_customer_lower_email on users(customer_id, lower(email));
                create unique index ux_users_lower_email_only on users(lower(email));";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);

        var composite = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_customer_lower_email");
        Assert.True(composite.HasExpressionKeyPart);
        Assert.True(composite.IsUnique);
        Assert.Equal(new[] { "customer_id" }, composite.Columns);

        // ALL-expression unique index: no column rows at all, but the entry
        // must exist (flagged, empty Columns) so it stays visible as a
        // competing constraint.
        var exprOnly = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_lower_email_only");
        Assert.True(exprOnly.HasExpressionKeyPart);
        Assert.True(exprOnly.IsUnique);
        Assert.Empty(exprOnly.Columns);

        // The original truncation shape stays dead: never an unflagged unique
        // singleton on customer_id.
        Assert.DoesNotContain(schema.Tables["users"].Indexes,
            i => i.Columns.Count == 1 && i.Columns[0] == "customer_id" && i.IsUnique && !i.HasExpressionKeyPart);
    }

    [Fact]
    public async Task SqliteExtractor_IntegerPrimaryKey_IsIdentity()
    {
        // Only a column declared exactly "INTEGER" (case-insensitive, no
        // facet) aliases the rowid and gets auto-assigned values. Confirmed
        // live (sqlite3 CLI): inserting a row without specifying the column
        // auto-assigns 1, 2, 3, ... for "id INTEGER PRIMARY KEY".
        var dbName = $"sqlite_pk_integer_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "create table widgets (id INTEGER primary key, name text);";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);
        var col = schema.Tables["widgets"].Columns["id"];

        Assert.Equal("int", col.DbType);
        Assert.True(col.IsIdentity);
    }

    [Theory]
    [InlineData("INT")]
    [InlineData("MEDIUMINT")]
    public async Task SqliteExtractor_NonLiteralIntegerPrimaryKey_IsNotIdentity(string declaredType)
    {
        // NormalizeSqliteType collapses INT/MEDIUMINT/etc. down to the same
        // DbType "int" as INTEGER (safe -- Int32 is a superset of their
        // declared range, and they share INTEGER storage affinity per
        // SQLite's own type-affinity rules), but only the literal declared
        // type "INTEGER" makes SQLite alias the column to the rowid.
        // Confirmed live (sqlite3 CLI): "id INT PRIMARY KEY" inserts NULL for
        // an omitted id -- no auto-assignment at all. Reporting
        // IsIdentity=true here (the old behavior) made AutoCrud drop the
        // column from generated INSERT statements entirely, silently
        // inserting NULL into it every time.
        var dbName = $"sqlite_pk_{declaredType}_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = $"create table widgets (id {declaredType} primary key, name text);";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);
        var col = schema.Tables["widgets"].Columns["id"];

        Assert.Equal("int", col.DbType);
        Assert.False(col.IsIdentity);
    }

    [Fact]
    public async Task SqliteExtractor_Bigint_KeptDistinctFromInt_NotIdentity()
    {
        // BIGINT is the opposite direction from INT/MEDIUMINT/TINYINT above:
        // it declares a WIDER range than "int" (System.Int32), not a
        // narrower one, so collapsing it down to "int" isn't safe the way
        // widening a SMALLINT up to "int" is -- DialectMapper maps "bigint"
        // to System.Int64 but "int" to System.Int32, so a BIGINT column that
        // legitimately stores a value outside Int32's range used to throw
        // InvalidCastException/overflow off a generated Int32 reader call.
        // Also confirms it still correctly does NOT alias to the rowid
        // (only the literal declared type "INTEGER" does that).
        var dbName = $"sqlite_pk_bigint_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "create table widgets (id BIGINT primary key, name text);";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);
        var col = schema.Tables["widgets"].Columns["id"];

        Assert.Equal("bigint", col.DbType);
        Assert.False(col.IsIdentity);
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("virtual")]
    public async Task SqliteExtractor_GeneratedColumn_IsComputed(string mode)
    {
        // PRAGMA table_info doesn't just fail to flag a generated column as
        // computed -- confirmed live (sqlite3 CLI), it OMITS the column from
        // its result set entirely (both STORED and VIRTUAL). table_xinfo's
        // extra "hidden" column (2=VIRTUAL, 3=STORED) is required to see the
        // column at all, not merely to classify it. Before this fix, a live
        // schema pull against a real GENERATED ALWAYS AS column silently
        // dropped it from the effective schema altogether (same failure
        // class as the MigrationParser computed-column drop this round).
        var dbName = $"sqlite_generated_{mode}_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = $"create table orders (id integer primary key, qty int, price real, total real generated always as (qty*price) {mode});";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);
        var total = schema.Tables["orders"].Columns["total"];
        var qty = schema.Tables["orders"].Columns["qty"];

        Assert.True(total.IsComputed, $"expected IsComputed=true for GENERATED ALWAYS AS (...) {mode}");
        Assert.False(qty.IsComputed);
    }

    [Fact]
    public void PostgresExtractor_ResolveDbType_UserDefinedUsesUdtName()
    {
        // information_schema reports data_type 'USER-DEFINED' for citext; the real
        // type name lives in udt_name and must be the recorded dbType.
        Assert.Equal("citext", PostgresExtractor.ResolveDbType("USER-DEFINED", "citext"));
    }

    [Fact]
    public void PostgresExtractor_ResolveDbType_StandardTypeKeepsDataType()
    {
        Assert.Equal("character varying", PostgresExtractor.ResolveDbType("character varying", "varchar"));
        Assert.Equal("integer", PostgresExtractor.ResolveDbType("integer", "int4"));
    }

    [Fact]
    public void PostgresExtractor_ResolveDbType_ArrayUsesUnderscoreStrippedUdtNamePlusBrackets()
    {
        // information_schema reports data_type 'ARRAY' for every array column
        // (never the element type); the real shape lives in udt_name as an
        // underscore-prefixed internal type name (confirmed live: text[] ->
        // "_text", integer[] -> "_int4"). Without stripping the underscore and
        // appending "[]", the recorded DbType was the literal string "ARRAY",
        // which DialectMapper.MapDbTypeToCSharp doesn't recognize (falls to
        // "object") and which never triggers its own "[]"-suffixed
        // array-unwrapping branch.
        Assert.Equal("text[]", PostgresExtractor.ResolveDbType("ARRAY", "_text"));
        Assert.Equal("int4[]", PostgresExtractor.ResolveDbType("ARRAY", "_int4"));
    }

    [Fact]
    public async Task SqliteExtractor_MaxLengthPrecisionScaleAndIsUnicode_ParsedFromDeclaredType()
    {
        // §2.7 extractor parity sweep ("remaining facet x extractor cells"):
        // SqliteExtractor has no length/precision/scale catalog at all (SQLite
        // has none) -- it parses the "(a)"/"(a,b)" facet straight out of the
        // declared type text via ParseDeclaredNumbers. None of MaxLength,
        // Precision, Scale, or IsUnicode had an explicit assertion anywhere in
        // this suite before now, despite being read (structurally correct) in
        // this round.
        var dbName = $"sqlite_facets_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        await keepAlive.OpenAsync();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                create table widgets (
                    id integer primary key,
                    code varchar(40),
                    price decimal(10,2),
                    blob_data blob,
                    qty int
                );";
            cmd.ExecuteNonQuery();
        }

        var schema = await new SqliteExtractor().ExtractAsync(connStr);
        var widgets = schema.Tables["widgets"];

        Assert.Equal(40, widgets.Columns["code"].MaxLength);
        Assert.True(widgets.Columns["code"].IsUnicode);

        Assert.Equal(10, widgets.Columns["price"].Precision);
        Assert.Equal(2, widgets.Columns["price"].Scale);

        // Non-text/non-decimal columns leave these facets null, not a
        // misleading default value.
        Assert.Null(widgets.Columns["blob_data"].MaxLength);
        Assert.Null(widgets.Columns["qty"].Precision);
        Assert.Null(widgets.Columns["qty"].IsUnicode);

        // SqliteExtractor never computes IsRowVersion at all (a SQL-Server-only
        // concept per ColumnSchema.IsRowVersion's own doc comment) -- closes
        // that facet's Sqlite cell as a proven N/A, completing the IsRowVersion
        // row across all 4 extractors (SqlServer: true positive; Postgres:
        // confirmed no name-collision false positive; MySql/Sqlite: confirmed
        // N/A defaults).
        Assert.False(widgets.Columns["qty"].IsRowVersion);
    }

    [Fact]
    public async Task MySqlExtractor_NoDatabaseInConnectionString_FailsFast()
    {
        // MySQL happily connects with no database selected, and every
        // INFORMATION_SCHEMA filter then matches nothing — a silently EMPTY
        // snapshot that looks like a successful pull. The guard runs before
        // any connection attempt, so no server is needed here.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MySqlExtractor().ExtractAsync("Server=localhost;User ID=root"));
        Assert.Contains("Database=", ex.Message);
    }
}
