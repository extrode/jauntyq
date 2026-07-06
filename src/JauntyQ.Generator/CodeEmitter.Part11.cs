using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitProcCallBody(
        System.Text.StringBuilder sb,
        string methodName,
        JauntyQ.Schema.ProcedureSchema procedure,
        string resultType,
        bool hasResults,
        string connVar,
        bool isStatic,
        bool isAsync,
        string? dialect)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        // Return: List<Result> for row-returning procs, else int (row count).
        string syncReturn = hasResults ? $"System.Collections.Generic.List<{resultType}>" : "int";
        string declaredReturn = isAsync ? $"System.Threading.Tasks.Task<{syncReturn}>" : syncReturn;
        string name = isAsync ? $"{methodName}Async" : methodName;

        // Parameter list: IN params by value, OUT/INOUT params as C# `out`.
        var parts = new System.Collections.Generic.List<string>();
        if (isStatic)
            parts.Add("System.Data.Common.DbConnection conn");
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out)
                parts.Add($"out {ct} {pname}");
            else if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.InOut)
                parts.Add($"ref {ct} {pname}");
            else
                parts.Add($"{ct} {pname}");
        }
        if (isAsync)
            parts.Add("System.Threading.CancellationToken cancellationToken = default");

        sb.AppendLine($"        {modifier}{asyncModifier} {declaredReturn} {name}({string.Join(", ", parts)})");
        sb.AppendLine("        {");

        // Pre-assign out params so the method is definitely-assigned on all paths.
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out)
            {
                string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
                sb.AppendLine($"            {pname} = default;");
            }
        }

        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                using var cmd = {connVar}.CreateCommand();");
        if (!isStatic)
            sb.AppendLine("                if (_db?.CurrentTransaction != null) cmd.Transaction = _db.CurrentTransaction;");
        sb.AppendLine($"                cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procedure.Name)}\";");
        sb.AppendLine("                cmd.CommandType = System.Data.CommandType.StoredProcedure;");

        // Bind parameters. Track OUT/INOUT parameter variable names for readback.
        var outReadback = new System.Collections.Generic.List<(string ParamVar, string CSharpName, string CSharpType, bool Nullable)>();
        int idx = 0;
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            string varName = $"p{idx++}";
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            sb.AppendLine($"                var {varName} = cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{IdentifierGuard.ToStringLiteral(p.Name)}\";");
            string? adoDbType = MapCSharpTypeToAdoDbType(ct);
            if (adoDbType != null)
                sb.AppendLine($"                {varName}.DbType = System.Data.DbType.{adoDbType};");
            switch (p.Direction)
            {
                case JauntyQ.Schema.ProcedureParamDirection.Out:
                    sb.AppendLine($"                {varName}.Direction = System.Data.ParameterDirection.Output;");
                    if (p.MaxLength is int ml && ml != 0)
                        sb.AppendLine($"                {varName}.Size = {ml};");
                    outReadback.Add((varName, pname, ct, p.IsNullable || !IsNonNullableValueType(ct)));
                    break;
                case JauntyQ.Schema.ProcedureParamDirection.InOut:
                    sb.AppendLine($"                {varName}.Direction = System.Data.ParameterDirection.InputOutput;");
                    if (p.MaxLength is int ml2 && ml2 != 0)
                        sb.AppendLine($"                {varName}.Size = {ml2};");
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? System.DBNull.Value;");
                    outReadback.Add((varName, pname, ct, p.IsNullable || !IsNonNullableValueType(ct)));
                    break;
                default:
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? System.DBNull.Value;");
                    break;
            }
            sb.AppendLine($"                cmd.Parameters.Add({varName});");
        }
        sb.AppendLine();

        if (hasResults)
        {
            string behavior = "System.Data.CommandBehavior.SingleResult";
            sb.AppendLine(isAsync
                ? $"                using (var reader = await cmd.ExecuteReaderAsync({behavior}, cancellationToken).ConfigureAwait(false))"
                : $"                using (var reader = cmd.ExecuteReader({behavior}))");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var results = new System.Collections.Generic.List<{resultType}>();");
            string readCall = isAsync ? "await reader.ReadAsync(cancellationToken).ConfigureAwait(false)" : "reader.Read()";
            sb.AppendLine($"                    while ({readCall})");
            sb.AppendLine($"                        results.Add(__Map{methodName}(reader));");
            EmitProcOutReadback(sb, outReadback, "                    ");
            sb.AppendLine("                    return results;");
            sb.AppendLine("                }");
        }
        else
        {
            sb.AppendLine(isAsync
                ? "                int __affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);"
                : "                int __affected = cmd.ExecuteNonQuery();");
            EmitProcOutReadback(sb, outReadback, "                ");
            sb.AppendLine("                return __affected;");
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>Reads OUT/INOUT parameter values back into the C# out/ref args.</summary>
    private static void EmitProcOutReadback(
        System.Text.StringBuilder sb,
        System.Collections.Generic.List<(string ParamVar, string CSharpName, string CSharpType, bool Nullable)> outParams,
        string indent)
    {
        foreach (var (paramVar, csName, csType, nullable) in outParams)
        {
            string baseType = csType.EndsWith("?") ? csType.Substring(0, csType.Length - 1) : csType;
            // DBNull -> default; otherwise unbox to the declared type.
            sb.AppendLine($"{indent}{csName} = {paramVar}.Value is null || {paramVar}.Value is System.DBNull ? default! : ({csType})({baseType}){paramVar}.Value;");
        }
    }

    /// <summary>
    /// Emits BulkInsert(IEnumerable&lt;Row&gt;): inserts many rows in one
    /// transaction using a single prepared command whose parameter values are
    /// reassigned per row. Dialect-portable, allocation-light, and far faster
    /// than N autocommitted round-trips. (Provider-native fast paths —
    /// SqlBulkCopy / Npgsql binary COPY — are a future optimization; see
    /// docs/01-roadmap/ROADMAP.md.) Identity and rowversion columns are database-assigned
    /// and excluded. Returns the number of rows inserted.
    /// </summary>
    public static string EmitBulkInsert(string entityName, string rowType, TableSchema tableSchema, string dialect)
    {
        var cols = tableSchema.Columns.Values.Where(c => !c.IsIdentity && !c.IsRowVersion).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");

        EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, "_conn", isStatic: false, isAsync: false);
        sb.AppendLine();
        EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, "conn", isStatic: true, isAsync: false);
        sb.AppendLine();
        EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, "_conn", isStatic: false, isAsync: true);
        sb.AppendLine();
        EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, "conn", isStatic: true, isAsync: true);

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

}
