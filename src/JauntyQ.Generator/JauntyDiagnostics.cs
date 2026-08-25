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

    /// <summary>
    /// A second top-level statement gets its own Error-severity diagnostic for
    /// the same reason as JNT1006/UNION and JNT1007/SUBQUERY: the generator
    /// knows the emitted code is wrong, not merely unusual. SqlParser's main
    /// loop dispatches on every SELECT/FROM/JOIN keyword in the token list, so
    /// two SELECT statements in one file merge into a single QueryModel
    /// carrying both projections and both table sets — the mapper is then
    /// generated against ordinals no single result set has. An
    /// INSERT/UPDATE/DELETE first statement fails the other way: the parser
    /// returns as soon as it has parsed that statement, so the second is
    /// dropped and never emitted at all. One file, one statement is the
    /// contract; batching several statements into one method is a feature that
    /// does not exist yet, not something to be arrived at by accident.
    /// </summary>
    public static readonly DiagnosticDescriptor JNT1008 = new(
        "JNT1008",
        "Multiple Statements In One File",
        "This file contains more than one SQL statement. JauntyQ generates one method per file " +
        "from one statement: a second statement's tables and columns are merged into the first " +
        "statement's model (SELECT) or dropped entirely (INSERT/UPDATE/DELETE), so the generated " +
        "code would not match either statement. Split each statement into its own .sql file.",
        "JauntyQ.Parsing",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// Spec 015: syntax the parser does not implement, reported as such.
    ///
    /// Split out of JNT2001 ("Table Not Found"), which was doing two unrelated
    /// jobs. A consumer writing LEFT JOIN LATERAL was told their table did not
    /// exist in their schema -- true of a relation the parser had invented from
    /// the keyword, and useless as advice, because nothing about their schema
    /// was wrong. "Your schema is missing something" and "my grammar is missing
    /// something" call for opposite responses from the person reading it, so
    /// they cannot share a code.
    ///
    /// JNT2001 keeps its meaning and its cases: a relation NAMED in the query
    /// and absent from the schema.
    /// </summary>
    public static readonly DiagnosticDescriptor JNT1009 = new(
        "JNT1009",
        "Unsupported Syntax",
        "{0}",
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

    public static readonly DiagnosticDescriptor JNT2009 = new(
        "JNT2009",
        "Duplicate Sequence Accessor Name",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2010 = new(
        "JNT2010",
        "Duplicate Entity Accessor Name",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor JNT2011 = new(
        "JNT2011",
        "Duplicate Column Property Name",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // AUD-R75-02: the "__" prefix is reserved for the generator's own
    // bookkeeping identifiers (__conn, __cmd, __weOpened, __reader, __Map,
    // __each_, __i_ and 16 more). Rounds 69 and 70 each closed one collision
    // by renaming a single bookkeeping local; that never converged, because a
    // rename only covers the one name it touches. Reserving the whole
    // namespace is what makes the fix hold when a 24th bookkeeping identifier
    // is added later.
    public static readonly DiagnosticDescriptor JNT2012 = new(
        "JNT2012",
        "Reserved Parameter Name Prefix",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // AUD-R75-01: sibling of JNT2011 for a parameter list rather than a
    // result set. Two individually-legal parameter names can fold to one C#
    // identifier (DialectMapper.ToPascalCase strips separators), emitting a
    // duplicate formal parameter.
    public static readonly DiagnosticDescriptor JNT2013 = new(
        "JNT2013",
        "Duplicate Parameter Name",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // AUD-R75-03: a table whose columns fold to one C# property name is
    // skipped by three separate sites and used to vanish from the generated
    // API with no diagnostic at all. Warning rather than Error: round 64 made
    // that skip deliberately non-breaking, and an Error would fail the build
    // of any consumer whose schema holds such a pair even in an unused table.
    public static readonly DiagnosticDescriptor JNT2014 = new(
        "JNT2014",
        "Table Skipped For Colliding Column Names",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    // AUD-R64-01 (T8 residual): the same silent-skip shape as JNT2014, for the
    // other reason AutoCrud refuses a table — a name it cannot emit unquoted.
    // Round 64 chose that skip to keep the generator from emitting SQL the
    // engine would reject, which was right, but the consequence (no entity, no
    // CRUD, no explanation) was invisible. It matters more now that the T8
    // probe corrected the lists in both directions: a reserved-word entry is no
    // longer a free safety margin, it is a table removed from the API, and that
    // has to be visible to be arguable. Warning, matching JNT2014 and for the
    // same reason.
    public static readonly DiagnosticDescriptor JNT2015 = new(
        "JNT2015",
        "Table Skipped For Unquotable Identifier",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    // Spec 013: sibling of JNT2011/JNT2013 for a captured database enum. Two
    // individually-legal member values can fold to one C# identifier
    // ("in progress" and "in-progress" both -> InProgress; "" and "-" both ->
    // "_"), which would emit a duplicate enum member (CS0102) and a duplicate
    // switch case in the generated Parse.
    public static readonly DiagnosticDescriptor JNT2016 = new(
        "JNT2016",
        "Duplicate Enum Member Name",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // Spec 013: an enum contributes three names to the generated namespace --
    // the enum itself, its {EnumName}Values companion, and (once, for the
    // assembly) JauntyQEnumValueException. Any of them can collide with a row
    // type, an entity accessor or another enum. Silently renaming is the
    // anti-pattern JNT2015 was created to end, so report instead.
    public static readonly DiagnosticDescriptor JNT2017 = new(
        "JNT2017",
        "Enum Type Name Collision",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // AUD-R4-16: a MySQL upsert on a table with a competing UNIQUE constraint
    // cannot use ON DUPLICATE KEY UPDATE, which names no conflict target and
    // would let the engine match a key the caller never asked about. The
    // key-targeted replacement is two statements, and two statements are not
    // atomic the way one is: concurrent upserts of the same new key can both
    // pass the NOT EXISTS guard and one takes a duplicate-key error. Warning,
    // matching JNT2014/JNT2015 and for the same reason -- a decision the
    // consumer should be able to argue with (by wrapping the call in the
    // transaction the generated method already honours, or by dropping the
    // competing constraint) has to be visible to be arguable.
    public static readonly DiagnosticDescriptor JNT2018 = new(
        "JNT2018",
        "Non-Atomic Key-Targeted MySQL Upsert",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    // A prefix UNIQUE (MySQL UNIQUE (email(5)), captured as
    // IndexSchema.HasPrefixKeyPart) enforces uniqueness over a truncated
    // prefix, not the full value. When it is the ONLY constraint that could
    // serve as a table's upsert key, no emitted form -- atomic or
    // two-statement -- can honour the full-value match the method's signature
    // implies: the engine matches rows sharing only the prefix. Decided
    // 2026-07-30: refuse Upsert synthesis outright rather than emit a method
    // whose contract the index cannot enforce. Warning, matching
    // JNT2014/JNT2015/JNT2018 and for the same reason -- a decision the
    // consumer can argue with (add a full-column UNIQUE, or hand-write the
    // upsert) has to be visible to be arguable.
    public static readonly DiagnosticDescriptor JNT2019 = new(
        "JNT2019",
        "Upsert Skipped For Prefix-Only Unique Key",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    // An expression UNIQUE (UNIQUE (lower(email)), captured as
    // IndexSchema.HasExpressionKeyPart with Columns holding only the
    // real-column subset of its key) can never serve as a table's upsert
    // key: the engine matches on an expression's value, which no emitted
    // method can bind a parameter to. When it is the ONLY constraint that
    // could have served, the skip used to be silent -- exactly the reading
    // such tables had before 2026-07-30, when the index was excluded at
    // capture entirely. Made visible 2026-08-01 for JNT2019's own reason:
    // a decision the consumer can argue with (add a full-column UNIQUE, or
    // hand-write the upsert) has to be visible to be arguable.
    public static readonly DiagnosticDescriptor JNT2020 = new(
        "JNT2020",
        "Upsert Skipped For Expression-Only Unique Key",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
        true);

    // Spec 015: views entered the snapshot so they could be READ. A write
    // against one is refused rather than skipped, and it is an Error rather
    // than a Warning, because the two silent-skip diagnostics above (JNT2014,
    // JNT2015) cover a table the GENERATOR declined to synthesize for -- the
    // consumer never asked for those methods. This is the opposite case: the
    // consumer wrote an INSERT/UPDATE/DELETE by hand and means it. Dropping it
    // silently would generate a method whose SQL the engine rejects at runtime,
    // and warning about it would let that method be called.
    public static readonly DiagnosticDescriptor JNT2021 = new(
        "JNT2021",
        "Write To View",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Error,
        true);

    // The last silent lossy rename, and the quietest of them:
    // DialectMapper.ToPascalCase keeps ASCII letters and digits only, so every
    // other Unicode letter is treated as a word separator and vanishes.
    // "groesse" spelled "größe" generates the property GrE; "café" generates
    // Caf; "日本語" generates "_". None of that collides, none of it fails to
    // compile, and so JNT2011/JNT2014 -- which only ever see two names folding
    // to ONE name -- cannot report any of it. The consumer gets a property
    // whose name is a different word from their column and no indication why.
    //
    // Warning, matching JNT2014/JNT2015 and for their reason: the rename is
    // long-standing behaviour and an Error would fail the build of any existing
    // consumer whose schema holds such a column, including in a table they
    // never query. Reporting it does not change a single generated name.
    //
    // NOT a fix. Whether ToPascalCase should keep Unicode letters outright --
    // C# identifiers allow them, so "größe" could generate Größe -- is a
    // separate decision, because it would change emitted property names for
    // exactly the consumers this warns, and it moves the injection trust
    // boundary documented on ToPascalCase. Recorded in the todo list.
    public static readonly DiagnosticDescriptor JNT2022 = new(
        "JNT2022",
        "Lossy Identifier Rename",
        "{0}",
        "JauntyQ.Schema",
        DiagnosticSeverity.Warning,
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

    public static readonly DiagnosticDescriptor JNT3010 = new(
        "JNT3010",
        "Mirrored Query Not Comparable",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Warning,
        true);

    // Warning, not Error, and deliberately so: the file still generates, and
    // the last line still wins as it always did. What changes is that the
    // overwrite is now stated. Making it an Error would break builds that are
    // presently generating exactly the code their authors expect.
    public static readonly DiagnosticDescriptor JNT3011 = new(
        "JNT3011",
        "Duplicate Directive",
        "{0}",
        "JauntyQ.Projection",
        DiagnosticSeverity.Warning,
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

    // Sibling of JNT5001/JNT5002 for the third way a literal can fail its
    // column: not too long, not out of range, but the wrong KIND of value
    // altogether -- one the engine rejects rather than truncates or
    // overflows. Created 2026-08-17 for Postgres's bit(n), which is a bit
    // string and refuses a bare number ("column is of type bit but
    // expression is of type integer"). Error, because unlike JNT5001/JNT5002
    // -- which report a statement that RUNS and quietly loses something --
    // this one reports a statement the server will not execute at all, so
    // there is no reading under which the build should pass.
    public static readonly DiagnosticDescriptor JNT5003 = new(
        "JNT5003",
        "Literal Type Mismatch",
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

    // Warning, not Error: a wrong acceptance file must not stop a build that
    // would otherwise succeed. The failure this guards against is SILENCE -- an
    // entry that suppresses nothing, or suppresses the wrong thing, while its
    // author believes otherwise -- and a warning ends the silence. An Error
    // would also mean a typo in a file whose whole purpose is to quieten the
    // build is louder than the warnings it was written to quieten.
    public static readonly DiagnosticDescriptor JNT6003 = new(
        "JNT6003",
        "Invalid Acceptance File",
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

    public static readonly DiagnosticDescriptor JNT8009 = new(
        "JNT8009",
        "Unstable Pagination",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8010 = new(
        "JNT8010",
        "Unordered Pagination",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor JNT8011 = new(
        "JNT8011",
        "Predicate Drift",
        "{0}",
        "JauntyQ.Performance",
        DiagnosticSeverity.Warning,
        true);

    // Spec 015: the companion to -- @allow-unindexed. The directive is present
    // but JNT8004 never fired, so it silences nothing. Reported because an
    // exemption outliving the condition that justified it is exactly how an
    // escape hatch becomes the default -- the migration lands, the index
    // exists, and the suppression stays in the file forever, now hiding a
    // future regression instead of an accepted one. Warning, not Error: a stale
    // suppression is untidy, not wrong.
    public static readonly DiagnosticDescriptor JNT8012 = new(
        "JNT8012",
        "Unnecessary Unindexed Acceptance",
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
