using JauntyQ.Generator;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Covers WHERE-clause predicate subqueries validated inside CTE chains, the
/// Users_Delete-shaped acceptance case, and the RETURNING/CTE statement
/// terminator (';') regressions.
/// </summary>
public class SubqueryAndTerminatorTests
{
    // Schema shaped like the epass users/applications area used by the
    // acceptance case: users(id), applications(id, created_by_user_id),
    // email_addresses(user_id, application_id).
    private static DatabaseSchema CreateSchema()
    {
        return new DatabaseSchema
        {
            Tables = new Dictionary<string, TableSchema>
            {
                ["users"] = new TableSchema
                {
                    Name = "users",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsNullable = false, IsPrimaryKey = true }
                    }
                },
                ["applications"] = new TableSchema
                {
                    Name = "applications",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsNullable = false, IsPrimaryKey = true },
                        ["created_by_user_id"] = new ColumnSchema { Name = "created_by_user_id", DbType = "int", IsNullable = false }
                    }
                },
                ["email_addresses"] = new TableSchema
                {
                    Name = "email_addresses",
                    Columns = new Dictionary<string, ColumnSchema>
                    {
                        ["user_id"] = new ColumnSchema { Name = "user_id", DbType = "int", IsNullable = true },
                        ["application_id"] = new ColumnSchema { Name = "application_id", DbType = "int", IsNullable = true }
                    }
                }
            },
            ForeignKeys = new List<ForeignKeySchema>()
        };
    }

    private static QueryModel ParseSql(string sql, string name = "TestQuery")
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        return SqlParser.SqlParser.Parse(tokens, name);
    }

    [Fact]
    public void Subquery_ReferencingEarlierCte_NoErrors()
    {
        // The subquery's FROM references a CTE declared earlier in the chain;
        // that name resolves as an in-scope virtual table, not a missing table.
        var query = ParseSql(
            "with owned_apps as (select id from applications where created_by_user_id = @id) " +
            "delete from email_addresses where application_id in (select id from owned_apps)");

        var errors = QueryValidator.Validate(query, CreateSchema());

        Assert.DoesNotContain(errors, e => e.Code == "JNT2001");
        Assert.DoesNotContain(errors, e => e.Code == "JNT1001");
        Assert.Empty(errors);
    }

    [Fact]
    public void UsersDelete_Acceptance_NoErrors()
    {
        var query = ParseSql(
            "with owned_apps as (select id from applications where created_by_user_id = @id), " +
            "d1 as (delete from email_addresses where user_id = @id or application_id in (select id from owned_apps)), " +
            "d2 as (delete from email_addresses where user_id = @id or application_id in (select id from owned_apps)) " +
            "delete from users where id = @id",
            "Users_Delete");

        var errors = QueryValidator.Validate(query, CreateSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void Subquery_InsideCteBody_UnknownColumn_JNT2002()
    {
        var query = ParseSql(
            "with owned_apps as (select id from applications where created_by_user_id = @id) " +
            "delete from email_addresses where application_id in (select nonexistent from applications)");

        var errors = QueryValidator.Validate(query, CreateSchema());

        Assert.Contains(errors, e => e.Code == "JNT2002" && e.Message.Contains("nonexistent"));
    }

    [Fact]
    public void ReturningPlainColumn_TrailingSemicolon_ParsesAsColumn()
    {
        // Regression: a trailing ';' must terminate the RETURNING projection
        // item instead of being swallowed into it (which demoted the plain
        // column to an alias-less expression -> JNT3004).
        var query = ParseSql("delete from users where id = @id returning id;");

        Assert.True(query.HasReturning);
        Assert.Single(query.Returning);
        Assert.False(query.Returning[0].IsExpression);
        Assert.Equal("id", query.Returning[0].ColumnName);
        Assert.Empty(query.ExpressionsMissingAlias);
    }

    [Fact]
    public void ReturningExpression_TrailingSemicolon_KeepsAlias()
    {
        var query = ParseSql(
            "delete from users where id = @id returning (id is not null) as present;");

        Assert.True(query.HasReturning);
        Assert.Single(query.Returning);
        Assert.True(query.Returning[0].IsExpression);
        Assert.Equal("present", query.Returning[0].OutputAlias);
        Assert.Empty(query.ExpressionsMissingAlias);
    }

    [Fact]
    public void CteChain_TrailingSemicolon_NoErrors()
    {
        var query = ParseSql(
            "with owned_apps as (select id from applications where created_by_user_id = @id) " +
            "delete from email_addresses where application_id in (select id from owned_apps);");

        var errors = QueryValidator.Validate(query, CreateSchema());

        Assert.Empty(errors);
        Assert.Equal(StatementType.Delete, query.StatementType);
        Assert.Equal("email_addresses", query.TargetTable);
    }
}
