using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class SqliteExtractorMutationCoverageTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"jq-sqlite-mut-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath};Pooling=False";

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private Task<DatabaseSchema> Extract() => new SqliteExtractor().ExtractAsync(ConnectionString);

    [Fact]
    public async Task QuotedTableAndIndexNames_StillYieldIndexesAndForeignKeys()
    {
        Exec("CREATE TABLE parent (id INTEGER PRIMARY KEY);");
        Exec("CREATE TABLE \"we\"\"ird\" (id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES parent(id), code TEXT);");
        Exec("CREATE INDEX \"ix\"\"q\" ON \"we\"\"ird\" (code);");

        var schema = await Extract();

        var index = Assert.Single(schema.Tables["we\"ird"].Indexes, i => i.Name == "ix\"q");
        Assert.Equal(new[] { "code" }, index.Columns);
        var fk = Assert.Single(schema.ForeignKeys, f => f.FromTable == "we\"ird");
        Assert.Equal(("parent_id", "parent", "id"), (fk.FromColumn, fk.ToTable, fk.ToColumn));
    }

    [Fact]
    public async Task PlainColumnIndex_HasNoPrefixKeyPart()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);");
        Exec("CREATE INDEX ix_people_email ON people (email);");

        var index = Assert.Single((await Extract()).Tables["people"].Indexes, i => i.Name == "ix_people_email");

        Assert.False(index.HasPrefixKeyPart);
        Assert.False(index.HasExpressionKeyPart);
    }

    [Theory]
    [InlineData("BLOBTEXT", "varchar")]
    [InlineData("DATECHAR", "varchar")]
    [InlineData("BLOBCLOB", "varchar")]
    public async Task TextAffinityWord_WinsOverLaterAffinityWords(string declared, string expected)
    {
        Exec($"CREATE TABLE t (c {declared});");

        Assert.Equal(expected, (await Extract()).Tables["t"].Columns["c"].DbType);
    }

    [Theory]
    [InlineData("5)", null, null)]
    [InlineData("(10)", 10, null)]
    [InlineData("(10,2)", 10, 2)]
    [InlineData("VARCHAR()", null, null)]
    [InlineData("VARCHAR", null, null)]
    public void ParseDeclaredNumbers_ReadsOnlyAWellFormedParenthesisedList(string declared, int? first, int? second)
    {
        Assert.Equal((first, second), SqliteExtractor.ParseDeclaredNumbers(declared));
    }
}
