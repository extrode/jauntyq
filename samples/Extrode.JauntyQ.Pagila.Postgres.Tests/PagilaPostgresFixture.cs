using Extrode.JauntyQ.TestInfra;
using Npgsql;
using Testcontainers.PostgreSql;
using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Pagila.Postgres.Tests;

/// <summary>
/// Boots a real PostgreSQL instance via Testcontainers and applies the
/// CANONICAL, untrimmed Pagila schema + full seed data, vendored verbatim
/// from devrimgunduz/pagila (BSD). Unlike the trimmed Sakila fixtures this
/// keeps the partitioned payment table, views, materialized views, triggers,
/// PL/pgSQL functions, the tsvector full-text column, and the pgvector
/// film_embedding table — which is why the image is pgvector/pgvector:pg18
/// rather than postgres:16-alpine (pgvector for the vector column; pg18
/// because the dump uses uuidv7() defaults), and why the scripts run through
/// ExecScriptAsync (psql handles COPY FROM stdin and dollar quoting).
/// Soft-skips (via <see cref="Available"/>) when Docker is unavailable.
/// </summary>
public sealed class PagilaPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }

    public JauntyDb Db => FixtureGate.RequireAvailable(_db, Available, SkipReason, nameof(PagilaPostgresFixture));
    public NpgsqlConnection Connection => FixtureGate.RequireAvailable(_conn, Available, SkipReason, nameof(PagilaPostgresFixture));
    private JauntyDb? _db;
    private NpgsqlConnection? _conn;

    public async Task InitializeAsync()
    {
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder("pgvector/pgvector:pg18").Build();
            _container = container;
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        string schema = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.postgres.sql"));
        string seed = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "seed.postgres.sql"));

        // psql exits 0 on SQL errors unless ON_ERROR_STOP is set; without
        // this a partially-applied schema surfaces later as empty query
        // results instead of failing the fixture loudly.
        const string strict = "\\set ON_ERROR_STOP on\n";
        var schemaResult = await container.ExecScriptAsync(strict + schema);
        if (schemaResult.ExitCode != 0)
            throw new InvalidOperationException($"Pagila schema apply failed: {schemaResult.Stderr}");
        var seedResult = await container.ExecScriptAsync(strict + seed);
        if (seedResult.ExitCode != 0)
            throw new InvalidOperationException($"Pagila seed apply failed: {seedResult.Stderr}");

        // pg_dump emits materialized views WITH NO DATA; pg_restore's final
        // step is the refresh, so doing it here is completing the canonical
        // restore, not deviating from it.
        var refreshResult = await container.ExecScriptAsync(strict + "REFRESH MATERIALIZED VIEW rental_by_category;");
        if (refreshResult.ExitCode != 0)
            throw new InvalidOperationException($"Pagila matview refresh failed: {refreshResult.Stderr}");

        _conn = new NpgsqlConnection(container.GetConnectionString());
        await _conn.OpenAsync();
        _db = new JauntyDb(_conn);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
