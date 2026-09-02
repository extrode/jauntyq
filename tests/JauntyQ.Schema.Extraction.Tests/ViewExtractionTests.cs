using JauntyQ.Schema.Extraction;
using Microsoft.Data.Sqlite;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Spec 015 T2: views enter the snapshot as queryable relations, flagged
/// non-insertable, without disturbing the base tables beside them.
///
/// SQLite carries this test because it needs no container and its extractor
/// takes the same shape as the other three — one relation-type predicate, and
/// the flag set from the row that decided inclusion, so the two can never
/// disagree. The Postgres, SQL Server and MySQL predicates are exercised by
/// the container-backed live extractor suites.
/// </summary>
public class ViewExtractionTests : IDisposable
{
    private readonly string _dbName = $"view_extract_{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public ViewExtractionTests()
    {
        _connectionString = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE inbound_messages (
                id INTEGER PRIMARY KEY,
                user_id INTEGER NOT NULL,
                subject TEXT NOT NULL
            );

            CREATE TABLE inbound_deliveries (
                id INTEGER PRIMARY KEY,
                inbound_message_id INTEGER NOT NULL,
                status TEXT NOT NULL
            );

            CREATE VIEW v_message_outcomes AS
            SELECT m.id AS message_id,
                   m.subject AS subject,
                   max(d.status) AS outcome
            FROM inbound_messages m
            LEFT JOIN inbound_deliveries d ON d.inbound_message_id = m.id
            GROUP BY m.id, m.subject;
        ";
        cmd.ExecuteNonQuery();
    }

    private async Task<JauntyQ.Schema.DatabaseSchema> ExtractAsync() =>
        await new SqliteExtractor().ExtractAsync(_connectionString);

    [Fact]
    public async Task View_EntersTheSnapshot_AsARelation()
    {
        var schema = await ExtractAsync();

        Assert.True(schema.Tables.ContainsKey("v_message_outcomes"),
            "the view is absent from the snapshot entirely");
    }

    [Fact]
    public async Task View_IsFlaggedAsAViewAndNotInsertable()
    {
        var schema = await ExtractAsync();

        var view = schema.Tables["v_message_outcomes"];
        Assert.True(view.IsView);
        Assert.False(view.IsInsertable);
    }

    [Fact]
    public async Task View_CarriesItsProjectedColumns()
    {
        var schema = await ExtractAsync();

        var view = schema.Tables["v_message_outcomes"];
        Assert.Contains("message_id", view.Columns.Keys);
        Assert.Contains("subject", view.Columns.Keys);
        Assert.Contains("outcome", view.Columns.Keys);
    }

    [Fact]
    public async Task BaseTables_AreUnchanged_AndStillInsertable()
    {
        var schema = await ExtractAsync();

        foreach (var name in new[] { "inbound_messages", "inbound_deliveries" })
        {
            var table = schema.Tables[name];
            Assert.False(table.IsView, $"{name} was misflagged as a view");
            Assert.True(table.IsInsertable);
        }

        // The base table's own detail still extracts as before -- widening the
        // relation predicate must not cost the table half its facets.
        var messages = schema.Tables["inbound_messages"];
        Assert.True(messages.Columns["id"].IsPrimaryKey);
        Assert.False(messages.Columns["subject"].IsNullable);
    }

    /// <summary>
    /// A view has no indexes, which is the condition JNT8009 already treats as
    /// "uniqueness is unprovable, stay silent". Asserted here so the schema-side
    /// precondition for that behaviour is pinned where the view is produced.
    /// </summary>
    [Fact]
    public async Task View_CarriesNoIndexes()
    {
        var schema = await ExtractAsync();

        Assert.Empty(schema.Tables["v_message_outcomes"].Indexes);
    }

    public void Dispose() => _keepAlive.Dispose();
}
