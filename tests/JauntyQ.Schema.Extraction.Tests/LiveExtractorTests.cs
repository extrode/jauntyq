using System;
using System.Linq;
using JauntyQ.Schema.Extraction;
using JauntyQ.Schema;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

// Live-server-gated coverage for the three network-database extractors
// (SqlServerExtractor, PostgresExtractor, MySqlExtractor). Only
// SqliteExtractor is exercised by ExtractorTests because SQLite needs no
// server; these three soft-skip (no assertion, no failure) when no server is
// reachable, mirroring the Available-flag pattern used by
// MySqlFixture/PostgresFixture in the samples (this project does not
// reference Testcontainers, so these connect to a plain already-running
// server instead of spinning one up).
//
// Connection strings are read only from environment variables — never
// hard-coded — so no credential material lives in source:
//   JAUNTYQ_TEST_SQLSERVER_CONNECTION - if unset, falls back to a local
//     integrated-security (Windows auth, no secret) connection to a
//     "Northwind" database, built the same way NorthwindFixture.ConnectionString
//     is shaped (local server, Northwind db, trusted connection). Assumes
//     the standard Microsoft Northwind sample schema is installed there.
//   JAUNTYQ_TEST_POSTGRES_CONNECTION - no fallback; unset means these tests
//     soft-skip entirely.
//   JAUNTYQ_TEST_MYSQL_CONNECTION - no fallback; unset means these tests
//     soft-skip entirely.
// For Postgres/MySql there is no pre-seeded Northwind-subset schema without
// Testcontainers, so each fixture creates one throwaway, uniquely-named
// table to assert known facts against. Per repo convention, no DROP is
// issued; the throwaway table is simply left behind (same trade-off as
// leaving temp .db files around, just for a server that isn't disposable).
//
// A skip is only ever for "nothing was asked for". Setting the variable IS
// the request, so a server that will not accept a connection fails the run --
// see LiveServerGate.

/// <summary>
/// The one rule the three fixtures share: an unreachable server is a skip when
/// nobody asked for a live run, and a failure when somebody did.
///
/// It is a separate type so the rule can be tested without setting process-wide
/// environment variables from inside a parallel test run, and so the three
/// fixtures cannot drift apart in how they word the skip — the wording is a
/// contract with <c>scripts/skip-audit.js</c>, which fails any run carrying a
/// skip whose reason it does not sanction.
/// </summary>
public static class LiveServerGate
{
    /// <summary>
    /// Builds the sanctioned skip reason for <paramref name="variable"/>.
    /// <c>scripts/skip-audit.js</c> matches <c>JAUNTYQ_TEST_&lt;X&gt;_CONNECTION not set</c>,
    /// so that phrase has to survive any rewording of the rest.
    /// </summary>
    public static string NotSet(string variable, string engine) =>
        variable + " not set — live " + engine + " extractor tests skipped.";

    /// <summary>
    /// Called when the connection probe threw. Returns the skip reason when
    /// <paramref name="requested"/> is null; throws otherwise.
    /// </summary>
    public static string SkipOrThrow(string variable, string? requested, string engine, Exception cause)
    {
        if (requested != null)
            throw new InvalidOperationException(
                variable + " is set but the server did not accept a connection. These tests " +
                "skip when nothing was asked for, never when it was.", cause);

        return NotSet(variable, engine);
    }

    /// <summary>
    /// The skip reason for a server that accepted the connection but whose
    /// database does not carry the canonical sample schema these tests assert
    /// against.
    ///
    /// A third state, and before spec 014 nothing distinguished it: the only
    /// two outcomes were "could not connect" (skip) and "connected" (assert).
    /// That was invisible while no SQL Server connection was ever set, because
    /// the tests skipped for the first reason and never reached the second.
    /// Pointing JAUNTYQ_TEST_SQLSERVER_CONNECTION at a live server holding
    /// anything other than Northwind then failed four tests for a missing
    /// database rather than a broken extractor -- the exact confusion
    /// <see cref="SkipOrThrow"/> exists to prevent, one layer further in.
    ///
    /// Deliberately narrow. It names the object it looked for and did not
    /// find, so it cannot launder a connection that works but returns garbage:
    /// only an absent table reaches here, and everything after this check is
    /// still allowed to throw.
    /// </summary>
    public static string SchemaAbsent(string variable, string schemaName, string probeObject) =>
        variable + " names a database without the canonical " + schemaName +
        " schema (no " + probeObject + ") — those live tests skipped.";
}

public sealed class SqlServerLiveFixture
{
    // Built via SqlConnectionStringBuilder (rather than a literal connection
    // string) so no credential-shaped text lives in source: local server,
    // integrated/Windows auth, "Northwind" database.
    private static string BuildDefaultConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = "localhost",
        InitialCatalog = "Northwind",
        IntegratedSecurity = true,
        TrustServerCertificate = true
    }.ConnectionString;

    public bool Available { get; }
    public string? SkipReason { get; }
    public DatabaseSchema? Schema { get; }

    public SqlServerLiveFixture()
    {
        string? requested = Environment.GetEnvironmentVariable("JAUNTYQ_TEST_SQLSERVER_CONNECTION");
        string connStr = requested ?? BuildDefaultConnectionString();

        // Only the reachability probe is soft-skip material. Wrapping
        // ExtractAsync in the same catch (the original shape here) meant any
        // exception thrown by a genuine SqlServerExtractor bug -- not just
        // "no server" -- was silently reported as the same "not reachable"
        // skip, so a real extractor regression would disappear as a skipped
        // test instead of a failure. Only a failure to open the connection
        // itself is a legitimate skip; everything after it must be allowed
        // to throw and fail these tests for real.
        try
        {
            using var probe = new SqlConnection(connStr);
            probe.Open();

            // Reachable is not the same as Northwind. Checked here rather than
            // left to the assertions so the absence of a sample database reads
            // as a skip, while an extractor that reaches a real Northwind and
            // gets it wrong still fails loudly below.
            using var check = probe.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Products'";
            if ((int)check.ExecuteScalar()! == 0)
            {
                Available = false;
                SkipReason = LiveServerGate.SchemaAbsent(
                    "JAUNTYQ_TEST_SQLSERVER_CONNECTION", "Northwind", "dbo.Products");
                return;
            }
        }
        catch (Exception ex)
        {
            // Nothing asked for means the fallback was a guess at a local
            // Northwind, and its absence is the ordinary developer-machine
            // case. It used to skip with a reason worded around "not
            // reachable", which scripts/skip-audit.js does not sanction, so
            // every full run on a machine without Northwind reported four
            // skips with "no sanctioned reason" and failed test-all.sh for an
            // expected condition.
            Available = false;
            SkipReason = LiveServerGate.SkipOrThrow(
                "JAUNTYQ_TEST_SQLSERVER_CONNECTION", requested, "SQL Server", ex);
            return;
        }

        Schema = new SqlServerExtractor().ExtractAsync(connStr).GetAwaiter().GetResult();
        Available = true;
    }
}

/// <summary>
/// Spec 014's SQL Server fixture, and deliberately NOT
/// <see cref="SqlServerLiveFixture"/>. That one asserts against the canonical
/// Northwind, so it needs a Northwind to exist; this one creates every object
/// it asserts on, so it runs against any reachable SQL Server. The distinction
/// matters because it is why these tests execute at all: the four Northwind
/// tests have skipped on every machine that never had one.
///
/// Objects are created in the connected database with GUID-suffixed names, and
/// never dropped — the repo convention, with the container as the disposal
/// mechanism (a cleanup script).
/// </summary>
public sealed class SqlServerSpec014LiveFixture : IDisposable
{
    public bool Available { get; }
    public string? SkipReason { get; }
    public DatabaseSchema? Schema { get; }

    private readonly string _suffix = Guid.NewGuid().ToString("N");

    public string AliasTypeName => "jq_alias_" + _suffix;
    public string TableTypeName => "jq_tabletype_" + _suffix;
    public string TableName => "jq_table_" + _suffix;
    public string FunctionName => "jq_fn_" + _suffix;
    public string NoArgFunctionName => "jq_fnnoargs_" + _suffix;
    public string TableValuedFunctionName => "jq_tvf_" + _suffix;
    public string TvpProcedureName => "jq_proc_tvp_" + _suffix;

    private SqlConnection? _conn;

    public SqlServerSpec014LiveFixture()
    {
        string? requested = Environment.GetEnvironmentVariable("JAUNTYQ_TEST_SQLSERVER_CONNECTION");
        if (string.IsNullOrEmpty(requested))
        {
            Available = false;
            SkipReason = LiveServerGate.NotSet("JAUNTYQ_TEST_SQLSERVER_CONNECTION", "SQL Server");
            return;
        }

        try
        {
            _conn = new SqlConnection(requested);
            _conn.Open();
        }
        catch (Exception ex)
        {
            SkipReason = LiveServerGate.SkipOrThrow(
                "JAUNTYQ_TEST_SQLSERVER_CONNECTION", requested, "SQL Server", ex);
            Available = false;
            return;
        }

        // One statement per command: CREATE TYPE, CREATE FUNCTION and CREATE
        // PROCEDURE each have to be the first statement in their batch, which
        // is the same GO-batching rule that makes sqlcmd mis-split
        // schema.sqlserver.sql. Sending them individually sidesteps batching
        // entirely rather than reproducing it.
        foreach (string ddl in new[]
        {
            $"CREATE TYPE {AliasTypeName} FROM varchar(11) NOT NULL",
            $"CREATE TYPE {TableTypeName} AS TABLE (id int NOT NULL, label nvarchar(50) NULL)",
            $"CREATE TABLE {TableName} (id int IDENTITY PRIMARY KEY, national_id {AliasTypeName} NOT NULL, note nvarchar(max) NULL)",
            $"CREATE FUNCTION {FunctionName}(@amount decimal(12,2), @rate decimal(5,4)) RETURNS decimal(12,2) AS BEGIN RETURN @amount * @rate END",
            $"CREATE FUNCTION {NoArgFunctionName}() RETURNS int AS BEGIN RETURN 42 END",
            // A table-valued function, which must be EXCLUDED from capture.
            $"CREATE FUNCTION {TableValuedFunctionName}(@since int) RETURNS TABLE AS RETURN (SELECT id FROM {TableName} WHERE id > @since)",
            // The TVP-taking procedure: INFORMATION_SCHEMA reports its
            // parameter's DATA_TYPE as the literal 'table type', so before
            // spec 014 every TVP in a database shared one pseudo-type name.
            $"CREATE PROCEDURE {TvpProcedureName} @ids {TableTypeName} READONLY AS SELECT id FROM @ids",
        })
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        Schema = new SqlServerExtractor().ExtractAsync(requested).GetAwaiter().GetResult();
        Available = true;
    }

    public void Dispose() => _conn?.Dispose();
}

[Trait("Category", "AuditRegression")]
public class SqlServerSpec014LiveTests : IClassFixture<SqlServerSpec014LiveFixture>
{
    private readonly SqlServerSpec014LiveFixture _fx;
    public SqlServerSpec014LiveTests(SqlServerSpec014LiveFixture fx) => _fx = fx;

    [SkippableFact]
    public void ScalarFunction_IsCapturedWithItsParametersAndReturn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = Assert.Single(_fx.Schema!.Functions.Values, f => f.Name == _fx.FunctionName);

        Assert.Equal(new[] { "amount", "rate" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "decimal", "decimal" }, fn.Params.Select(p => p.DbType));
        Assert.Equal("decimal", fn.Return.DbType);
        Assert.Equal("dbo", fn.Schema);
    }

    [SkippableFact]
    public void ZeroArgumentFunction_IsCaptured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.True(_fx.Schema!.Functions.ContainsKey(_fx.NoArgFunctionName));
        Assert.Empty(_fx.Schema!.Functions[_fx.NoArgFunctionName].Params);
    }

    [SkippableFact]
    public void TableValuedFunction_IsExcludedFromCapture()
    {
        // SQL Server reports a TVF's return as DATA_TYPE 'TABLE'. Captured as
        // a scalar it would generate a method returning the first column of
        // the first row and present it as the function's value.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.DoesNotContain(
            _fx.Schema!.Functions.Values, f => f.Name == _fx.TableValuedFunctionName);
    }

    [SkippableFact]
    public void AliasType_IsCapturedResolvedToItsBaseType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var alias = _fx.Schema!.UserTypes[_fx.AliasTypeName];

        Assert.Equal(UserTypeKind.Alias, alias.Kind);
        Assert.Equal("varchar", alias.UnderlyingDbType);
        Assert.Equal(11, alias.MaxLength);
    }

    [SkippableFact]
    public void AliasTypedColumn_GeneratesTheUnderlyingPrimitiveNotObject()
    {
        // SC-002 on SQL Server. Before spec 014 this column mapped to object
        // with JNT2007.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var col = _fx.Schema!.Tables[_fx.TableName].Columns["national_id"];

        Assert.Equal("varchar", col.DbType);
        Assert.Equal(11, col.MaxLength);
        Assert.Equal(_fx.AliasTypeName, col.ResolvedFromUserType);
    }

    [SkippableFact]
    public void TableType_IsCapturedWithItsColumnsAndNotResolved()
    {
        // The list JNT2025 prints. Capturing a type the generator will never
        // emit for is what makes the refusal name a shape instead of saying
        // "unmappable".
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var tt = _fx.Schema!.UserTypes[_fx.TableTypeName];

        Assert.Equal(UserTypeKind.TableType, tt.Kind);
        Assert.Null(tt.UnderlyingDbType);
        Assert.Equal(new[] { "id", "label" }, tt.Members.Select(m => m.Name));
        Assert.Equal("int", tt.Members[0].DbType);
        Assert.Equal("nvarchar", tt.Members[1].DbType);
        Assert.Equal(50, tt.Members[1].MaxLength);
    }

    [SkippableFact]
    public void AProcedureTakingATvpRecordsTheTypeNameNotThePseudoType()
    {
        // What INFORMATION_SCHEMA.PARAMETERS reports for a table-valued
        // parameter, measured rather than assumed: DATA_TYPE is the literal
        // string 'table type', not null. So nothing ever threw -- every TVP in
        // the database collapsed onto one shared pseudo-type instead, which
        // made two procedures taking two DIFFERENT table types indistinguish-
        // able in the snapshot and left JNT2025 with no name to report.
        // USER_DEFINED_TYPE_NAME is where the real one lives.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var proc = _fx.Schema!.Procedures[_fx.TvpProcedureName];
        var param = Assert.Single(proc.Params);

        Assert.Equal("ids", param.Name);
        Assert.Equal(_fx.TableTypeName, param.DbType);
    }
}

[Trait("Category", "AuditRegression")]
public class SqlServerExtractorLiveTests : IClassFixture<SqlServerLiveFixture>
{
    private readonly SqlServerLiveFixture _fx;
    public SqlServerExtractorLiveTests(SqlServerLiveFixture fx) => _fx = fx;

    [SkippableFact]
    public void ProductsTable_Exists()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        Assert.True(_fx.Schema!.Tables.ContainsKey("Products"));
    }

    [SkippableFact]
    public void ProductId_IsIdentityPrimaryKey()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        // Column casing varies between the classic Microsoft Northwind sample
        // ("ProductID") and modernized copies ("ProductId"); resolve it
        // case-insensitively so the extractor is verified against either.
        var col = _fx.Schema!.Tables["Products"].Columns
            .First(c => string.Equals(c.Key, "ProductID", StringComparison.OrdinalIgnoreCase)).Value;

        Assert.True(col.IsPrimaryKey);
        Assert.True(col.IsIdentity);
        Assert.Equal("int", col.DbType);
    }

    [SkippableFact]
    public void ProductName_IsUnicodeVarcharWithMaxLength40()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables["Products"].Columns["ProductName"];

        Assert.Equal("nvarchar", col.DbType);
        Assert.Equal(40, col.MaxLength);
        Assert.True(col.IsUnicode);
        Assert.False(col.IsNullable);
    }

    [SkippableFact]
    public void ProductsTable_IndexesCapturedInKeyOrder()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var indexes = _fx.Schema!.Tables["Products"].Indexes;

        Assert.NotEmpty(indexes);
        // The PK constraint creates a unique index whose leading (only) key
        // column is ProductID (case-insensitive: "ProductID" or "ProductId").
        Assert.Contains(indexes, ix => ix.IsUnique && ix.Columns.Count > 0
            && string.Equals(ix.Columns[0], "ProductID", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PostgresLiveFixture : IDisposable
{
    public bool Available { get; }
    public string? SkipReason { get; }
    public DatabaseSchema? Schema { get; }
    public string TableName { get; } = $"jauntyq_livetest_{Guid.NewGuid():N}";

    /// <summary>Spec 015: a regular view over <see cref="TableName"/>.</summary>
    public string ViewName { get; } = $"jauntyq_liveview_{Guid.NewGuid():N}";

    /// <summary>
    /// Spec 015: a MATERIALIZED view, which is the case information_schema
    /// cannot see at all and which the catalog pass exists for.
    /// </summary>
    public string MatViewName { get; } = $"jauntyq_livematview_{Guid.NewGuid():N}";

    /// <summary>
    /// A base table whose columns carry every type facet the two extraction
    /// passes could disagree on, and <see cref="FacetMatViewName"/> selecting
    /// them straight through. Same columns, same types, so any difference in
    /// the extracted <c>ColumnSchema</c> is the extractor's, not the schema's.
    /// </summary>
    public string FacetTableName { get; } = $"jauntyq_livefacets_{Guid.NewGuid():N}";

    /// <summary>The matview counterpart of <see cref="FacetTableName"/>.</summary>
    public string FacetMatViewName { get; } = $"jauntyq_livefacetmv_{Guid.NewGuid():N}";

    /// <summary>Spec 014: a DOMAIN over varchar(11), and a table column typed by it.</summary>
    public string DomainName { get; } = $"jauntyq_livedomain_{Guid.NewGuid():N}";

    /// <summary>Spec 014: the table carrying a <see cref="DomainName"/>-typed column.</summary>
    public string DomainTableName { get; } = $"jauntyq_livedomaintable_{Guid.NewGuid():N}";

    /// <summary>Spec 014: a composite type — captured, never resolved.</summary>
    public string CompositeName { get; } = $"jauntyq_livecomposite_{Guid.NewGuid():N}";

    /// <summary>Spec 014: a two-argument scalar function, the headline case.</summary>
    public string FunctionName { get; } = $"jauntyq_livefn_{Guid.NewGuid():N}";

    /// <summary>Spec 014: a zero-argument scalar function.</summary>
    public string NoArgFunctionName { get; } = $"jauntyq_livefnnoargs_{Guid.NewGuid():N}";

    /// <summary>
    /// Spec 014: two functions sharing one name, which PostgreSQL permits and a
    /// name-keyed snapshot would silently collapse to whichever the catalog
    /// returned last.
    /// </summary>
    public string OverloadedFunctionName { get; } = $"jauntyq_livefnover_{Guid.NewGuid():N}";

    /// <summary>
    /// Spec 014: a set-returning (table-valued) function. Deferred to its own
    /// spec, so this must be EXCLUDED at capture — captured as a scalar it
    /// would generate a method returning the first column of the first row and
    /// call that the function's value.
    /// </summary>
    public string SetReturningFunctionName { get; } = $"jauntyq_livefntable_{Guid.NewGuid():N}";

    /// <summary>
    /// Spec 014: a user-defined aggregate. Out of scope (spec §4) and excluded
    /// at capture deliberately rather than by crashing the extractor.
    /// </summary>
    public string AggregateName { get; } = $"jauntyq_liveagg_{Guid.NewGuid():N}";

    private NpgsqlConnection? _conn;

    public PostgresLiveFixture()
    {
        // No hard-coded fallback connection string: unset env var means
        // these tests soft-skip rather than guessing at local credentials.
        string? connStr = Environment.GetEnvironmentVariable("JAUNTYQ_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrEmpty(connStr))
        {
            Available = false;
            SkipReason = LiveServerGate.NotSet("JAUNTYQ_TEST_POSTGRES_CONNECTION", "Postgres");
            return;
        }

        // Only the connection-open step is soft-skip material. The original
        // shape wrapped the throwaway-table DDL and ExtractAsync in the same
        // catch, so a genuine PostgresExtractor bug (or a DDL incompatibility
        // with a real server) was silently reported as the same "not
        // reachable" skip -- a real regression would disappear as a skipped
        // test instead of a failure. Only a failed connection attempt is a
        // legitimate skip; everything after it must be allowed to throw and
        // fail these tests for real.
        try
        {
            _conn = new NpgsqlConnection(connStr);
            _conn.Open();
        }
        catch (Exception ex)
        {
            // Reaching here means the variable was set, since an unset one
            // returned above -- so this always throws.
            SkipReason = LiveServerGate.SkipOrThrow(
                "JAUNTYQ_TEST_POSTGRES_CONNECTION", connStr, "Postgres", ex);
            Available = false;
            return;
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"CREATE EXTENSION IF NOT EXISTS citext;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $@"
                CREATE TABLE {TableName} (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(40) NOT NULL,
                    price NUMERIC(10,2) NULL,
                    email CITEXT NULL
                );
                CREATE INDEX {TableName}_name_idx ON {TableName} (name);

                CREATE VIEW {ViewName} AS
                    SELECT id, name, price FROM {TableName};

                CREATE MATERIALIZED VIEW {MatViewName} AS
                    SELECT name, count(*) AS n FROM {TableName} GROUP BY name;

                -- The matview pass reads pg_class/pg_attribute rather than
                -- information_schema, so it is a SECOND implementation of
                -- column extraction and can disagree with the first. This
                -- table and matview carry the same columns deliberately: the
                -- parity tests below compare the two passes column by column,
                -- which is the only thing that catches a divergence in a facet
                -- nobody thought to assert.
                CREATE TABLE {FacetTableName} (
                    name VARCHAR(40) NOT NULL,
                    price NUMERIC(10,2) NULL,
                    code CHAR(5) NULL,
                    tags INTEGER[] NULL,
                    labels TEXT[] NULL,
                    body TEXT NULL
                );

                CREATE MATERIALIZED VIEW {FacetMatViewName} AS
                    SELECT name, price, code, tags, labels, body FROM {FacetTableName};";
            cmd.ExecuteNonQuery();
        }

        // Spec 014. Every object here is one the extractor must treat
        // differently from its neighbour: a domain resolves, a composite is
        // captured whole, a scalar function is captured, and a set-returning
        // function and an aggregate are excluded. Built in one batch so a
        // failure names the statement that caused it.
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $@"
                CREATE DOMAIN {DomainName} AS VARCHAR(11);

                CREATE TABLE {DomainTableName} (
                    id SERIAL PRIMARY KEY,
                    national_id {DomainName} NULL,
                    note TEXT NULL
                );

                CREATE TYPE {CompositeName} AS (amount NUMERIC(12,2), currency CHAR(3));

                CREATE FUNCTION {FunctionName}(amount NUMERIC, rate NUMERIC) RETURNS NUMERIC
                    AS $$ SELECT amount * rate $$ LANGUAGE sql;

                CREATE FUNCTION {NoArgFunctionName}() RETURNS INTEGER
                    AS $$ SELECT 42 $$ LANGUAGE sql;

                CREATE FUNCTION {OverloadedFunctionName}(a INTEGER) RETURNS INTEGER
                    AS $$ SELECT a $$ LANGUAGE sql;
                CREATE FUNCTION {OverloadedFunctionName}(a TEXT) RETURNS INTEGER
                    AS $$ SELECT length(a) $$ LANGUAGE sql;

                CREATE FUNCTION {SetReturningFunctionName}(since INTEGER)
                    RETURNS TABLE (id INTEGER, note TEXT)
                    AS $$ SELECT id, note FROM {DomainTableName} WHERE id > since $$ LANGUAGE sql;

                CREATE AGGREGATE {AggregateName} (INTEGER) (
                    sfunc = int4pl, stype = INTEGER, initcond = '0'
                );";
            cmd.ExecuteNonQuery();
        }

        Schema = new PostgresExtractor().ExtractAsync(connStr).GetAwaiter().GetResult();
        Available = true;
    }

    public void Dispose() => _conn?.Dispose();
}

[Trait("Category", "AuditRegression")]
public class PostgresExtractorLiveTests : IClassFixture<PostgresLiveFixture>
{
    private readonly PostgresLiveFixture _fx;
    public PostgresExtractorLiveTests(PostgresLiveFixture fx) => _fx = fx;

    [SkippableFact]
    public void Table_Extracted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        Assert.True(_fx.Schema!.Tables.ContainsKey(_fx.TableName));
    }

    [SkippableFact]
    public void IdColumn_IsIdentityPrimaryKey()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["id"];

        Assert.True(col.IsPrimaryKey);
        Assert.True(col.IsIdentity); // serial -> nextval() default
    }

    [SkippableFact]
    public void NameColumn_IsUnicodeVarcharWithMaxLength40()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["name"];

        Assert.Equal("character varying", col.DbType);
        Assert.Equal(40, col.MaxLength);
        Assert.True(col.IsUnicode);
        Assert.False(col.IsNullable);
    }

    [SkippableFact]
    public void CitextColumn_IsUnicode()
    {
        // citext (case-insensitive text extension type) reports
        // data_type = 'USER-DEFINED', not one of the plain string types the
        // is_unicode CASE checked -- before this fix, IsUnicode stayed null
        // ("unknown") for it even though Postgres text of any kind is
        // Unicode under the server's UTF-8 encoding.
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["email"];

        Assert.Equal("citext", col.DbType);
        Assert.True(col.IsUnicode);
    }

    [SkippableFact]
    public void PriceColumn_HasPrecisionAndScale()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["price"];

        Assert.Equal(10, col.Precision);
        Assert.Equal(2, col.Scale);
        Assert.True(col.IsNullable);
    }

    [SkippableFact]
    public void NameIndex_Captured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var indexes = _fx.Schema!.Tables[_fx.TableName].Indexes;

        Assert.Contains(indexes, ix => ix.Columns.Contains("name"));
    }

    // ── Spec 015: views ────────────────────────────────────────────────────

    [SkippableFact]
    public void View_Extracted_AsNonInsertableRelation()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var view = _fx.Schema!.Tables[_fx.ViewName];
        Assert.True(view.IsView);
        Assert.False(view.IsInsertable);
        Assert.Equal(new[] { "id", "name", "price" }, view.Columns.Keys.OrderBy(k => k).ToArray());
    }

    /// <summary>
    /// The case the whole catalog pass exists for. A materialized view appears
    /// in NEITHER information_schema.tables NOR information_schema.columns, so
    /// widening the table_type predicate cannot reach it and there would be no
    /// column shape even if it could.
    /// </summary>
    [SkippableFact]
    public void MaterializedView_Extracted_ThoughInformationSchemaCannotSeeIt()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var matview = _fx.Schema!.Tables[_fx.MatViewName];
        Assert.True(matview.IsView);
        Assert.False(matview.IsInsertable);
        Assert.Contains("name", matview.Columns.Keys);
        Assert.Contains("n", matview.Columns.Keys);
    }

    /// <summary>
    /// The matview pass is a second implementation of column extraction, so it
    /// is checked against the first rather than against hand-written expected
    /// values — a facet both passes get wrong the same way is not a divergence,
    /// and a facet only one pass captures is exactly what this catches.
    ///
    /// Measured 2026-08-18, before the fix: every one of these was null on the
    /// matview side. <c>format_type(atttypid, NULL)</c> passes a NULL typmod,
    /// which is what carries length, precision and scale, and the pass read no
    /// facet columns at all.
    /// </summary>
    [SkippableTheory]
    [InlineData("name")]
    [InlineData("price")]
    [InlineData("code")]
    [InlineData("tags")]
    [InlineData("labels")]
    [InlineData("body")]
    public void MaterializedViewColumn_CarriesTheSameTypeFacetsAsItsBaseTableColumn(string column)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var tableCol = _fx.Schema!.Tables[_fx.FacetTableName].Columns[column];
        var mvCol = _fx.Schema!.Tables[_fx.FacetMatViewName].Columns[column];

        Assert.Equal(tableCol.DbType, mvCol.DbType);
        Assert.Equal(tableCol.MaxLength, mvCol.MaxLength);
        Assert.Equal(tableCol.Precision, mvCol.Precision);
        Assert.Equal(tableCol.Scale, mvCol.Scale);
        Assert.Equal(tableCol.IsUnicode, mvCol.IsUnicode);
    }

    /// <summary>
    /// The parity test above would also pass if BOTH passes captured nothing,
    /// so the facets that must be non-null are named outright. varchar(40) is
    /// the one with a consumer-visible consequence: JNT5001 refuses a string
    /// literal that cannot fit the column, and a null MaxLength silently skips
    /// that check for every matview column.
    /// </summary>
    [SkippableFact]
    public void MaterializedViewColumn_CapturesLengthAndPrecision_NotJustParity()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var cols = _fx.Schema!.Tables[_fx.FacetMatViewName].Columns;

        Assert.Equal(40, cols["name"].MaxLength);
        Assert.Equal(5, cols["code"].MaxLength);
        Assert.Equal(10, cols["price"].Precision);
        Assert.Equal(2, cols["price"].Scale);
    }

    /// <summary>
    /// The other half: a facet the base-table pass deliberately leaves null
    /// must stay null here. <c>_pg_numeric_precision</c> answers 32 for a plain
    /// <c>integer</c>, but the table pass gates precision to
    /// <c>data_type = 'numeric'</c>, so reading the helper ungated would make
    /// the matview claim a precision no table column ever reports — parity
    /// broken in the opposite direction by the fix for the first break.
    /// </summary>
    [SkippableFact]
    public void MaterializedViewIntegerColumn_ClaimsNoPrecision()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var n = _fx.Schema!.Tables[_fx.MatViewName].Columns["n"];

        Assert.Null(n.Precision);
        Assert.Null(n.Scale);
    }

    /// <summary>
    /// The array-spelling divergence, which is its own bug: the table pass sees
    /// <c>data_type = 'ARRAY'</c> with <c>udt_name = '_int4'</c> and records
    /// <c>int4[]</c>, while <c>format_type</c> spells the same type
    /// <c>integer[]</c>. Both map to <c>int[]</c> in C#, so nothing was
    /// mistyped — but a snapshot then holds two different strings for one type,
    /// and every DbType comparison (migration impact, snapshot diffing) reads
    /// that as a change.
    /// </summary>
    [SkippableFact]
    public void MaterializedViewArrayColumn_IsSpelledAsTheTablePassSpellsIt()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var mv = _fx.Schema!.Tables[_fx.FacetMatViewName].Columns;

        Assert.Equal("int4[]", mv["tags"].DbType);
        Assert.Equal("text[]", mv["labels"].DbType);
    }

    [SkippableFact]
    public void BaseTable_IsNotFlaggedAsAView()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.False(_fx.Schema!.Tables[_fx.TableName].IsView);
        Assert.True(_fx.Schema!.Tables[_fx.TableName].IsInsertable);
    }

    // ── Spec 014: functions and user-defined types ────────────────────

    [SkippableFact]
    public void ScalarFunction_IsCapturedWithItsParametersAndReturn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = Assert.Single(_fx.Schema!.Functions.Values, f => f.Name == _fx.FunctionName);

        Assert.Equal(new[] { "amount", "rate" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "numeric", "numeric" }, fn.Params.Select(p => p.DbType));
        Assert.Equal("numeric", fn.Return.DbType);
        Assert.Equal("public", fn.Schema);
    }

    [SkippableFact]
    public void ZeroArgumentFunction_IsCapturedAndKeyedByItsBareName()
    {
        // The signature key degrades to the bare name when there are no
        // arguments, so the common case reads like every sibling entity.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.True(_fx.Schema!.Functions.ContainsKey(_fx.NoArgFunctionName));
        Assert.Empty(_fx.Schema!.Functions[_fx.NoArgFunctionName].Params);
    }

    [SkippableFact]
    public void BothOverloadsSurviveCapture()
    {
        // The reason FunctionSchema is keyed by signature rather than name.
        // A name-keyed dictionary keeps whichever the catalog returned last,
        // and the generator then has nothing to report JNT2023 over -- the
        // collision would be resolved silently, at capture, in catalog order.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var overloads = _fx.Schema!.Functions.Values
            .Where(f => f.Name == _fx.OverloadedFunctionName)
            .ToList();

        Assert.Equal(2, overloads.Count);
        Assert.Equal(
            new[] { "integer", "text" },
            overloads.Select(o => o.Params.Single().DbType).OrderBy(t => t, StringComparer.Ordinal));
    }

    [SkippableFact]
    public void SetReturningFunction_IsExcludedFromCapture()
    {
        // Table-valued functions are deferred to their own spec. Captured as a
        // scalar, this one would generate a method returning the first column
        // of the first row and present it as the function's value.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.DoesNotContain(
            _fx.Schema!.Functions.Values, f => f.Name == _fx.SetReturningFunctionName);
    }

    [SkippableFact]
    public void UserDefinedAggregate_IsExcludedFromCapture()
    {
        // Out of scope (spec §4), and excluded deliberately rather than by
        // crashing the extractor -- which is the failure mode the spec's edge
        // case names.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.DoesNotContain(_fx.Schema!.Functions.Values, f => f.Name == _fx.AggregateName);
    }

    [SkippableFact]
    public void Domain_IsCapturedResolvedToItsBaseType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var domain = _fx.Schema!.UserTypes[_fx.DomainName];

        Assert.Equal(UserTypeKind.Domain, domain.Kind);
        Assert.Equal("character varying", domain.UnderlyingDbType);
        Assert.Equal(11, domain.MaxLength);
    }

    [SkippableFact]
    public void DomainTypedColumn_GeneratesTheUnderlyingPrimitiveNotObject()
    {
        // SC-002, and the whole point of FR-004. Before spec 014 this column's
        // DbType was the domain name, which no DialectMapper rule matches, so
        // it fell to object + JNT2007.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var col = _fx.Schema!.Tables[_fx.DomainTableName].Columns["national_id"];

        Assert.Equal("character varying", col.DbType);
        Assert.Equal(11, col.MaxLength);
        Assert.Equal(_fx.DomainName, col.ResolvedFromUserType);
    }

    [SkippableFact]
    public void CompositeType_IsCapturedWholeAndNotResolved()
    {
        // A composite has members rather than an underlying scalar, so there is
        // nothing to resolve it TO. Capturing the members is what lets a
        // refusal name the shape instead of saying "unmappable".
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var composite = _fx.Schema!.UserTypes[_fx.CompositeName];

        Assert.Equal(UserTypeKind.Composite, composite.Kind);
        Assert.Null(composite.UnderlyingDbType);
        Assert.Equal(new[] { "amount", "currency" }, composite.Members.Select(m => m.Name));
        Assert.Equal("numeric", composite.Members[0].DbType);
        Assert.Equal(12, composite.Members[0].Precision);
        Assert.Equal(2, composite.Members[0].Scale);
    }
}

public sealed class MySqlLiveFixture : IDisposable
{
    public bool Available { get; }
    public string? SkipReason { get; }
    public DatabaseSchema? Schema { get; }
    public string TableName { get; } = $"jauntyq_livetest_{Guid.NewGuid():N}";
    public string ViewName { get; }

    /// <summary>Spec 014: a two-argument stored function.</summary>
    public string FunctionName { get; } = $"jauntyq_livefn_{Guid.NewGuid():N}";

    /// <summary>Spec 014: a zero-argument stored function.</summary>
    public string NoArgFunctionName { get; } = $"jauntyq_livefnnoargs_{Guid.NewGuid():N}";

    private MySqlConnection? _conn;

    public MySqlLiveFixture()
    {
        ViewName = TableName + "_v";

        // No hard-coded fallback connection string: unset env var means
        // these tests soft-skip rather than guessing at local credentials.
        string? connStr = Environment.GetEnvironmentVariable("JAUNTYQ_TEST_MYSQL_CONNECTION");
        if (string.IsNullOrEmpty(connStr))
        {
            Available = false;
            SkipReason = LiveServerGate.NotSet("JAUNTYQ_TEST_MYSQL_CONNECTION", "MySQL");
            return;
        }

        // Only the connection-open step is soft-skip material. The original
        // shape wrapped the throwaway-table DDL and ExtractAsync in the same
        // catch, so a genuine MySqlExtractor bug (or a DDL incompatibility
        // with a real server) was silently reported as the same "not
        // reachable" skip -- a real regression would disappear as a skipped
        // test instead of a failure. Only a failed connection attempt is a
        // legitimate skip; everything after it must be allowed to throw and
        // fail these tests for real.
        try
        {
            _conn = new MySqlConnection(connStr);
            _conn.Open();
        }
        catch (Exception ex)
        {
            // Set-but-unreachable is a failure, not a skip; an unset variable
            // returned above, so this always throws.
            SkipReason = LiveServerGate.SkipOrThrow(
                "JAUNTYQ_TEST_MYSQL_CONNECTION", connStr, "MySQL", ex);
            Available = false;
            return;
        }

        using (var cmd = _conn.CreateCommand())
        {
            // CHARACTER SET is explicit so IsUnicode is deterministic
            // regardless of the server's default charset.
            cmd.CommandText = $@"
                CREATE TABLE {TableName} (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(40) CHARACTER SET utf8mb4 NOT NULL,
                    price DECIMAL(10,2) NULL,
                    INDEX {TableName}_name_idx (name)
                );";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _conn.CreateCommand())
        {
            // Separate command: MySqlConnector does not allow multiple
            // statements in one CommandText unless the connection string
            // opts in, and the table DDL above is deliberately single.
            cmd.CommandText = $"CREATE VIEW {ViewName} AS SELECT id, name, price FROM {TableName};";
            cmd.ExecuteNonQuery();
        }

        // Spec 014. MySQL has functions but no user-defined types of any kind,
        // so this covers FR-001 only -- and one statement per command, for the
        // multiple-statement reason above.
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $@"
                CREATE FUNCTION {FunctionName}(amount DECIMAL(12,2), rate DECIMAL(5,4))
                RETURNS DECIMAL(12,2) DETERMINISTIC
                RETURN amount * rate;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $@"
                CREATE FUNCTION {NoArgFunctionName}()
                RETURNS INT DETERMINISTIC
                RETURN 42;";
            cmd.ExecuteNonQuery();
        }

        Schema = new MySqlExtractor().ExtractAsync(connStr).GetAwaiter().GetResult();
        Available = true;
    }

    public void Dispose() => _conn?.Dispose();
}

[Trait("Category", "AuditRegression")]
public class MySqlExtractorLiveTests : IClassFixture<MySqlLiveFixture>
{
    private readonly MySqlLiveFixture _fx;
    public MySqlExtractorLiveTests(MySqlLiveFixture fx) => _fx = fx;

    [SkippableFact]
    public void Table_Extracted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        Assert.True(_fx.Schema!.Tables.ContainsKey(_fx.TableName));
    }

    [SkippableFact]
    public void IdColumn_IsIdentityPrimaryKey()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["id"];

        Assert.True(col.IsPrimaryKey);
        Assert.True(col.IsIdentity); // auto_increment
    }

    [SkippableFact]
    public void NameColumn_IsUnicodeVarcharWithMaxLength40()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["name"];

        Assert.Equal("varchar", col.DbType);
        Assert.Equal(40, col.MaxLength);
        Assert.True(col.IsUnicode);
        Assert.False(col.IsNullable);
    }

    [SkippableFact]
    public void PriceColumn_HasPrecisionAndScale()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var col = _fx.Schema!.Tables[_fx.TableName].Columns["price"];

        Assert.Equal(10, col.Precision);
        Assert.Equal(2, col.Scale);
        Assert.True(col.IsNullable);
    }

    [SkippableFact]
    public void NameIndex_Captured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var indexes = _fx.Schema!.Tables[_fx.TableName].Indexes;

        Assert.Contains(indexes, ix => ix.Columns.Contains("name"));
    }

    // ── Spec 015: views ────────────────────────────────────────────────────
    //
    // MySqlExtractor's widened table_type predicate had no live coverage when
    // 015 merged: SQLite, Postgres and SQL Server each had one, MySQL did not.
    // MySQL is the dialect where the distinction is least mechanical, because
    // information_schema.tables reports a view's TABLE_TYPE as 'VIEW' but
    // information_schema.columns lists its columns exactly like a base table's,
    // so a widened filter with no IsView flag would produce a writable-looking
    // relation that fails at runtime rather than at compile time.

    [SkippableFact]
    public void View_Extracted_AsNonInsertableRelation()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var view = _fx.Schema!.Tables[_fx.ViewName];
        Assert.True(view.IsView);
        Assert.False(view.IsInsertable);
        Assert.Equal(new[] { "id", "name", "price" }, view.Columns.Keys.OrderBy(k => k).ToArray());
    }

    [SkippableFact]
    public void BaseTable_IsNotFlaggedAsAView()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.False(_fx.Schema!.Tables[_fx.TableName].IsView);
        Assert.True(_fx.Schema!.Tables[_fx.TableName].IsInsertable);
    }

    // ── Spec 014: functions ───────────────────────────────────────────

    [SkippableFact]
    public void StoredFunction_IsCapturedWithItsParametersAndReturn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var fn = Assert.Single(_fx.Schema!.Functions.Values, f => f.Name == _fx.FunctionName);

        Assert.Equal(new[] { "amount", "rate" }, fn.Params.Select(p => p.Name));
        Assert.Equal(new[] { "decimal", "decimal" }, fn.Params.Select(p => p.DbType));
        Assert.Equal("decimal", fn.Return.DbType);
    }

    [SkippableFact]
    public void TheFunctionsReturnRowIsNotCapturedAsAParameter()
    {
        // MySQL's PARAMETERS view has no IS_RESULT column: a function's RETURN
        // is a row with ORDINAL_POSITION 0 and a null name, sitting in the same
        // result set as the arguments. Counted as a parameter it would give
        // every function one extra, unnamed argument -- and the emitted method
        // would demand it at every call site.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Equal(2, _fx.Schema!.Functions.Values.Single(f => f.Name == _fx.FunctionName).Params.Count);
        Assert.Empty(_fx.Schema!.Functions[_fx.NoArgFunctionName].Params);
    }

    [SkippableFact]
    public void MySqlCapturesNoUserTypes()
    {
        // Not an omission: MySQL has no DOMAIN, no composite type and no table
        // type, so FR-004 is a no-op for this dialect. Asserted rather than
        // assumed, because "empty" and "not implemented" look identical in a
        // snapshot and only one of them is correct.
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Empty(_fx.Schema!.UserTypes);
    }
}
