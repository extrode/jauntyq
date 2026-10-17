#!/usr/bin/env kiln
// Generates schema.sqlserver.sql for the Northwind Testcontainers fixture.
//
// DDL is emitted from the committed generator snapshot (jaunty.schema.json) so the
// live SQL Server exactly matches the types the generated reader expects (e.g.
// EmployeeId is smallint, not canonical Northwind's int). Seed data is reused
// verbatim from Microsoft's canonical instnwnd.sql (MIT), split on GO batches and
// kept only for the tables we load. Views are ported from the canonical bodies to
// the PascalCase-no-spaces names this repo's db/views/*.sql queries reference.
//
// Canonical source (MIT), download once:
//     https://raw.githubusercontent.com/microsoft/sql-server-samples/master/samples/databases/northwind-pubs/instnwnd.sql
//
// Run from anywhere:
//     kiln samples/Extrode.JauntyQ.Northwind.Tests/db/build-sqlserver-seed.cs <path-to-instnwnd.sql>
//
// Ported from build_sqlserver_seed.py with the same output byte for byte, including on inputs the
// real files never contain: the Python text and regex rules it relied on are spelled out in Py below.
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: build-sqlserver-seed.cs <path-to-instnwnd.sql>");
    return 1;
}
try
{
    return Seed.Run(args[0]);
}
catch (Exception e)
{
    // Python's traceback exit: a missing file or a schema of the wrong shape ends the run with 1.
    Console.Error.WriteLine($"build-sqlserver-seed: {e.GetType().Name}: {e.Message}");
    return 1;
}

static partial class Seed
{
    static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    static readonly string Snapshot = Path.Combine(Here(), "schema", "jaunty.schema.json");
    static readonly string Out = Path.Combine(Path.GetDirectoryName(Here())!, "schema.sqlserver.sql");

    // Tables to materialize, in FK-dependency order (parents before children).
    static readonly string[] TableOrder =
    [
        "Region", "Categories", "Suppliers", "Shippers", "CustomerDemographics",
        "Customers", "Employees", "Products", "Territories", "Orders",
        "Order Details", "EmployeeTerritories", "CustomerCustomerDemo",
    ];
    // Which of those carry seed data in canonical instnwnd.sql.
    static readonly HashSet<string> DataTables =
    [
        "Categories", "Customers", "Employees", "Products", "Shippers",
        "Suppliers", "Orders", "Order Details", "Region", "Territories",
        "EmployeeTerritories",
    ];

    static string TsqlType(object? col)
    {
        var t = Py.Lower(Py.Str(Py.Index(col, "dbType"), strict: true));
        var ln = Py.Get(col, "maxLength");
        if (t is "nvarchar" or "nchar" or "varchar" or "char")
            return $"{t}({Py.Str(ln)})";
        // ntext / image / int / smallint / money / real / bit / datetime carry no length
        return t;
    }

    static string EmitDdl(object? snapshot)
    {
        var tables = Py.Index(snapshot, "tables");
        var output = new List<string>();
        foreach (var name in TableOrder)
        {
            var tbl = Py.Index(tables, name);
            var cols = Py.Items(Py.Index(tbl, "columns"));
            var pk = cols.Where(c => Py.Truthy(Py.Get(c.Value, "isPrimaryKey"))).Select(c => c.Key).ToList();
            var lines = new List<string> { $"CREATE TABLE [{name}] (" };
            var colLines = new List<string>();
            foreach (var (cn, meta) in cols)
            {
                var parts = new List<string> { $"    [{cn}] {TsqlType(meta)}" };
                if (Py.Truthy(Py.Get(meta, "isIdentity")))
                    parts.Add("IDENTITY(1,1)");
                parts.Add(!Py.Truthy(Py.Index(meta, "isNullable")) ? "NOT NULL" : "NULL");
                colLines.Add(string.Join(" ", parts));
            }
            if (pk.Count > 0)
            {
                var pkCols = string.Join(", ", pk.Select(c => $"[{c}]"));
                colLines.Add($"    CONSTRAINT [PK_{name.Replace(" ", "_")}] PRIMARY KEY ({pkCols})");
            }
            lines.Add(string.Join(",\n", colLines));
            lines.Add(")");
            output.Add(string.Join("\n", lines));
            output.Add("GO");

            var pkIndexName = $"PK_{name.Replace(" ", "_")}";
            foreach (var idx in Py.Iterate(Py.Get(tbl, "indexes", new List<object?>())))
            {
                if (Py.Index(idx, "name") is string s && s == pkIndexName)
                    continue; // already declared as the table's PK constraint above
                var unique = Py.Truthy(Py.Get(idx, "isUnique")) ? "UNIQUE " : "";
                var idxCols = string.Join(", ", Py.Iterate(Py.Index(idx, "columns")).Select(c => $"[{Py.Str(c)}]"));
                output.Add($"CREATE {unique}INDEX [{Py.Str(Py.Index(idx, "name"))}] ON [{name}] ({idxCols})");
                output.Add("GO");
            }
        }
        return string.Join("\n", output);
    }

    // re.IGNORECASE on these literals: Python also folds ı/İ to i and ſ to s.
    const string Go = "[gG][oO]";
    const string Set = "[sS\u017f][eE][tT]";
    const string IdentityInsertWord = "[iI\u0131\u0130][dD][eE][nN][tT][iI\u0131\u0130][tT][yY]_[iI\u0131\u0130][nN][sS\u017f][eE][rR][tT]";
    const string On = "[oO][nN]";
    const string Off = "[oO][fF][fF]";
    const string Insert = "[iI\u0131\u0130][nN][sS\u017f][eE][rR][tT]";
    const string Into = "[iI\u0131\u0130][nN][tT][oO]";
    const string Values = "[vV][aA][lL][uU][eE][sS\u017f]";
    const string Region = "[rR][eE][gG][iI\u0131\u0130][oO][nN]";
    const string S = Py.Space;

    [GeneratedRegex("^" + S + "*" + Go + S + "*$", RegexOptions.CultureInvariant)]
    private static partial Regex GoLine { get; }

    static List<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        var cur = new List<string>();
        foreach (var line in Py.SplitLines(sql))
        {
            if (GoLine.IsMatch(line))
            {
                if (cur.Count > 0)
                {
                    batches.Add(string.Join("\n", cur));
                    cur = [];
                }
            }
            else
                cur.Add(line);
        }
        if (cur.Count > 0)
            batches.Add(string.Join("\n", cur));
        return batches;
    }

    [GeneratedRegex(Set + S + "+" + IdentityInsertWord + S + "+\"?(" + Py.NotQuoteOrSpace + "+)\"?" + S + "+(" + On + "|" + Off + ")", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityInsert { get; }
    [GeneratedRegex(Insert + S + "+(?:" + Into + S + "+)?\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedInsert { get; }
    [GeneratedRegex(Insert + S + "+" + Into + S + "+(" + Py.Word + "+)" + S + "+" + Values, RegexOptions.CultureInvariant)]
    private static partial Regex PlainInsert { get; }
    [GeneratedRegex("^" + S + "*" + Insert + S + "+" + Into + S + "+(" + Py.Word + "+)" + S + "+" + Values, RegexOptions.CultureInvariant)]
    private static partial Regex TailInsert { get; }

    // The target table of a data/identity batch, or null to skip.
    static string? BatchTable(string batch)
    {
        foreach (var re in new[] { IdentityInsert, QuotedInsert, PlainInsert })
            if (re.Match(batch) is { Success: true } m)
                return m.Groups[1].Value;
        return null;
    }

    // Canonical defines Region/Territories/EmployeeTerritories at the file tail with
    // their CREATE TABLE and first inserts sharing one GO-batch, so batch extraction
    // would drag in DDL. These three are simple single-line positional inserts, so we
    // lift them line-by-line instead.
    static readonly string[] TailTables = ["Region", "Territories", "EmployeeTerritories"];

    static Dictionary<string, List<string>> CollectData(string canonical)
    {
        var perTable = DataTables.ToDictionary(t => t, _ => new List<string>());
        // Early, cleanly GO-separated tables: extract whole insert batches.
        foreach (var batch in SplitBatches(canonical))
            if (BatchTable(batch) is { } t && perTable.ContainsKey(t) && !TailTables.Contains(t))
                perTable[t].Add(Py.Strip(batch));
        // Tail tables: one batch of the matching Insert lines only.
        var tailLines = TailTables.ToDictionary(t => t, _ => new List<string>());
        foreach (var line in Py.SplitLines(canonical))
            if (TailInsert.Match(line) is { Success: true } m && tailLines.TryGetValue(m.Groups[1].Value, out var list))
                list.Add(Py.Strip(line));
        foreach (var (t, lines) in tailLines)
            if (lines.Count > 0)
                perTable[t].Add(string.Join("\n", lines));
        return perTable;
    }

    [GeneratedRegex(Insert + S + "+" + Into + S + "+" + Region + S + "+" + Values, RegexOptions.CultureInvariant)]
    private static partial Regex RegionInsert { get; }

    [GeneratedRegex("INSERT|Insert Into", RegexOptions.CultureInvariant)]
    private static partial Regex InsertCount { get; }

    static string EmitData(Dictionary<string, List<string>> perTable)
    {
        var output = new List<string>();
        foreach (var name in TableOrder)
        {
            if (!DataTables.Contains(name))
                continue;
            var batches = perTable.GetValueOrDefault(name) ?? [];
            if (batches.Count == 0)
                continue;
            output.Add($"-- ---- data: {name} ----");
            var wrap = name == "Region"; // snapshot makes RegionId an identity; canonical does not wrap it
            if (wrap)
            {
                output.Add($"SET IDENTITY_INSERT [{name}] ON");
                output.Add("GO");
            }
            foreach (var batch in batches)
            {
                var b = batch;
                if (wrap)
                    // IDENTITY_INSERT requires an explicit column list; canonical Region
                    // inserts are positional (RegionId, Description).
                    b = RegionInsert.Replace(b, "Insert Into [Region] ([RegionId], [Description]) Values");
                output.Add(b);
                output.Add("GO");
            }
            if (wrap)
            {
                output.Add($"SET IDENTITY_INSERT [{name}] OFF");
                output.Add("GO");
            }
        }
        return string.Join("\n", output);
    }

    static string EmitFks(object? snapshot)
    {
        var output = new List<string> { "-- ---- foreign keys (WITH NOCHECK: relationships only, no load-order coupling) ----" };
        var i = 0;
        foreach (var fk in Py.Iterate(Py.Index(snapshot, "foreignKeys")))
        {
            var (ft, fc) = (Py.Index(fk, "fromTable"), Py.Index(fk, "fromColumn"));
            var (tt, tc) = (Py.Index(fk, "toTable"), Py.Index(fk, "toColumn"));
            var cname = $"FK_{Py.Str(ft, strict: true).Replace(" ", "_")}_{Py.Str(tt, strict: true).Replace(" ", "_")}_{i}";
            output.Add($"ALTER TABLE [{Py.Str(ft)}] WITH NOCHECK ADD CONSTRAINT [{cname}] FOREIGN KEY ([{Py.Str(fc)}]) REFERENCES [{Py.Str(tt)}] ([{Py.Str(tc)}])");
            output.Add("GO");
            i++;
        }
        return string.Join("\n", output);
    }

    // Views ported from canonical instnwnd.sql to PascalCase-no-spaces names, ordered
    // so inter-view dependencies (OrderSubtotals, OrderDetailsExtended,
    // ProductSalesFor1997) are created before their dependents.
    static readonly (string Name, string Body)[] Views =
    [
        ("CustomerAndSuppliersByCity", """

            SELECT City, CompanyName, ContactName, 'Customers' AS Relationship
            FROM Customers
            UNION SELECT City, CompanyName, ContactName, 'Suppliers'
            FROM Suppliers
            """),
        ("AlphabeticalListOfProducts", """

            SELECT Products.*, Categories.CategoryName
            FROM Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID
            WHERE (((Products.Discontinued)=0))
            """),
        ("CurrentProductList", """

            SELECT Product_List.ProductID, Product_List.ProductName
            FROM Products AS Product_List
            WHERE (((Product_List.Discontinued)=0))
            """),
        ("OrdersQry", """

            SELECT Orders.OrderID, Orders.CustomerID, Orders.EmployeeID, Orders.OrderDate, Orders.RequiredDate,
                Orders.ShippedDate, Orders.ShipVia, Orders.Freight, Orders.ShipName, Orders.ShipAddress, Orders.ShipCity,
                Orders.ShipRegion, Orders.ShipPostalCode, Orders.ShipCountry,
                Customers.CompanyName, Customers.Address, Customers.City, Customers.Region, Customers.PostalCode, Customers.Country
            FROM Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID
            """),
        ("ProductsAboveAveragePrice", """

            SELECT Products.ProductName, Products.UnitPrice
            FROM Products
            WHERE Products.UnitPrice>(SELECT AVG(UnitPrice) From Products)
            """),
        ("ProductsByCategory", """

            SELECT Categories.CategoryName, Products.ProductName, Products.QuantityPerUnit, Products.UnitsInStock, Products.Discontinued
            FROM Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID
            WHERE Products.Discontinued <> 1
            """),
        ("QuarterlyOrders", """

            SELECT DISTINCT Customers.CustomerID, Customers.CompanyName, Customers.City, Customers.Country
            FROM Customers RIGHT JOIN Orders ON Customers.CustomerID = Orders.CustomerID
            WHERE Orders.OrderDate BETWEEN '19970101' And '19971231'
            """),
        ("OrderDetailsExtended", """

            SELECT "Order Details".OrderID, "Order Details".ProductID, Products.ProductName,
                "Order Details".UnitPrice, "Order Details".Quantity, "Order Details".Discount,
                (CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS ExtendedPrice
            FROM Products INNER JOIN "Order Details" ON Products.ProductID = "Order Details".ProductID
            """),
        ("OrderSubtotals", """

            SELECT "Order Details".OrderID, Sum(CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS Subtotal
            FROM "Order Details"
            GROUP BY "Order Details".OrderID
            """),
        ("ProductSalesFor1997", """

            SELECT Categories.CategoryName, Products.ProductName,
            Sum(CONVERT(money,("Order Details".UnitPrice*Quantity*(1-Discount)/100))*100) AS ProductSales
            FROM (Categories INNER JOIN Products ON Categories.CategoryID = Products.CategoryID)
                INNER JOIN (Orders
                    INNER JOIN "Order Details" ON Orders.OrderID = "Order Details".OrderID)
                ON Products.ProductID = "Order Details".ProductID
            WHERE (((Orders.ShippedDate) Between '19970101' And '19971231'))
            GROUP BY Categories.CategoryName, Products.ProductName
            """),
        ("CategorySalesFor1997", """

            SELECT ProductSalesFor1997.CategoryName, Sum(ProductSalesFor1997.ProductSales) AS CategorySales
            FROM ProductSalesFor1997
            GROUP BY ProductSalesFor1997.CategoryName
            """),
        ("SalesByCategory", """

            SELECT Categories.CategoryID, Categories.CategoryName, Products.ProductName,
                Sum(OrderDetailsExtended.ExtendedPrice) AS ProductSales
            FROM Categories INNER JOIN
                (Products INNER JOIN
                    (Orders INNER JOIN OrderDetailsExtended ON Orders.OrderID = OrderDetailsExtended.OrderID)
                ON Products.ProductID = OrderDetailsExtended.ProductID)
                ON Categories.CategoryID = Products.CategoryID
            WHERE Orders.OrderDate BETWEEN '19970101' And '19971231'
            GROUP BY Categories.CategoryID, Categories.CategoryName, Products.ProductName
            """),
        ("SalesTotalsByAmount", """

            SELECT OrderSubtotals.Subtotal AS SaleAmount, Orders.OrderID, Customers.CompanyName, Orders.ShippedDate
            FROM Customers INNER JOIN
                (Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID)
                ON Customers.CustomerID = Orders.CustomerID
            WHERE (OrderSubtotals.Subtotal >2500) AND (Orders.ShippedDate BETWEEN '19970101' And '19971231')
            """),
        ("SummaryOfSalesByQuarter", """

            SELECT Orders.ShippedDate, Orders.OrderID, OrderSubtotals.Subtotal
            FROM Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID
            WHERE Orders.ShippedDate IS NOT NULL
            """),
        ("SummaryOfSalesByYear", """

            SELECT Orders.ShippedDate, Orders.OrderID, OrderSubtotals.Subtotal
            FROM Orders INNER JOIN OrderSubtotals ON Orders.OrderID = OrderSubtotals.OrderID
            WHERE Orders.ShippedDate IS NOT NULL
            """),
    ];

    static string EmitViews()
    {
        var output = new List<string> { "-- ---- views (PascalCase-no-spaces to match db/views/*.sql) ----" };
        foreach (var (name, body) in Views)
        {
            // Raw literals end lines with the source file's newline; the views are emitted with \n.
            output.Add($"CREATE VIEW [{name}] AS{body.ReplaceLineEndings("\n")}");
            output.Add("GO");
        }
        return string.Join("\n", output);
    }

    internal static int Run(string canonicalPath)
    {
        var canonical = Py.ReadText(canonicalPath, strict: false);
        var snapshot = Py.LoadJson(Py.ReadText(Snapshot, strict: true));

        const string header =
            "-- Northwind SQL Server subset: schema + seed for the Testcontainers MsSql\n" +
            "-- fixture. DDL is generated from db/schema/jaunty.schema.json (so column\n" +
            "-- types match what the generated reader expects). Seed data is reused from\n" +
            "-- Microsoft's canonical instnwnd.sql (MIT). Views are ported to the\n" +
            "-- PascalCase-no-spaces names this repo's db/views/*.sql queries reference.\n" +
            "-- Regenerate with db/build_sqlserver_seed.py. Batches are separated by GO;\n" +
            "-- NorthwindFixture splits on GO before executing.\n";
        var perTable = CollectData(canonical);
        string[] parts =
        [
            header,
            "-- ==== schema ====",
            EmitDdl(snapshot),
            "-- ==== data ====",
            EmitData(perTable),
            EmitFks(snapshot),
            "-- ==== views ====",
            EmitViews(),
        ];
        Py.WriteText(Out, string.Join("\n", parts) + "\n");

        var counts = perTable.ToDictionary(p => p.Key, p => p.Value.Sum(b => InsertCount.Count(b)));
        Console.WriteLine($"wrote {Out}");
        foreach (var t in TableOrder)
            if (counts.TryGetValue(t, out var n))
                Console.WriteLine($"  {t}: {n} insert rows");
        return 0;
    }
}

// The Python 3.12 behaviour build_sqlserver_seed.py relied on, so the port matches it on any input.
static class Py
{
    // str.isspace(): what \s and strip() mean in Python.
    const string SpaceChars = @"\t\n\v\f\r\x1c-\x1f \x85\xa0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000";
    internal const string Space = "[" + SpaceChars + "]";
    internal const string NotQuoteOrSpace = "[^\"" + SpaceChars + "]";
    // str.isalnum() or "_": what \w means in Python (letters and all numbers, no combining marks).
    internal const string Word = @"[\p{L}\p{N}_]";

    static bool IsSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\x85' or '\xa0' or '\u1680'
        or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' || c is >= '\x1c' and <= '\x1f' || c is >= '\u2000' and <= '\u200a';

    internal static string Strip(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsSpace(s[a])) a++;
        while (b > a && IsSpace(s[b - 1])) b--;
        return s[a..b];
    }

    // str.splitlines(): every line boundary Python knows, a trailing one making no empty line.
    internal static List<string> SplitLines(string s)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '\n' or '\v' or '\f' or '\x1c' or '\x1d' or '\x1e' or '\x85' or '\u2028' or '\u2029' or '\r')
            {
                lines.Add(s[start..i]);
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
        }
        if (start < s.Length)
            lines.Add(s[start..]);
        return lines;
    }

    // open(path, encoding="utf-8"[, errors="replace"]).read(): a BOM stays as U+FEFF, newlines become \n.
    internal static string ReadText(string path, bool strict) =>
        new UTF8Encoding(false, throwOnInvalidBytes: strict).GetString(File.ReadAllBytes(path))
            .Replace("\r\n", "\n").Replace('\r', '\n');

    // open(path, "w", encoding="utf-8").write(text): \n becomes the platform's newline.
    internal static void WriteText(string path, string text) =>
        File.WriteAllText(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));

    // str.lower(): .NET's simple mapping plus Python's two extras, İ -> i̇ and a word-final Σ -> ς.
    internal static string Lower(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\u0130')
                sb.Append("i\u0307");
            else if (c == '\u03a3' && i > 0 && char.IsLetter(s[i - 1]) && !(i + 1 < s.Length && char.IsLetter(s[i + 1])))
                sb.Append('\u03c2');
            else
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // json.load into Python values: dict (ordered, a repeated key keeps its first place and last
    // value), list, str, int, float, bool, None.
    internal static object? LoadJson(string text)
    {
        // Python stops at its recursion limit, not at JsonDocument's default depth of 64.
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 990 });
        return Convert(doc.RootElement);
    }

    static object? Convert(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => ToDict(e),
        JsonValueKind.Array => e.EnumerateArray().Select(Convert).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText() is var raw && raw.IndexOfAny(['.', 'e', 'E']) < 0
            ? BigInteger.Parse(raw, CultureInfo.InvariantCulture)
            : double.Parse(raw, CultureInfo.InvariantCulture),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    static List<KeyValuePair<string, object?>> ToDict(JsonElement e)
    {
        var items = new List<KeyValuePair<string, object?>>();
        var at = new Dictionary<string, int>();
        foreach (var p in e.EnumerateObject())
        {
            var v = Convert(p.Value);
            if (at.TryGetValue(p.Name, out var i))
                items[i] = new(p.Name, v);
            else
            {
                at[p.Name] = items.Count;
                items.Add(new(p.Name, v));
            }
        }
        return items;
    }

    // d[key] on a dict; anything else is Python's TypeError or KeyError.
    internal static object? Index(object? d, string key) =>
        d is List<KeyValuePair<string, object?>> items
            ? items.FindIndex(p => p.Key == key) is var i and >= 0 ? items[i].Value : throw new KeyNotFoundException(key)
            : throw new InvalidOperationException($"cannot index {TypeName(d)} with '{key}'");

    // d.get(key, fallback): only a dict has .get.
    internal static object? Get(object? d, string key, object? fallback = null) =>
        d is List<KeyValuePair<string, object?>> items
            ? items.FindIndex(p => p.Key == key) is var i and >= 0 ? items[i].Value : fallback
            : throw new InvalidOperationException($"{TypeName(d)} has no get");

    internal static List<KeyValuePair<string, object?>> Items(object? d) =>
        d as List<KeyValuePair<string, object?>> ?? throw new InvalidOperationException($"{TypeName(d)} has no items");

    // for x in v: a list's items, a dict's keys, a str's characters.
    internal static IEnumerable<object?> Iterate(object? v) => v switch
    {
        List<KeyValuePair<string, object?>> items => items.Select(p => (object?)p.Key),
        List<object?> list => list,
        string s => s.EnumerateRunes().Select(r => (object?)r.ToString()),
        _ => throw new InvalidOperationException($"{TypeName(v)} is not iterable"),
    };

    internal static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        BigInteger n => !n.IsZero,
        double d => d != 0,
        string s => s.Length > 0,
        List<object?> l => l.Count > 0,
        List<KeyValuePair<string, object?>> items => items.Count > 0,
        _ => true,
    };

    // str(v) as an f-string prints it. strict: the caller calls a str method on it, so it must be a str.
    internal static string Str(object? v, bool strict = false) => v switch
    {
        string s => s,
        _ when strict => throw new InvalidOperationException($"{TypeName(v)} is not a str"),
        null => "None",
        bool b => b ? "True" : "False",
        BigInteger n => n.ToString(CultureInfo.InvariantCulture),
        double d => FloatRepr(d),
        _ => throw new NotSupportedException($"printing a {TypeName(v)} is not ported"),
    };

    // repr(float): shortest round-trip digits, fixed between 1e-4 and 1e16, else d.ddde+XX.
    static string FloatRepr(double d)
    {
        if (double.IsInfinity(d))
            return d > 0 ? "inf" : "-inf";
        var (digits, exp) = Decompose(Math.Abs(d).ToString("R", CultureInfo.InvariantCulture));
        var sign = d < 0 || (d == 0 && double.IsNegative(d)) ? "-" : "";
        if (digits == "0")
            return sign + "0.0";
        if (exp >= -4 && exp < 16)
        {
            string body;
            if (exp >= digits.Length - 1)
                body = digits + new string('0', exp - digits.Length + 1) + ".0";
            else if (exp >= 0)
                body = digits[..(exp + 1)] + "." + digits[(exp + 1)..];
            else
                body = "0." + new string('0', -exp - 1) + digits;
            return sign + body;
        }
        var mantissa = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
        return $"{sign}{mantissa}e{(exp < 0 ? "-" : "+")}{Math.Abs(exp):00}";
    }

    // "1.5E-05" or "123.45" -> ("15", -5) or ("12345", 2): significant digits and the power of
    // ten of the first one.
    static (string Digits, int Exp) Decompose(string s)
    {
        var e = 0;
        if (s.IndexOf('E') is var ei and >= 0)
        {
            e = int.Parse(s[(ei + 1)..], CultureInfo.InvariantCulture);
            s = s[..ei];
        }
        var dot = s.IndexOf('.');
        var intPart = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..];
        var all = (intPart + frac).TrimStart('0');
        var lead = (intPart + frac).Length - all.Length;
        all = all.TrimEnd('0');
        if (all.Length == 0)
            return ("0", 0);
        return (all, intPart.Length - 1 - lead + e);
    }

    static string TypeName(object? v) => v switch
    {
        null => "NoneType",
        bool => "bool",
        BigInteger => "int",
        double => "float",
        string => "str",
        List<object?> => "list",
        List<KeyValuePair<string, object?>> => "dict",
        _ => v.GetType().Name,
    };
}
