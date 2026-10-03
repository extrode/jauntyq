using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

/// <summary>
/// The statement a parameter binding resolves in. A binding carried up from a
/// subquery or CTE body records that body as <see cref="ParameterRef.BoundScope"/>,
/// but the parser has no schema: an unqualified column the body's own tables do
/// not have is a correlated reference to the enclosing statement, as in
/// <c>exists (select 1 from items where status = @s)</c> with <c>status</c> only
/// on the outer table. That binding resolves in the enclosing statement.
/// </summary>
internal static class BindingScope
{
    public static QueryModel Of(ParameterRef param, QueryModel query, DatabaseSchema? schema)
    {
        var scope = param.BoundScope;
        if (scope == null || schema == null || !string.IsNullOrEmpty(param.BoundTableAlias) ||
            string.IsNullOrEmpty(param.BoundColumnName))
            return scope ?? query;
        if (!HasColumn(scope, param.BoundColumnName, schema) && HasColumn(query, param.BoundColumnName, schema))
            return query;
        return scope;
    }

    private static bool HasColumn(QueryModel model, string column, DatabaseSchema schema)
    {
        if (model.TargetTable != null && TableHasColumn(model.TargetTable, column, schema))
            return true;
        foreach (var table in model.Tables)
        {
            if (TableHasColumn(table.TableName, column, schema))
                return true;
        }
        return false;
    }

    private static bool TableHasColumn(string table, string column, DatabaseSchema schema) =>
        SchemaLookup.TryGetTable(schema, table, out var tableSchema) &&
        SchemaLookup.TryGetColumn(tableSchema!, column, out _);
}
