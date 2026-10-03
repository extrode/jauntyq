using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.NameCollisions;

internal static class NameCollisionHarness
{
    internal static string Table(string name, string pk, params (string col, string type, bool nullable)[] cols)
    {
        var parts = new List<string> { $@"""{pk}"": {{ ""name"": ""{pk}"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true }}" };
        foreach (var (col, type, nullable) in cols)
            parts.Add($@"""{col}"": {{ ""name"": ""{col}"", ""dbType"": ""{type}"", ""isNullable"": {(nullable ? "true" : "false")} }}");
        return $@"""{name}"": {{ ""name"": ""{name}"", ""columns"": {{ {string.Join(", ", parts)} }} }}";
    }

    internal static string Schema(string dialect, string tables, string procedures = "") =>
        $@"{{ ""dialect"": ""{dialect}"", {procedures} ""tables"": {{ {tables} }}, ""foreignKeys"": [] }}";

    internal static GeneratorDriverRunResult Generate(string site, string name, string dialect)
    {
        var none = Array.Empty<(string, string)>();
        string orders(params (string, string, bool)[] cols) => Table("orders", "id", cols);
        switch (site)
        {
            case "column":
                return Run(none, Schema(dialect, orders((name, "int", false), ("note", "varchar", true))), null, true);
            case "scope":
                return Run(new[] { (ScopePath, $@"{{ ""scopes"": [ {{ ""table"": ""orders"", ""column"": ""{name}"" }} ] }}") },
                    Schema(dialect, orders((name, "int", false), ("note", "varchar", true))), null, true);
            case "pk":
                return Run(none, Schema(dialect, Table("orders", name, ("note", "varchar", true))), null, true);
            case "qparam":
                return Run(none, Schema(dialect, orders(("note", "varchar", true))), null, false,
                    ("db/Orders/Find.sql", $"SELECT id, note FROM orders WHERE note = @{name}"));
            case "alias":
                return Run(none, Schema(dialect, orders(("note", "varchar", true))), null, false,
                    ("db/Orders/Find.sql", $"SELECT id AS {Quote(name, dialect)}, note FROM orders"));
            case "procparam":
                var proc = $@"""procedures"": {{ ""find_orders"": {{ ""name"": ""find_orders"", ""params"": [ {{ ""name"": ""{name}"", ""dbType"": ""int"", ""direction"": ""In"", ""isNullable"": false }} ], ""results"": [] }} }},";
                return Run(none, Schema(dialect, orders(("note", "varchar", true)), proc), null, false,
                    ("db/Orders/Call.sql", "-- @call find_orders\n"));
            case "table":
                return Run(none, Schema(dialect, Table(name, "id", ("note", "varchar", true))), null, true);
        }
        throw new ArgumentException(site);
    }

    internal static string Quote(string name, string dialect) => dialect switch
    {
        "sqlserver" => $"[{name}]",
        "mysql" => $"`{name}`",
        _ => $"\"{name}\"",
    };

    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlConnection).Assembly.Location),
        };
        foreach (var dll in new[] { "System.Runtime.dll", "System.Data.Common.dll", "System.ComponentModel.Primitives.dll",
                     "System.Threading.Tasks.dll", "System.Collections.dll", "System.Collections.NonGeneric.dll",
                     "System.Linq.dll", "System.Runtime.Extensions.dll", "netstandard.dll", "System.Memory.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }
        return refs.ToArray();
    });

    internal static IEnumerable<Diagnostic> CompileErrorDiagnostics(GeneratorDriverRunResult result) =>
        CSharpCompilation.Create("Probe", result.GeneratedTrees, References.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);

    internal static string SourceOf(GeneratorDriverRunResult result, string file)
    {
        var tree = result.GeneratedTrees.SingleOrDefault(t => Path.GetFileName(t.FilePath) == file)
            ?? throw new InvalidOperationException($"{file} not generated: {string.Join(", ", result.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)))}");
        return tree.GetText().ToString();
    }

    internal static List<string> CompileErrors(string site, string name, string dialect) =>
        CompileErrorDiagnostics(Generate(site, name, dialect)).Select(d => $"{d.Id}: {d.GetMessage()}").ToList();
}
