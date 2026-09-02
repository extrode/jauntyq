using MySqlConnector;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// AUD-R4-16 pre-build probe. The fix replaces MySQL's untargeted
/// <c>ON DUPLICATE KEY UPDATE</c> with a key-targeted two-statement form on
/// tables carrying more than one UNIQUE constraint, and that form rests on four
/// facts about MySqlConnector and the two engines that must be measured rather
/// than assumed: multi-statement CommandText under a default connection string,
/// a named parameter reused across statements, <c>SELECT ... FROM DUAL WHERE
/// NOT EXISTS</c> parity between MySQL and MariaDB, and what ExecuteNonQuery
/// reports in each upsert branch under both UseAffectedRows settings.
///
/// These run before the emitter changes and stay afterwards: if a future
/// MySqlConnector or engine bump breaks any of them, the emitted SQL is wrong
/// and this is where it surfaces, rather than in a sample's round-trip.
/// </summary>
public sealed class MySqlUpsertProbeFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string MySqlConnectionString { get; private set; } = "";
    public string MariaDbConnectionString { get; private set; } = "";

    private const string Ddl = """
        CREATE TABLE users (
            id INT AUTO_INCREMENT PRIMARY KEY,
            email VARCHAR(100) NOT NULL UNIQUE,
            username VARCHAR(100) NOT NULL UNIQUE,
            bio VARCHAR(200) NULL
        );
        """;

    public async Task InitializeAsync()
    {
        var engines = await Task.WhenAll(EngineContainers.MySql, EngineContainers.MariaDb);
        var (mysql, mariadb) = (engines[0], engines[1]);
        if (!mysql.Available || !mariadb.Available)
        {
            SkipReason = mysql.SkipReason ?? mariadb.SkipReason;
            return;
        }
        MySqlConnectionString = await EngineContainers.CreateDatabaseAsync(mysql, "fx_upsert_probe");
        MariaDbConnectionString = await EngineContainers.CreateDatabaseAsync(mariadb, "fx_upsert_probe");

        // Seed DDL runs outside the skip guard: a failure here is a real bug.
        foreach (string cs in new[] { MySqlConnectionString, MariaDbConnectionString })
        {
            await using var conn = new MySqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Ddl;
            await cmd.ExecuteNonQueryAsync();
        }

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlUpsertProbeTests : IClassFixture<MySqlUpsertProbeFixture>
{
    private readonly MySqlUpsertProbeFixture _fx;

    public MySqlUpsertProbeTests(MySqlUpsertProbeFixture fx) => _fx = fx;

    public static TheoryData<string> Engines => new() { "mysql", "mariadb" };

    private string ConnectionString(string engine, bool useAffectedRows = false)
    {
        string cs = engine == "mysql" ? _fx.MySqlConnectionString : _fx.MariaDbConnectionString;
        var builder = new MySqlConnectionStringBuilder(cs) { UseAffectedRows = useAffectedRows };
        return builder.ConnectionString;
    }

    /// <summary>
    /// The exact shape the emitter will produce: one CommandText, two
    /// statements, the key parameter bound once and referenced three times.
    /// </summary>
    private const string KeyTargetedUpsert = """
        UPDATE users SET username = @username, bio = @bio WHERE email = @email;
        INSERT INTO users (email, username, bio)
        SELECT @email, @username, @bio FROM DUAL
         WHERE NOT EXISTS (SELECT 1 FROM users WHERE email = @email);
        """;

    private static async Task<int> UpsertAsync(string connectionString, string email, string username, string? bio)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = KeyTargetedUpsert;
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@bio", (object?)bio ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? result = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private static async Task ResetAsync(string connectionString)
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Probes 1-3 together, because the statement that answers one answers all
    /// three: two statements in one CommandText, @email bound once and read in
    /// three places, and FROM DUAL + NOT EXISTS on both engines.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Engines))]
    public async Task KeyTargetedUpsert_Runs_As_One_Command_On_Both_Engines(string engine)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason ?? "");
        string cs = ConnectionString(engine);
        await ResetAsync(cs);

        await UpsertAsync(cs, "a@example.com", "alice", "first");

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM users"));
        Assert.Equal("first", await ScalarAsync<string>(cs, "SELECT bio FROM users WHERE email = 'a@example.com'"));
    }

    /// <summary>
    /// The defect itself, stated as a behavioural assertion rather than a string
    /// comparison. The resolved conflict key is <c>email</c>; a second upsert
    /// with a new email but a username that already exists must therefore fail
    /// on the username UNIQUE rather than quietly updating the first row, which
    /// is what ON DUPLICATE KEY UPDATE does today.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Engines))]
    public async Task Key_Targeting_Does_Not_Match_On_A_Competing_Unique(string engine)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason ?? "");
        string cs = ConnectionString(engine);
        await ResetAsync(cs);

        await UpsertAsync(cs, "a@example.com", "alice", "first");

        // Same username, different email. ON DUPLICATE KEY UPDATE would match
        // the existing row on username and update it in place; key-targeted
        // SQL attempts a genuine insert, which the username UNIQUE rejects.
        await Assert.ThrowsAsync<MySqlException>(
            () => UpsertAsync(cs, "b@example.com", "alice", "second"));

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM users"));
        Assert.Equal("first", await ScalarAsync<string>(cs, "SELECT bio FROM users WHERE email = 'a@example.com'"));
    }

    /// <summary>
    /// Probe 4. Insert, update-with-change and update-no-change, under both
    /// UseAffectedRows settings. The value that matters for correctness is not
    /// the returned int but that update-no-change does NOT fall through to the
    /// INSERT and take a duplicate-key error -- the ROW_COUNT() trap this design
    /// avoids by asking NOT EXISTS instead.
    /// </summary>
    [SkippableTheory]
    [InlineData("mysql", false)]
    [InlineData("mysql", true)]
    [InlineData("mariadb", false)]
    [InlineData("mariadb", true)]
    public async Task Rerunning_An_Unchanged_Upsert_Does_Not_Attempt_An_Insert(string engine, bool useAffectedRows)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason ?? "");
        string cs = ConnectionString(engine, useAffectedRows);
        await ResetAsync(cs);

        int inserted = await UpsertAsync(cs, "a@example.com", "alice", "first");
        int changed = await UpsertAsync(cs, "a@example.com", "alice", "second");
        int unchanged = await UpsertAsync(cs, "a@example.com", "alice", "second");

        Assert.Equal(1, await ScalarAsync<int>(cs, "SELECT COUNT(*) FROM users"));
        Assert.Equal("second", await ScalarAsync<string>(cs, "SELECT bio FROM users WHERE email = 'a@example.com'"));

        Assert.Equal(1, inserted);
        Assert.Equal(1, changed);
        // Recorded rather than assumed: with UseAffectedRows=true an UPDATE that
        // matched but changed nothing reports 0, and the design accepts that
        // because the table state above is what callers depend on.
        Assert.Equal(useAffectedRows ? 0 : 1, unchanged);
    }
}
