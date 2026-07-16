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

    public static readonly DiagnosticDescriptor JNT1002 = new(
        "JNT1002",
        "Unterminated Token",
        "{0}",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT1003 = new(
        "JNT1003",
        "Input Too Large",
        "{0}",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT1004 = new(
        "JNT1004",
        "Unsupported Character",
        "{0}",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT1005 = new(
        "JNT1005",
        "Nesting Too Deep",
        "{0}",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// UNION gets its own Error-severity diagnostic rather than sharing
    /// JNT1001's Warning severity: the parser only ever models the first
    /// branch's projection (SqlParser.Part6.cs records the UNION construct
    /// but never parses past it), so a second branch with different
    /// nullability (e.g. a literal NULL in a column the first branch has
    /// NOT NULL) silently generates a projection that throws at read time --
    /// this isn't "an unusual construct we noticed", it's "we know the
    /// generated code is wrong here."
    /// </summary>
    public static readonly DiagnosticDescriptor JNT1006 = new(
        "JNT1006",
        "Unsupported UNION",
        "UNION/UNION ALL is not supported: the generator only models the first branch's " +
        "column shape, so a second branch with different nullability would silently generate " +
        "code that reads NULL as non-nullable and throws at runtime. Split into separate " +
        "queries, or model the combined result as an application-level merge.",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SUBQUERY gets its own Error-severity diagnostic for the same reason as
    /// JNT1006/UNION rather than sharing JNT1001's Warning severity: a nested
    /// SELECT (a scalar subquery in the projection list, or a derived table in
    /// FROM) is not lifted and parsed as its own scope the way a WHERE-clause
    /// [NOT] IN/EXISTS predicate subquery is — its tokens fall straight
    /// through the enclosing statement's ordinary FROM/SELECT/JOIN parsing,
    /// which was never written to expect a nested SELECT there. This isn't
    /// "an unusual construct we noticed", it's "the outer query's shape may
    /// already be misparsed."
    /// </summary>
    public static readonly DiagnosticDescriptor JNT1007 = new(
        "JNT1007",
        "Unsupported Subquery",
        "This subquery form is not supported: only WHERE-clause '[NOT] IN (SELECT ...)' and " +
        "'[NOT] EXISTS (SELECT ...)' predicates are modeled as their own scope. A scalar " +
        "subquery in the projection list or a derived table in FROM falls through to the " +
        "enclosing statement's ordinary parsing, which can misread the outer query's shape. " +
        "Rewrite using a JOIN, a WHERE-clause IN/EXISTS predicate, or a CTE.",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
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

    public static readonly DiagnosticDescriptor JNT2004 = new(
        "JNT2004",
        "Illegal Identifier",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2005 = new(
        "JNT2005",
        "Stored Procedure Not Found",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2006 = new(
        "JNT2006",
        "Entity/Row POCO Name Collision",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2007 = new(
        "JNT2007",
        "Unmapped Column Type",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT2008 = new(
        "JNT2008",
        "Duplicate Generated File",
        "{0}",
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

    public static readonly DiagnosticDescriptor JNT3003 = new(
        "JNT3003",
        "Invalid Directive Combination",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT3004 = new(
        "JNT3004",
        "Expression Projection Missing Alias",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT3005 = new(
        "JNT3005",
        "Expression Type Unresolved",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT3006 = new(
        "JNT3006",
        "Unknown Type Directive Alias",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT3007 = new(
        "JNT3007",
        "IN-Subquery Column Count",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT3008 = new(
        "JNT3008",
        "Unrecognized Directive",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT3009 = new(
        "JNT3009",
        "Duplicate Result Column",
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

    public static readonly DiagnosticDescriptor JNT6002 = new(
        "JNT6002",
        "Multiple Schema Snapshots",
        "{0}",
        "JauntyQ.Configuration",
        DiagnosticSeverity.Warning,
        true);

    // ── 7xxx: Dialect ─────────────────────────────────────

    public static readonly DiagnosticDescriptor JNT7001 = new(
        "JNT7001",
        "Identity Return Unavailable",
        "{0}",
        "JauntyQ.Dialect",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT7002 = new(
        "JNT7002",
        "Construct Unavailable On Dialect",
        "{0}",
        "JauntyQ.Dialect",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT7003 = new(
        "JNT7003",
        "Unknown Dialect",
        "{0}",
        "JauntyQ.Dialect",
        DiagnosticSeverity.Error,
        true);

    // ── 8xxx: Performance ─────────────────────────────────

    public static readonly DiagnosticDescriptor JNT8001 = new(
        "JNT8001",
        "Possible Cartesian Product",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8002 = new(
        "JNT8002",
        "Non-Sargable Predicate",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8003 = new(
        "JNT8003",
        "Leading-Wildcard LIKE",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8004 = new(
        "JNT8004",
        "Unindexed Filter Column",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8005 = new(
        "JNT8005",
        "Duplicate Query",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8006 = new(
        "JNT8006",
        "Join Not A Foreign Key",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8007 = new(
        "JNT8007",
        "Unindexed Order By",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8008 = new(
        "JNT8008",
        "Possible N+1 Access Pattern",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
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

    public static readonly DiagnosticDescriptor JNT9003 = new(
        "JNT9003",
        "Missing or Unknown Dialect for DDL Schema Source",
        "{0}",
        "JauntyQ.Migrations",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT9004 = new(
        "JNT9004",
        "Migration Risky Impact",
        "{0}",
        "JauntyQ.Migrations",
        DiagnosticSeverity.Warning,
        true);
}
