using Microsoft.CodeAnalysis;

namespace JauntyQ.Generator;

/// <summary>
/// Central registry of all JauntyQ diagnostic codes.
///
/// Format: JNTxxxx where the first digit defines the category:
///   1xxx — SQL parsing errors
///   2xxx — Schema validation errors
///   3xxx — Query shape / projection errors
///   4xxx — Parameter binding errors
///   5xxx — Value safety (literal/constant vs column constraints)
///   6xxx — Configuration errors
///   7xxx — Dialect errors
///   8xxx — Performance warnings
///   9xxx — Migrations
/// </summary>
public static class JauntyDiagnostics
{
    // ── 1xxx: SQL Parsing ─────────────────────────────────

    public static readonly DiagnosticDescriptor JNT1001 = new(
        "JNT1001",
        "Unsupported SQL Construct",
        "Unsupported SQL construct: {0}",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Warning,
        true);

    // ── 2xxx: Schema Validation ───────────────────────────

    public static readonly DiagnosticDescriptor JNT2001 = new(
        "JNT2001",
        "Table Not Found",
        "Table '{0}' does not exist in schema",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2002 = new(
        "JNT2002",
        "Column Not Found",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2003 = new(
        "JNT2003",
        "Ambiguous Column",
        "Ambiguous column reference '{0}' found in tables: {1}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // ── 3xxx: Query Shape / Projection ────────────────────

    public static readonly DiagnosticDescriptor JNT3001 = new(
        "JNT3001",
        "Empty Query",
        "SQL file is empty or contains no SELECT columns",
        "JauntyQ.Projection",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT3002 = new(
        "JNT3002",
        "SELECT * Forbidden",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    // ── 4xxx: Parameter Binding ───────────────────────────

    public static readonly DiagnosticDescriptor JNT4003 = new(
        "JNT4003",
        "Parameter Type Unresolved",
        "Parameter type could not be inferred for '@{0}'. Declare it explicitly with: -- @params {0}:<type>.",
        "JauntyQ.Parameters",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT4004 = new(
        "JNT4004",
        "Duplicate Parameter",
        "Duplicate parameter name '@{0}'",
        "JauntyQ.Parameters",
        DiagnosticSeverity.Error,
        true);

    // ── 5xxx: Value safety ────────────────────────────────

    public static readonly DiagnosticDescriptor JNT5001 = new(
        "JNT5001",
        "String Literal Exceeds Column Length",
        "{0}",
        "JauntyQ.ValueSafety",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT5002 = new(
        "JNT5002",
        "Numeric Literal Out Of Range",
        "{0}",
        "JauntyQ.ValueSafety",
        DiagnosticSeverity.Error,
        true);

    // ── 6xxx: Configuration ───────────────────────────────

    public static readonly DiagnosticDescriptor JNT6001 = new(
        "JNT6001",
        "Schema Missing",
        "Schema snapshot not found; run 'jaunty schema pull'",
        "JauntyQ.Configuration",
        DiagnosticSeverity.Error,
        true);

    // ── 7xxx: Dialect ─────────────────────────────────────

    public static readonly DiagnosticDescriptor JNT7001 = new(
        "JNT7001",
        "Identity Return Unavailable",
        "{0}",
        "JauntyQ.Dialect",
        DiagnosticSeverity.Error,
        true);

    // ── 9xxx: Migrations ──────────────────────────────────

    public static readonly DiagnosticDescriptor JNT9001 = new(
        "JNT9001",
        "Migration Statement Not Simulated",
        "{0}",
        "JauntyQ.Migrations",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT9002 = new(
        "JNT9002",
        "Migration Invalid Against Schema",
        "{0}",
        "JauntyQ.Migrations",
        DiagnosticSeverity.Error,
        true);
}
