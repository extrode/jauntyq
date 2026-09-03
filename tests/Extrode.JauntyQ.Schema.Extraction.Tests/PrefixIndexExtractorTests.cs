using System.Linq;
using Extrode.JauntyQ.Schema.Extraction;
using MySqlConnector;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// A MySQL column-prefix key part — <c>UNIQUE (email(5))</c> — enforces
/// uniqueness over the truncated prefix, not the full column values, so it can
/// fire against rows whose full columns differ. Before SUB_PART capture such an
/// index reached analysis indistinguishable from a full-column UNIQUE, which is
/// what forced UpsertKeyResolver's superset relaxation to be reverted
/// (AUD-R4-16's registry entry has the history). These tests pin the capture
/// live against the same INFORMATION_SCHEMA.STATISTICS query the product runs:
/// prefixed indexes arrive flagged, full-column ones arrive unflagged, and one
/// prefixed key part flags a composite index whose other parts are full-column.
/// MariaDB shares this extractor and the same STATISTICS.SUB_PART contract.
/// </summary>
public sealed class MySqlPrefixIndexFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MySql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_prefix_index");

        // Seed DDL runs outside the skip guard: a failure is a real bug, not a
        // Docker-absent skip.
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            create table users (
                id int not null primary key,
                email varchar(100) not null,
                tenant varchar(20) not null,
                unique key ux_users_email_prefix (email(5)),
                unique key ux_users_email_tenant_mixed (email(5), tenant),
                unique key ux_users_tenant_full (tenant)
            );";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlPrefixIndexExtractorTests : IClassFixture<MySqlPrefixIndexFixture>
{
    private readonly MySqlPrefixIndexFixture _fx;
    public MySqlPrefixIndexExtractorTests(MySqlPrefixIndexFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task PrefixUniqueIndex_ArrivesFlagged_WithFullColumnNames()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_email_prefix");

        Assert.True(ix.IsUnique);
        Assert.True(ix.HasPrefixKeyPart);
        // The full column name, not "email(5)": Columns stays name-resolvable
        // against TableSchema.Columns; the prefix truncation lives in the flag.
        Assert.Equal(new[] { "email" }, ix.Columns);
    }

    [SkippableFact]
    public async Task OnePrefixKeyPart_FlagsTheWholeCompositeIndex()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_email_tenant_mixed");

        // tenant's own key part is full-column and its row reports SUB_PART
        // NULL; the flag must still be set (and stay set regardless of which
        // key part the capture loop sees last).
        Assert.True(ix.HasPrefixKeyPart);
        Assert.Equal(new[] { "email", "tenant" }, ix.Columns);
    }

    [SkippableFact]
    public async Task FullColumnUniqueIndex_ArrivesUnflagged()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        var full = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_tenant_full");
        Assert.False(full.HasPrefixKeyPart);

        // And the PK, which INFORMATION_SCHEMA also reports as an index row.
        var pk = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "PRIMARY");
        Assert.False(pk.HasPrefixKeyPart);
    }
}
