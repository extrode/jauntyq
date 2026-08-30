using JauntyQ.Generated;
using Microsoft.Data.Sqlite;

// Packaged-consumer smoke: proves that JauntyQ, restored from a .nupkg and
// registered as an analyzer package, still generates code that compiles and
// runs. Every other project in this repo references the generator by
// ProjectReference, so nothing else here exercises the packed
// build/JauntyQ.Generator.props registration path, which is the only one an
// external consumer ever uses. a consumer hit the consequence: JauntyQ 0.1.0
// predated migration-parser index DDL, so the packaged generator raised JNT8004
// across their repo while this repo was green.
//
// Three things this checks that no other build here can:
//
//   * JauntyQShapeGuard is a POST-INITIALIZATION output. The generator emits it
//     before any pipeline node runs, so it exists if and only if the generator
//     ran at all. Referencing it means a load failure fails at a named type
//     rather than as a pile of spurious CS0246 on generated types.
//   * JauntyDb and the Widgets accessor come from the pipeline, so they exist
//     only if the generator ran AND produced output.
//   * auto-CRUD is OFF, which it can only be if <JauntyQAutoCrud>false reached
//     the generator through the packed props. See the .csproj for why that is
//     the assertion rather than a feature the smoke happens to want.
//
// Exit code 0 = all three held and the generated query returned the right rows.

SQLitePCL.Batteries_V2.Init();

var csb = new SqliteConnectionStringBuilder
{
    DataSource = "packaged_smoke",
    Mode = SqliteOpenMode.Memory,
    Cache = SqliteCacheMode.Shared
};

using var keepAlive = new SqliteConnection(csb.ToString());
keepAlive.Open();
using (var ddl = keepAlive.CreateCommand())
{
    // Raw ADO.NET rather than generated auto-CRUD, because auto-CRUD is off here.
    ddl.CommandText =
        "CREATE TABLE Widgets (WidgetId INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price NUMERIC NOT NULL);" +
        "INSERT INTO Widgets (Name, Price) VALUES ('Gadget', 9.99), ('Gizmo', 14.50);";
    ddl.ExecuteNonQuery();
}

System.Type shapeGuard = typeof(JauntyQShapeGuard);

using var conn = new SqliteConnection(csb.ToString());
conn.Open();
var db = new JauntyDb(conn);

var widgets = db.Widgets;
decimal streamedTotal = 0m;
int rows = 0;
foreach (var w in widgets.StreamAll())
{
    streamedTotal += w.Price;
    rows++;
}

// Auto-CRUD off means the accessor carries the one query from db/tables and
// nothing else. Insert/GetAll/GetById are the auto-CRUD surface; their absence
// is what proves the property crossed the packaged boundary.
System.Type accessor = widgets.GetType();
string[] autoCrudMethods = ["Insert", "Update", "Delete", "GetAll", "GetById", "Upsert"];
// GetMethods, not GetMethod: auto-CRUD emits overloads, and GetMethod throws
// AmbiguousMatchException on those -- which fails the smoke for the right
// reason but with a stack trace instead of the explanation below.
var present = new System.Collections.Generic.HashSet<string>(
    System.Linq.Enumerable.Select(accessor.GetMethods(), m => m.Name), System.StringComparer.Ordinal);
var leaked = System.Array.FindAll(autoCrudMethods, present.Contains);

bool ok =
    shapeGuard.FullName == "JauntyQ.Generated.JauntyQShapeGuard" &&
    rows == 2 &&
    streamedTotal == 24.49m &&
    leaked.Length == 0;

if (leaked.Length > 0)
{
    Console.Error.WriteLine(
        $"auto-CRUD is still ON: {accessor.Name} exposes {string.Join(", ", leaked)}. " +
        "<JauntyQAutoCrud>false did not reach the generator, which means the packed " +
        "build/JauntyQ.Generator.props was not imported or no longer registers the property.");
}

Console.WriteLine(ok
    ? $"JauntyQ packaged-consumer smoke OK (rows={rows}, streamedTotal={streamedTotal}, auto-CRUD off)."
    : "JauntyQ packaged-consumer smoke FAILED.");

return ok ? 0 : 1;
