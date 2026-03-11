using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

[Generator(LanguageNames.CSharp)]
public class JauntyQGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect SQL files
        var sqlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

        // Collect schema files
        var schemaFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase));

        // Combine: all SQL files + the first schema file
        var schemaText = schemaFiles.Collect().Select(static (files, _) =>
            files.IsEmpty ? null : files[0].GetText()?.ToString());

        var combined = sqlFiles.Combine(schemaText);

        context.RegisterSourceOutput(combined, static (ctx, pair) =>
        {
            var (sqlFile, schemaJson) = pair;
            Execute(ctx, sqlFile, schemaJson);
        });
    }

    private static void Execute(
        SourceProductionContext context,
        AdditionalText sqlFile,
        string? schemaJson)
    {
        var sqlText = sqlFile.GetText(context.CancellationToken)?.ToString();
        if (string.IsNullOrWhiteSpace(sqlText))
            return;

        // Derive query name from filename
        string fileName = System.IO.Path.GetFileNameWithoutExtension(sqlFile.Path);

        // Tokenize
        var tokens = SqlTokenizer.Tokenize(sqlText!);

        // Parse
        var queryModel = SqlParser.SqlParser.Parse(tokens, fileName);

        // Load schema
        DatabaseSchema? schema = null;
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                schema = SchemaLoader.Load(schemaJson!);
            }
            catch
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.SchemaLoadFailed, Location.None, sqlFile.Path));
                return;
            }
        }

        // Validate
        var errors = QueryValidator.Validate(queryModel, schema);
        bool hasErrors = false;
        foreach (var error in errors)
        {
            var severity = error.Severity == ValidationSeverity.Warning
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Error;

            var descriptor = new DiagnosticDescriptor(
                error.Code,
                error.Code,
                error.Message,
                "JauntyQ",
                severity,
                true);

            context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));

            if (error.Severity == ValidationSeverity.Error)
                hasErrors = true;
        }

        if (hasErrors)
            return;

        // Build projection
        if (schema == null)
            return;

        var projection = ProjectionBuilder.Build(queryModel, schema);

        // JAUNTY008: Check for unresolved parameter types
        foreach (var param in queryModel.Parameters)
        {
            string inferredType = CodeEmitter.InferParameterType(param.Name, queryModel, projection, schema);
            if (inferredType == "object")
            {
                var descriptor = new DiagnosticDescriptor(
                    "JAUNTY008",
                    "JAUNTY008",
                    $"Parameter type could not be inferred for '@{param.Name}'",
                    "JauntyQ",
                    DiagnosticSeverity.Warning,
                    true);
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
            }
        }

        // Emit code
        var source = CodeEmitter.Emit(queryModel, projection, sqlText!, schema);

        context.AddSource($"{fileName}.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor SchemaLoadFailed = new(
            "JAUNTY006",
            "Schema Load Failed",
            "Failed to load schema for query '{0}'",
            "JauntyQ",
            DiagnosticSeverity.Error,
            true);
    }
}
