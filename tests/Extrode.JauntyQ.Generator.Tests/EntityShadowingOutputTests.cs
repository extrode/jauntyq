using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class EntityShadowingOutputTests
{
    private static readonly string[] ShadowedNames =
    {
        "ArgumentException", "ArgumentNullException", "Array", "Convert", "DbEnumerator", "DbType",
        "IAsyncEnumerable", "IDisposable", "IEnumerable", "IEnumerator", "IndexOutOfRangeException",
        "InvalidOperationException", "List", "Math", "MySqlBulkCopy", "MySqlBulkCopyColumnMapping",
        "MySqlConnection", "MySqlTransaction", "NpgsqlBinaryImporter", "NpgsqlConnection", "NpgsqlDbType",
        "NpgsqlParameter", "SqlBulkCopy", "SqlBulkCopyOptions", "SqlConnection", "SqlTransaction",
        "StringComparison", "Task", "Type", "Volatile",
    };

    [Theory]
    [MemberData(nameof(GeneratedOutputApprovalTests.Dialects), MemberType = typeof(GeneratedOutputApprovalTests))]
    public void EntityShadowingAFrameworkType_EveryReferenceKeepsItsNamespace(string dialect)
    {
        string output = GeneratedOutputApprovalTests.Generate(
            Path.Combine(GeneratedOutputApprovalTests.ApprovedDir(), dialect),
            (file, text) => file == "jaunty.schema.json" ? AddShadowingTables(text) : text,
            dialect is "postgres" or "mysql" ? QueryBindingScenarioTests.EnumEachQueries : Array.Empty<(string, string)>());

        Assert.Contains("global::System.Threading.Tasks.Task", output);
        foreach (string name in ShadowedNames)
            Assert.DoesNotMatch(new Regex($@"global::{name}\b"), output);
    }

    private static string AddShadowingTables(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson)!.AsObject();
        var tables = root["tables"]!.AsObject();
        string idType = (string)tables["customers"]!["columns"]!["id"]!["dbType"]!;
        foreach (string name in ShadowedNames)
        {
            tables[name] = new JsonObject
            {
                ["name"] = name,
                ["columns"] = new JsonObject
                {
                    ["id"] = new JsonObject { ["name"] = "id", ["dbType"] = idType, ["isNullable"] = false, ["isPrimaryKey"] = true },
                },
            };
        }
        return root.ToJsonString();
    }
}
