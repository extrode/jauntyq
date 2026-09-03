using System.Data.Common;
using Conduit.MariaDb.Tests;
using Conduit.MySql.Tests;
using Conduit.Postgres.Tests;
using Conduit.SqlServer.Tests;
using Conduit.Sqlite.Tests;
using Xunit;

namespace Conduit.Differential.Tests;

/// <summary>
/// One engine's seeded database, with its JauntyDb held as <see cref="object"/>.
///
/// The boxing is required, not stylistic: five referenced assemblies each
/// declare Extrode.JauntyQ.Generated.JauntyDb, so naming the type here is ambiguous.
/// Everything downstream reaches it through <see cref="QueryDispatcher"/>.
/// </summary>
/// <param name="Connection">
/// The fixture's own open connection. Spec 018's stress corpus executes SQL
/// directly on it rather than through a generated method, because a stress
/// case is a new query and authoring one the 017 way would mean editing five
/// sample projects -- see 018-plan.md. 017's own corpus does not use it.
/// </param>
public sealed record EngineHandle(
    string Name,
    bool Available,
    string? SkipReason,
    object? Db,
    DbConnection? Connection);

/// <summary>
/// Boots all five Conduit fixtures once for the assembly.
///
/// Each fixture already soft-skips when Docker is unavailable, so this adds
/// only the suite-level rule: one engine cannot disagree with itself, so
/// fewer than two available engines is a skip and not a pass.
/// </summary>
public sealed class EngineSet : IAsyncLifetime
{
    private ConduitSqliteFixture? _sqlite;
    private ConduitPostgresFixture? _postgres;
    private ConduitMySqlFixture? _mysql;
    private ConduitMariaDbFixture? _mariadb;
    private ConduitSqlServerFixture? _sqlserver;

    public IReadOnlyList<EngineHandle> Engines { get; private set; } = Array.Empty<EngineHandle>();

    public IReadOnlyList<EngineHandle> AvailableEngines =>
        Engines.Where(e => e.Available).ToList();

    /// <summary>
    /// SQLite is the pivot when present: it is in-proc, needs no container,
    /// and is therefore the one engine a developer always has, which makes
    /// its rows the stable side of every failure message.
    /// </summary>
    public EngineHandle? Pivot =>
        AvailableEngines.FirstOrDefault(e => e.Name == "Sqlite") ?? AvailableEngines.FirstOrDefault();

    public string SkipMessage =>
        $"Needs at least two engines; available: "
        + (AvailableEngines.Count == 0 ? "none" : string.Join(", ", AvailableEngines.Select(e => e.Name)))
        + ". Unavailable: "
        + string.Join("; ", Engines.Where(e => !e.Available).Select(e => $"{e.Name} ({e.SkipReason})"));

    public bool Enough => AvailableEngines.Count >= 2;

    public async Task InitializeAsync()
    {
        _sqlite = new ConduitSqliteFixture();
        _postgres = new ConduitPostgresFixture();
        _mysql = new ConduitMySqlFixture();
        _mariadb = new ConduitMariaDbFixture();
        _sqlserver = new ConduitSqlServerFixture();

        // Started together: four containers in sequence is four cold starts,
        // and the nightly job's wall clock is the reason this suite can run
        // there at all.
        await Task.WhenAll(
            _postgres.InitializeAsync(),
            _mysql.InitializeAsync(),
            _mariadb.InitializeAsync(),
            _sqlserver.InitializeAsync());

        Engines = new List<EngineHandle>
        {
            new("Sqlite", _sqlite.Available, _sqlite.SkipReason, _sqlite.Available ? _sqlite.Db : null, _sqlite.Available ? _sqlite.Connection : null),
            new("Postgres", _postgres.Available, _postgres.SkipReason, _postgres.Available ? _postgres.Db : null, _postgres.Available ? _postgres.Connection : null),
            new("MySql", _mysql.Available, _mysql.SkipReason, _mysql.Available ? _mysql.Db : null, _mysql.Available ? _mysql.Connection : null),
            new("MariaDb", _mariadb.Available, _mariadb.SkipReason, _mariadb.Available ? _mariadb.Db : null, _mariadb.Available ? _mariadb.Connection : null),
            new("SqlServer", _sqlserver.Available, _sqlserver.SkipReason, _sqlserver.Available ? _sqlserver.Db : null, _sqlserver.Available ? _sqlserver.Connection : null),
        };
    }

    public async Task DisposeAsync()
    {
        _sqlite?.Dispose();
        foreach (var disposal in new[]
        {
            _postgres?.DisposeAsync(),
            _mysql?.DisposeAsync(),
            _mariadb?.DisposeAsync(),
            _sqlserver?.DisposeAsync(),
        })
        {
            if (disposal is not null)
                try { await disposal; } catch { /* a fixture that never started */ }
        }
    }
}

[CollectionDefinition("Differential")]
public sealed class DifferentialCollection : ICollectionFixture<EngineSet> { }
