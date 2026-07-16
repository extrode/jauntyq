using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Generator;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class SchemaScenarioTests
{
    #region Relationship Stress Schema

    private const string RelationshipsSchemaJson = @"{
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""username"": { ""name"": ""username"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""email"": { ""name"": ""email"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    },
    ""roles"": {
      ""name"": ""roles"",
      ""columns"": {
        ""role_id"": { ""name"": ""role_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""role_name"": { ""name"": ""role_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    },
    ""permissions"": {
      ""name"": ""permissions"",
      ""columns"": {
        ""permission_id"": { ""name"": ""permission_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""permission_name"": { ""name"": ""permission_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    },
    ""user_roles"": {
      ""name"": ""user_roles"",
      ""columns"": {
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""role_id"": { ""name"": ""role_id"", ""dbType"": ""int"", ""isNullable"": false }
      }
    },
    ""role_permissions"": {
      ""name"": ""role_permissions"",
      ""columns"": {
        ""role_id"": { ""name"": ""role_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""permission_id"": { ""name"": ""permission_id"", ""dbType"": ""int"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""user_roles"", ""fromColumn"": ""user_id"", ""toTable"": ""users"", ""toColumn"": ""user_id"" },
    { ""fromTable"": ""user_roles"", ""fromColumn"": ""role_id"", ""toTable"": ""roles"", ""toColumn"": ""role_id"" },
    { ""fromTable"": ""role_permissions"", ""fromColumn"": ""role_id"", ""toTable"": ""roles"", ""toColumn"": ""role_id"" },
    { ""fromTable"": ""role_permissions"", ""fromColumn"": ""permission_id"", ""toTable"": ""permissions"", ""toColumn"": ""permission_id"" }
  ]
}";

    private static DatabaseSchema LoadRelationshipsSchema() =>
        SchemaLoader.Load(RelationshipsSchemaJson);

    [Fact]
    public void RelationshipsSchema_LoadsAllTables()
    {
        var schema = LoadRelationshipsSchema();

        Assert.Equal(5, schema.Tables.Count);
        Assert.True(schema.Tables.ContainsKey("users"));
        Assert.True(schema.Tables.ContainsKey("roles"));
        Assert.True(schema.Tables.ContainsKey("permissions"));
        Assert.True(schema.Tables.ContainsKey("user_roles"));
        Assert.True(schema.Tables.ContainsKey("role_permissions"));
    }

    [Fact]
    public void RelationshipsSchema_ForeignKeysCorrect()
    {
        var schema = LoadRelationshipsSchema();

        Assert.Equal(4, schema.ForeignKeys.Count);
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "user_roles" && fk.FromColumn == "user_id" &&
            fk.ToTable == "users" && fk.ToColumn == "user_id");
        Assert.Contains(schema.ForeignKeys, fk =>
            fk.FromTable == "role_permissions" && fk.FromColumn == "permission_id" &&
            fk.ToTable == "permissions" && fk.ToColumn == "permission_id");
    }

    [Fact]
    public void ManyToMany_MultiJoinValidation()
    {
        // users -> user_roles -> roles (three-table join through junction)
        var sql = @"
select u.username, r.role_name
from users u
join user_roles ur on u.user_id = ur.user_id
join roles r on ur.role_id = r.role_id
where u.user_id = @userId";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetUserRoles");
        var errors = QueryValidator.Validate(query, LoadRelationshipsSchema());

        Assert.Empty(errors);
        Assert.Equal(3, query.Tables.Count);
        Assert.Equal(2, query.Joins.Count);
    }

    [Fact]
    public void ManyToMany_FullChainProjection()
    {
        // users -> user_roles -> roles -> role_permissions -> permissions (five-table join)
        var sql = @"
select u.username, r.role_name, p.permission_name
from users u
join user_roles ur on u.user_id = ur.user_id
join roles r on ur.role_id = r.role_id
join role_permissions rp on r.role_id = rp.role_id
join permissions p on rp.permission_id = p.permission_id
where u.user_id = @userId";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetUserPermissions");
        var schema = LoadRelationshipsSchema();

        var errors = QueryValidator.Validate(query, schema);
        Assert.Empty(errors);

        var projection = ProjectionBuilder.Build(query, schema);
        Assert.Equal("GetUserPermissions", projection.Name);
        Assert.Equal(3, projection.Columns.Count);
        Assert.Equal("Username", projection.Columns[0].Name);
        Assert.Equal("string", projection.Columns[0].Type);
        Assert.Equal("RoleName", projection.Columns[1].Name);
        Assert.Equal("PermissionName", projection.Columns[2].Name);
    }

    #endregion

    #region Edge Case Schema

    private const string EdgeCaseSchemaJson = @"{
  ""tables"": {
    ""employees"": {
      ""name"": ""employees"",
      ""columns"": {
        ""employee_id"": { ""name"": ""employee_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""first_name"": { ""name"": ""first_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""last_name"": { ""name"": ""last_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""manager_id"": { ""name"": ""manager_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""hire_date"": { ""name"": ""hire_date"", ""dbType"": ""date"", ""isNullable"": false },
        ""salary"": { ""name"": ""salary"", ""dbType"": ""decimal"", ""isNullable"": false },
        ""is_active"": { ""name"": ""is_active"", ""dbType"": ""bool"", ""isNullable"": false }
      }
    },
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""employee_id"": { ""name"": ""employee_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""total"": { ""name"": ""total"", ""dbType"": ""decimal(10,2)"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false },
        ""notes"": { ""name"": ""notes"", ""dbType"": ""text"", ""isNullable"": true }
      }
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""employees"", ""fromColumn"": ""manager_id"", ""toTable"": ""employees"", ""toColumn"": ""employee_id"" },
    { ""fromTable"": ""orders"", ""fromColumn"": ""employee_id"", ""toTable"": ""employees"", ""toColumn"": ""employee_id"" }
  ]
}";

    private static DatabaseSchema LoadEdgeCaseSchema() =>
        SchemaLoader.Load(EdgeCaseSchemaJson);

    [Fact]
    public void SelfJoin_ValidatesCorrectly()
    {
        var sql = @"
select e.first_name, e.last_name, m.first_name as manager_name
from employees e
left join employees m on e.manager_id = m.employee_id";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetEmployeesWithManagers");
        var errors = QueryValidator.Validate(query, LoadEdgeCaseSchema());

        Assert.Empty(errors);
        Assert.Equal(2, query.Tables.Count);
        Assert.Equal("employees", query.Tables[0].TableName);
        Assert.Equal("employees", query.Tables[1].TableName);
    }

    [Fact]
    public void SelfJoin_ProjectionCorrect()
    {
        var sql = @"
select e.first_name, e.last_name, m.first_name as manager_name
from employees e
left join employees m on e.manager_id = m.employee_id";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetEmployeesWithManagers");
        var schema = LoadEdgeCaseSchema();
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Equal(3, projection.Columns.Count);
        Assert.Equal("FirstName", projection.Columns[0].Name);
        Assert.Equal("string", projection.Columns[0].Type);
        Assert.Equal("LastName", projection.Columns[1].Name);
        Assert.Equal("ManagerName", projection.Columns[2].Name);
        // m is the LEFT-joined side of the self-join: an employee with no
        // manager (manager_id IS NULL) produces NULL for every m.* column,
        // even though employees.first_name is NOT NULL in the schema.
        Assert.Equal("string?", projection.Columns[2].Type);
    }

    [Fact]
    public void NullableForeignKey_ParameterTypeInferred()
    {
        // manager_id is nullable int — param should infer as int?
        var sql = @"
select e.first_name from employees e
where e.manager_id = @managerId";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetByManager");
        var schema = LoadEdgeCaseSchema();
        var projection = ProjectionBuilder.Build(query, schema);

        var paramType = CodeEmitter.InferParameterType("managerId", query, projection, schema);
        Assert.Equal("int?", paramType);
    }

    [Fact]
    public void OrdersTable_NullableFk_ValidatesCorrectly()
    {
        var sql = @"
select o.order_id, o.total
from orders o
where o.employee_id = @employeeId";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetOrders");
        var errors = QueryValidator.Validate(query, LoadEdgeCaseSchema());

        Assert.Empty(errors);
    }

    [Fact]
    public void MixedNullability_ProjectionTypes()
    {
        var sql = @"
select o.order_id, o.employee_id, o.total, o.notes
from orders o";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetAllOrders");
        var schema = LoadEdgeCaseSchema();
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Equal(4, projection.Columns.Count);
        Assert.Equal("int", projection.Columns[0].Type);       // order_id NOT NULL
        Assert.Equal("int?", projection.Columns[1].Type);      // employee_id nullable
        Assert.Equal("decimal", projection.Columns[2].Type);   // total NOT NULL
        Assert.Equal("string?", projection.Columns[3].Type);   // notes nullable (text -> string?)
    }

    [Fact]
    public void DateAndBoolTypes_MappedCorrectly()
    {
        var sql = @"
select e.hire_date, e.salary, e.is_active
from employees e
where e.employee_id = @employeeId";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "GetEmployeeDetails");
        var schema = LoadEdgeCaseSchema();
        var projection = ProjectionBuilder.Build(query, schema);

        Assert.Equal(3, projection.Columns.Count);
        Assert.Equal("System.DateTime", projection.Columns[0].Type);
        Assert.Equal("decimal", projection.Columns[1].Type);
        Assert.Equal("bool", projection.Columns[2].Type);
    }

    [Fact]
    public void SelfJoin_InvalidColumn_ReportsError()
    {
        var sql = @"
select e.first_name, m.nonexistent
from employees e
left join employees m on e.manager_id = m.employee_id";

        var tokens = SqlTokenizer.Tokenize(sql);
        var query = SqlParser.SqlParser.Parse(tokens, "BadSelfJoin");
        var errors = QueryValidator.Validate(query, LoadEdgeCaseSchema());

        Assert.Contains(errors, e => e.Code == "JNT2002" && e.Message.Contains("nonexistent"));
    }

    #endregion
}
