using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JauntyQ.Schema;
using JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

// SqlServerExtractor against the canonical AdventureWorks OLTP schema
// (canonical/adventureworkslt-schema.sql, vendored verbatim from
// microsoft/sql-server-samples' instawdb.sql — Microsoft ships the LT edition
// only as a .bak, so the full OLTP DDL is the authoritative script form; see
// the file's header for exactly which sections were kept). This is the
// hardest schema the extractor faces in-repo: five non-dbo schemas, alias
// types, computed columns (including one over a hierarchyid method call),
// typed-XML columns bound to XML SCHEMA COLLECTIONs, ROWGUIDCOL
// uniqueidentifiers and XML indexes. The extractor is single-schema scoped by
// design, so the fixture runs it once per schema and the tests pin per-schema
// results against counts read straight off the vendored DDL.
public sealed class AdventureWorksLtFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public IReadOnlyDictionary<string, DatabaseSchema> Schemas { get; private set; } =
        new Dictionary<string, DatabaseSchema>();

    private static readonly string[] SchemaNames =
        { "dbo", "HumanResources", "Person", "Production", "Purchasing", "Sales" };

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MsSql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        string connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_adventureworks");

        string script = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "canonical", "adventureworkslt-schema.sql"));

        // The vendored script is SSMS-style: batches separated by bare GO
        // lines, which are a client-tool convention SqlCommand does not
        // understand. The XML SCHEMA COLLECTION batches are the large ones,
        // hence the generous timeout.
        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            foreach (string batch in script.Split('\n')
                         .Aggregate(new List<System.Text.StringBuilder> { new() }, (acc, line) =>
                         {
                             if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)) acc.Add(new());
                             else acc[^1].AppendLine(line);
                             return acc;
                         })
                         .Select(b => b.ToString())
                         .Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var schemas = new Dictionary<string, DatabaseSchema>();
        foreach (string name in SchemaNames)
            schemas[name] = await new SqlServerExtractor(name).ExtractAsync(connectionString);
        Schemas = schemas;
        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class AdventureWorksLtExtractorTests : IClassFixture<AdventureWorksLtFixture>
{
    private readonly AdventureWorksLtFixture _fx;
    public AdventureWorksLtExtractorTests(AdventureWorksLtFixture fx) => _fx = fx;

    [SkippableFact]
    public void PerSchemaTableCounts_MatchTheVendoredDdl()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        // grep -c "CREATE TABLE [<schema>]" over the vendored script.
        Assert.Equal(3, _fx.Schemas["dbo"].Tables.Count);
        Assert.Equal(6, _fx.Schemas["HumanResources"].Tables.Count);
        Assert.Equal(13, _fx.Schemas["Person"].Tables.Count);
        Assert.Equal(25, _fx.Schemas["Production"].Tables.Count);
        Assert.Equal(5, _fx.Schemas["Purchasing"].Tables.Count);
        Assert.Equal(19, _fx.Schemas["Sales"].Tables.Count);
        Assert.Equal(71, _fx.Schemas.Values.Sum(s => s.Tables.Count));
    }

    [SkippableFact]
    public void ComputedColumns_AreCapturedAsRealColumnsAndFlagged()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var header = _fx.Schemas["Sales"].Tables["SalesOrderHeader"];
        Assert.True(header.Columns["SalesOrderNumber"].IsComputed);
        Assert.True(header.Columns["TotalDue"].IsComputed);

        // Computed over a CLR method call (OrganizationNode.GetLevel()).
        Assert.True(_fx.Schemas["HumanResources"].Tables["Employee"].Columns["OrganizationLevel"].IsComputed);
    }

    [SkippableFact]
    public void RowguidColumn_IsNonNullableUniqueidentifierWithUniqueIndex()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var address = _fx.Schemas["Person"].Tables["Address"];
        var rowguid = address.Columns["rowguid"];
        Assert.Equal("uniqueidentifier", rowguid.DbType);
        Assert.False(rowguid.IsNullable);
        Assert.False(rowguid.IsPrimaryKey);

        var ak = address.Indexes.Single(i => i.Name == "AK_Address_rowguid");
        Assert.True(ak.IsUnique);
        Assert.Equal(new[] { "rowguid" }, ak.Columns);
    }

    [SkippableFact]
    public void TypedXmlColumns_SurfaceAsXmlDbType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var person = _fx.Schemas["Person"].Tables["Person"];
        Assert.Equal(13, person.Columns.Count);
        Assert.Equal("xml", person.Columns["AdditionalContactInfo"].DbType);
        Assert.True(person.Columns["AdditionalContactInfo"].IsNullable);
        Assert.Equal("xml", person.Columns["Demographics"].DbType);
    }

    [SkippableFact]
    public void Hierarchyid_SurvivesExtractionAsItsOwnDbType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var node = _fx.Schemas["HumanResources"].Tables["Employee"].Columns["OrganizationNode"];
        Assert.Equal("hierarchyid", node.DbType);
        Assert.True(node.IsNullable);
    }

    [SkippableFact]
    public void AliasTypedColumn_ReportsItsBaseType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        // FirstName is declared as the alias type [Name] (nvarchar(50) NULL);
        // INFORMATION_SCHEMA reports the base type, which is what the
        // generator needs to map it.
        var firstName = _fx.Schemas["Person"].Tables["Person"].Columns["FirstName"];
        Assert.Equal("nvarchar", firstName.DbType);
        Assert.Equal(50, firstName.MaxLength);
        Assert.False(firstName.IsNullable);
    }

    [SkippableFact]
    public void SalesSchemaForeignKeys_IncludeOrderDetailToHeader()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var fks = _fx.Schemas["Sales"].ForeignKeys;
        Assert.Contains(fks, fk =>
            fk.FromTable == "SalesOrderDetail" && fk.FromColumn == "SalesOrderID" &&
            fk.ToTable == "SalesOrderHeader" && fk.ToColumn == "SalesOrderID");
    }
}
