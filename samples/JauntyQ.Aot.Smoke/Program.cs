using JauntyQ.Generated;
using Microsoft.Data.Sqlite;

// Microsoft.Data.Sqlite.Core needs the native provider registered explicitly.
SQLitePCL.Batteries_V2.Init();

// Native-AOT smoke test: exercises generated JauntyQ code (auto-CRUD, identity
// return, typed ordinal reads, and @stream iterators) against real SQLite. Built
// with PublishAot/IsAotCompatible so the trim + AOT analyzers verify none of the
// emitted or referenced code uses reflection or other AOT-unsafe patterns.
// Exit code 0 = the round-trip produced the expected values.

var csb = new SqliteConnectionStringBuilder
{
    DataSource = "aot_smoke",
    Mode = SqliteOpenMode.Memory,
    Cache = SqliteCacheMode.Shared
};

// Keep-alive holds the shared in-memory database open for the process.
using var keepAlive = new SqliteConnection(csb.ToString());
keepAlive.Open();
using (var ddl = keepAlive.CreateCommand())
{
    ddl.CommandText =
        "CREATE TABLE Widgets (WidgetId INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price NUMERIC NOT NULL);";
    ddl.ExecuteNonQuery();
}

using var conn = new SqliteConnection(csb.ToString());
conn.Open();
var db = new JauntyDb(conn);

// Insert via generated auto-CRUD with RETURNING identity.
int id1 = db.Widgets.Insert("Gadget", 9.99m);
int id2 = db.Widgets.Insert("Gizmo", 14.50m);

// Typed ordinal read back.
var all = db.Widgets.GetAll();
var one = db.Widgets.GetById(id1);

// Streaming iterator path (IEnumerable<Widget>).
decimal streamedTotal = 0m;
foreach (var w in db.Widgets.StreamAll())
    streamedTotal += w.Price;

bool ok =
    id1 > 0 && id2 > id1 &&
    all.Count == 2 &&
    one is { Name: "Gadget" } &&
    streamedTotal == 24.49m;

Console.WriteLine(ok
    ? $"JauntyQ AOT smoke test OK (rows={all.Count}, streamedTotal={streamedTotal})."
    : "JauntyQ AOT smoke test FAILED.");

return ok ? 0 : 1;
