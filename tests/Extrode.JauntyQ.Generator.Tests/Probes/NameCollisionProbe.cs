using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Extrode.JauntyQ.Generator.Tests.NameCollisions.NameCollisionHarness;

namespace Extrode.JauntyQ.Generator.Tests.Probes;

public class NameCollisionProbe
{
    private static readonly string[] Names =
    {
        "conn", "connection", "transaction", "tx", "ownTx", "cancellationToken", "ct", "token",
        "row", "rows", "id", "ordinal", "name", "value", "column", "data", "i", "n", "length",
        "reader", "cmd", "command", "result", "results", "item", "items", "p", "parameters",
        "count", "affected", "db", "entity", "list", "sql", "e", "ex", "task",
        "order", "orders", "note", "Order", "Orders",
        "__cmd", "__reader", "__results", "__count", "__p", "__weOpened", "__conn", "__row", "__rows",
        "__transaction", "__cancellationToken", "__id", "__affected", "__Map", "__ScopeRows",
        "__bulkCopy", "__importer", "__npgsqlConn", "__warnCmd", "__each_x", "__i_0",
        "Task", "List", "DbConnection", "DbCommand", "DbTransaction", "DbDataReader", "DbParameter",
        "CancellationToken", "Exception", "Convert", "DBNull", "Math", "Array", "String", "Object",
        "Guid", "DateTime", "Decimal", "JauntyDb", "IEnumerable", "ValueTask", "Enumerable",
        "System", "Extrode", "Generated", "Nullable", "Func", "Action", "Dictionary",
        "StringComparison", "Type", "Int32", "Linq", "Microsoft", "Npgsql", "MySqlConnector",
        "GetAll", "GetById", "Insert", "Update", "Delete", "Upsert", "BulkInsert", "InsertAsync",
        "Find", "Call", "Equals", "GetHashCode", "ToString", "GetType",
        "class", "event", "params", "object", "string", "int", "this", "base", "default", "new",
        "return", "var", "async", "await", "dynamic", "get", "set", "record", "nameof", "global",
        "transactions", "connections", "commands", "sessions", "events", "types", "values", "tasks",
        "users", "readers", "Transactions", "Connection", "Transaction", "Session",
        "ConnectionState", "CommandBehavior", "CommandType", "ParameterDirection", "InvalidOperationException",
        "ArgumentNullException", "Enum", "Span", "Interlocked", "JauntyQShapeGuard", "Read", "Map", "Create",
        "_conn", "_tx", "_transaction", "_connection", "_db", "_jauntyDb", "openedNow", "inner",
        "CurrentTransaction", "Dispose", "DisposeAsync", "BeginTransaction", "Commit", "Rollback", "Core",
        "OrderRow", "Row", "jaunty_db", "order_row",
        "read", "map", "create", "commit", "rollback", "dispose", "core", "connection_state", "command_behavior",
        "current_transaction", "begin_transaction", "jauntyq_shape_guard", "bulk_insert", "get_all", "get_by_id",
        "upsert", "insert", "update", "delete", "insert_async", "find", "call", "db_null", "date_time", "guid",
        "to_string", "get_type", "equals", "get_hash_code", "system", "microsoft", "task", "list", "exception",
    };

    private static readonly string[] Dialects = { "sqlite", "postgres", "sqlserver", "mysql" };
    private static readonly string[] Sites = { "column", "scope", "pk", "qparam", "alias", "procparam", "table" };

    private sealed record Case(string Site, string Name, string Dialect, string Outcome,
        List<string> GeneratorDiagnostics, List<string> CompileErrors, int GeneratedFiles, string? Exception);

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("JAUNTYQ_NAME_PROBE") != "1")
            return;

        var cases = new ConcurrentBag<Case>();
        var work = from site in Sites from name in Names from dialect in Dialects
                   where !(site == "procparam" && dialect == "sqlite")
                   select (site, name, dialect);
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, w =>
        {
            var result = Generate(w.site, w.name, w.dialect);
            var exception = result.Results.Select(r => r.Exception?.Message).FirstOrDefault(m => m != null);
            var diags = result.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Select(d => $"{d.Id} {d.Severity}: {d.GetMessage()}").Distinct().ToList();
            var errors = CompileErrorDiagnostics(result)
                .Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    var file = Path.GetFileName(span.Path);
                    var line = d.Location.SourceTree?.GetText().Lines[span.StartLinePosition.Line].ToString().Trim() ?? "";
                    return $"{d.Id} {file}:{span.StartLinePosition.Line + 1}: {d.GetMessage()} | {line}";
                })
                .Distinct().ToList();
            string outcome = exception != null ? "generator-exception"
                : errors.Count > 0 ? "compile-error"
                : result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? "diagnostic-error"
                : diags.Count > 0 ? "warning"
                : "ok";
            cases.Add(new Case(w.site, w.name, w.dialect, outcome, diags, errors.Take(6).ToList(), result.GeneratedTrees.Length, exception));
        });

        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir)!;
        var ordered = cases.OrderBy(c => c.Site).ThenBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Dialect).ToList();
        File.WriteAllText(Path.Combine(dir, "tmp", "name-collision-probe.json"),
            JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }));
    }
}
