using JauntyQ.Generator;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R10-03 (§2.9 dialect-conditional fan-out mandate): every other dialect
/// comparison in the generator/analysis stack (SchemaSimulator.cs;
/// CodeEmitter.Part3.cs/Part11.cs; QueryValidator.Part2.cs; ProjectionBuilder.cs;
/// DialectMapper.cs itself) compares <c>schema.Dialect</c> case-insensitively
/// via <c>string.Equals(dialect, "x", StringComparison.OrdinalIgnoreCase)</c> --
/// matching <see cref="DialectMapper.IsKnownDialect"/>'s own
/// <c>StringComparer.OrdinalIgnoreCase</c> membership check (the one JNT7003
/// uses to accept or reject a schema snapshot's declared dialect).
///
/// CodeEmitter.Part4.cs's <c>BuildIdentityInsertSql</c> and CodeEmitter.Part6.cs's
/// <c>EmitUpsert</c> instead use a plain C# <c>switch (dialect) { case
/// "sqlserver": ... }</c>, which is case-SENSITIVE by construction (there is no
/// implicit case-folding on a string switch). <see cref="DatabaseSchema.Dialect"/>
/// is a bare string set directly from the JSON snapshot
/// (<c>SchemaLoader.Load</c> does a plain <c>JsonSerializer.Deserialize</c> with
/// no normalization), so a perfectly ordinary, JNT7003-ACCEPTED hand-authored
/// snapshot spelling the dialect in .NET/PascalCase convention -- e.g.
/// <c>"dialect": "SqlServer"</c>, matching the extractor class's own name --
/// falls through to each switch's <c>default:</c> arm:
/// <list type="bullet">
/// <item><c>BuildIdentityInsertSql</c>'s default silently returns the INSERT
/// SQL UNCHANGED -- an <c>-- @identity</c> INSERT then never appends "output
/// inserted.x" / "returning x" / "select last_insert_id()", so the
/// database-assigned identity is silently never read back (S2: no diagnostic
/// at all; W2: silent wrong runtime data -- the identity property keeps its
/// C# default instead of the real key).</item>
/// <item><c>EmitUpsert</c>'s default THROWS <see cref="System.InvalidOperationException"/>
/// -- an unhandled exception during source generation, which crashes the
/// entire generator run for any schema with an upsert-eligible table, with no
/// JNT-prefixed diagnostic pointing at the real cause.</item>
/// </list>
/// Both are reachable by any of the 4 known dialects spelled in anything other
/// than all-lowercase (R1: JNT7003 explicitly accepts case variants as valid,
/// so nothing warns the author away from this). Fixed by lowercasing the
/// switch subject in both methods, mirroring OrdinalIgnoreCase semantics.
/// </summary>
public class DialectCaseSensitivityTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("SQLSERVER")]
    [InlineData("sqlserver")]
    public void BuildIdentityInsertSql_SqlServer_AnyCasing_SplicesOutputClause(string dialect)
    {
        string sql = "insert into products (name) values (@name)";
        string result = CodeEmitter.BuildIdentityInsertSql(sql, dialect, "product_id");
        Assert.Equal("insert into products (name) OUTPUT INSERTED.product_id\nvalues (@name)", result);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("POSTGRES")]
    [InlineData("Sqlite")]
    [InlineData("SQLITE")]
    public void BuildIdentityInsertSql_PostgresOrSqlite_AnyCasing_AppendsReturningClause(string dialect)
    {
        string sql = "insert into products (name) values (@name)";
        string result = CodeEmitter.BuildIdentityInsertSql(sql, dialect, "product_id");
        Assert.Equal(sql + "\nRETURNING product_id", result);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MYSQL")]
    public void BuildIdentityInsertSql_MySql_AnyCasing_AppendsLastInsertIdSelect(string dialect)
    {
        string sql = "insert into products (name) values (@name)";
        string result = CodeEmitter.BuildIdentityInsertSql(sql, dialect, "product_id");
        Assert.Equal(sql + ";\nSELECT last_insert_id()", result);
    }

    private static TableSchema BuildCustomersTable() => new TableSchema
    {
        Name = "customers",
        Columns = new System.Collections.Generic.Dictionary<string, ColumnSchema>
        {
            ["customer_id"] = new ColumnSchema
            {
                Name = "customer_id", DbType = "varchar", IsNullable = false, IsPrimaryKey = true
            },
            ["company_name"] = new ColumnSchema
            {
                Name = "company_name", DbType = "varchar", IsNullable = false
            }
        }
    };

    [Theory]
    [InlineData("SqlServer", "MERGE INTO customers WITH (HOLDLOCK) AS target")]
    [InlineData("SQLSERVER", "MERGE INTO customers WITH (HOLDLOCK) AS target")]
    public void EmitUpsert_SqlServer_AnyCasing_DoesNotThrow_EmitsMergeSql(string dialect, string expectedFragment)
    {
        string sql = CodeEmitter.EmitUpsert("Customer", BuildCustomersTable(), dialect);
        Assert.Contains(expectedFragment, sql);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("POSTGRES")]
    [InlineData("Sqlite")]
    [InlineData("SQLITE")]
    public void EmitUpsert_PostgresOrSqlite_AnyCasing_DoesNotThrow_EmitsOnConflictSql(string dialect)
    {
        string sql = CodeEmitter.EmitUpsert("Customer", BuildCustomersTable(), dialect);
        Assert.Contains("ON CONFLICT (customer_id) DO UPDATE SET", sql);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MYSQL")]
    public void EmitUpsert_MySql_AnyCasing_DoesNotThrow_EmitsOnDuplicateKeySql(string dialect)
    {
        string sql = CodeEmitter.EmitUpsert("Customer", BuildCustomersTable(), dialect);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
    }
}
