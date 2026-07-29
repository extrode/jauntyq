using System;
using System.Collections.Generic;

namespace JauntyQ.Analysis;

/// <summary>
/// Which syntactic position a bare identifier is about to be emitted into.
/// The distinction is small but not empty: across 425 candidate words probed
/// live against four engines, six diverge between the two positions, and
/// collapsing them costs real schemas either way (see
/// <see cref="DialectReservedWords"/>).
/// </summary>
public enum SqlIdentifierPosition
{
    /// <summary>A column reference: SELECT list, INSERT column list, UPDATE SET, WHERE, ORDER BY.</summary>
    Column,

    /// <summary>An object name: the table in FROM/INSERT INTO/UPDATE/DELETE, or a sequence.</summary>
    Object,
}

/// <summary>
/// AUD-R64-01: <see cref="AutoCrud"/>'s "can this name be emitted bare/unquoted"
/// gate previously validated only against <c>JauntyQ.SqlParser.SqlTokenizer</c>'s
/// own ~55-word internal keyword list (the subset of SQL this project's own
/// tokenizer models) — not against the TARGET ENGINE's actual reserved-word
/// grammar. A column literally named <c>select</c> or <c>index</c> is not a
/// JauntyQ keyword, so the old gate passed it through, but the engine refuses
/// the statement (or, worse, silently parses the name as something else). This
/// is a second, independent check from
/// <see cref="JauntyQ.SqlParser.SqlTokenizer.IsReservedKeyword"/> (which still
/// matters for JauntyQ's own tokenizer/parser correctness) — a name must clear
/// BOTH to be safely emitted bare.
///
/// AUD-R64-01 residual (T8, 2026-07-29): round 64 curated these lists from each
/// engine's published grammar and shipped NO list for SQLite at all. Both
/// halves of that were wrong in ways only an engine can settle, so the lists
/// below are now the verdict of an exhaustive live probe rather than a reading
/// of the documentation. 425 candidate words — the union of all four shipped
/// lists, SQLite's keyword appendix, and a control group of ordinary column
/// nouns — were created quoted and then referenced BARE in every position
/// AutoCrud emits (SELECT list, INSERT column list, UPDATE SET, WHERE, ORDER
/// BY, DELETE, the dialect-native upsert, and the table-name position) against
/// postgres:16, mysql:8.0.46, MariaDB 11.8.8, SQL Server 2022 RTM-CU25 and
/// SQLite 3.45.3. A word is listed if the engine rejected the bare reference,
/// or accepted it and returned something other than the stored sentinel.
///
/// What that changed: SQLite gained a list where it had none (63 words);
/// PostgreSQL gained 22, most importantly the join keywords — a column called
/// <c>left</c> or <c>right</c> is entirely ordinary and aborted every generated
/// statement; MySQL gained 8, including MariaDB's <c>returning</c>/<c>offset</c>
/// (MariaDB rides the "mysql" dialect string, so this list is the union of both
/// engines); SQL Server needed none. Six words were REMOVED as
/// over-inclusions — <c>groups over row within</c> on PostgreSQL and
/// <c>second user</c> on MySQL all parse fine bare, and reserving them silently
/// removed the whole table from the generated API for nothing.
///
/// Over-inclusion is not a free safety margin: the gate's failure mode is to
/// skip, and a skipped table is absent from the generated API. That is why the
/// five SQL Server words Microsoft documents as reserved but its parser accepts
/// (<c>disk dump load precision securityaudit</c>) are kept — documented-reserved
/// is a promise about future versions — while words no vendor documents as
/// reserved are not.
/// </summary>
public static class DialectReservedWords
{

    /// <summary>PostgreSQL 16. Column and object positions agree exactly. Reserved in both positions (99 words).</summary>
    private static readonly HashSet<string> PostgresBoth = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc",
        "asymmetric", "authorization", "binary", "both", "case", "cast",
        "check", "collate", "column", "concurrently", "constraint", "create",
        "cross", "current_catalog", "current_date", "current_role",
        "current_time", "current_timestamp", "current_user", "default",
        "deferrable", "desc", "distinct", "do", "else", "end", "except",
        "false", "fetch", "for", "foreign", "freeze", "from", "full", "grant",
        "group", "having", "ilike", "in", "initially", "inner", "intersect",
        "into", "is", "isnull", "join", "lateral", "leading", "left", "like",
        "limit", "localtime", "localtimestamp", "natural", "not", "notnull",
        "null", "offset", "on", "only", "or", "order", "outer", "overlaps",
        "placing", "primary", "references", "returning", "right", "select",
        "session_user", "similar", "some", "symmetric", "system_user", "table",
        "tablesample", "then", "to", "trailing", "true", "union", "unique",
        "user", "using", "variadic", "verbose", "when", "where", "window",
        "with",
    };

    /// <summary>MySQL 8.0.46 union MariaDB 11.8.8 — one dialect string serves both. Reserved in both positions (253 words).</summary>
    private static readonly HashSet<string> MySqlBoth = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessible", "add", "all", "alter", "analyze", "and", "as", "asc",
        "asensitive", "before", "between", "bigint", "binary", "blob", "both",
        "by", "call", "cascade", "case", "change", "char", "character", "check",
        "collate", "column", "condition", "constraint", "continue", "convert",
        "create", "cross", "cube", "current_date", "current_role",
        "current_time", "current_timestamp", "current_user", "cursor",
        "database", "databases", "day_hour", "day_microsecond", "day_minute",
        "day_second", "dec", "decimal", "declare", "default", "delayed",
        "delete", "dense_rank", "desc", "describe", "deterministic", "distinct",
        "distinctrow", "div", "double", "drop", "dual", "each", "else",
        "elseif", "empty", "enclosed", "escaped", "except", "exists", "exit",
        "explain", "false", "fetch", "float", "float4", "float8", "for",
        "force", "foreign", "from", "fulltext", "function", "generated", "get",
        "grant", "group", "grouping", "groups", "having", "high_priority",
        "hour_microsecond", "hour_minute", "hour_second", "if", "ignore", "in",
        "index", "infile", "inner", "inout", "insensitive", "insert", "int",
        "int1", "int2", "int3", "int4", "int8", "integer", "intersect",
        "interval", "into", "is", "iterate", "join", "key", "keys", "kill",
        "lag", "lateral", "lead", "leading", "leave", "left", "like", "limit",
        "linear", "lines", "load", "localtime", "localtimestamp", "lock",
        "long", "longblob", "longtext", "loop", "low_priority", "match",
        "maxvalue", "mediumblob", "mediumint", "mediumtext", "middleint",
        "minute_microsecond", "minute_second", "mod", "modifies", "natural",
        "no_write_to_binlog", "not", "null", "numeric", "of", "offset", "on",
        "optimize", "optimizer_costs", "option", "optionally", "or", "order",
        "out", "outer", "outfile", "over", "partition", "precision", "primary",
        "procedure", "purge", "range", "rank", "read", "read_write", "reads",
        "real", "recursive", "references", "regexp", "release", "rename",
        "repeat", "replace", "require", "resignal", "restrict", "return",
        "returning", "revoke", "right", "rlike", "row", "row_number", "rows",
        "schema", "schemas", "second_microsecond", "select", "sensitive",
        "separator", "set", "show", "signal", "smallint", "spatial", "specific",
        "sql", "sql_big_result", "sql_calc_found_rows", "sql_small_result",
        "sqlexception", "sqlstate", "sqlwarning", "ssl", "starting", "stored",
        "straight_join", "table", "terminated", "then", "tinyblob", "tinyint",
        "tinytext", "to", "trailing", "trigger", "true", "undo", "union",
        "unique", "unlock", "unsigned", "update", "usage", "use", "using",
        "utc_date", "utc_time", "utc_timestamp", "values", "varbinary",
        "varchar", "varcharacter", "varying", "virtual", "when", "where",
        "while", "window", "with", "write", "xor", "year_month", "zerofill",
    };

    /// <summary>
    /// Reserved as an OBJECT name only. MariaDB refuses
    /// <c>INSERT INTO value (...)</c> but accepts <c>value</c> as a column
    /// everywhere; since "value" is a very common column name, reserving it
    /// wholesale would silently drop those tables.
    /// </summary>
    private static readonly HashSet<string> MySqlObjectOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "value",
    };

    /// <summary>SQL Server 2022 (RTM-CU25). Column and object positions agree exactly. Reserved in both positions (180 words).</summary>
    private static readonly HashSet<string> SqlServerBoth = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "any", "as", "asc", "authorization",
        "backup", "begin", "between", "break", "browse", "bulk", "by",
        "cascade", "case", "check", "checkpoint", "close", "clustered",
        "coalesce", "collate", "column", "commit", "compute", "constraint",
        "contains", "containstable", "continue", "convert", "create", "cross",
        "current", "current_date", "current_time", "current_timestamp",
        "current_user", "cursor", "database", "dbcc", "deallocate", "declare",
        "default", "delete", "deny", "desc", "disk", "distinct", "distributed",
        "double", "drop", "dump", "else", "end", "errlvl", "escape", "except",
        "exec", "execute", "exists", "exit", "external", "fetch", "file",
        "fillfactor", "for", "foreign", "freetext", "freetexttable", "from",
        "full", "function", "goto", "grant", "group", "having", "holdlock",
        "identity", "identity_insert", "identitycol", "if", "in", "index",
        "inner", "insert", "intersect", "into", "is", "join", "key", "kill",
        "left", "like", "lineno", "load", "merge", "national", "nocheck",
        "nonclustered", "not", "null", "nullif", "of", "off", "offsets", "on",
        "open", "opendatasource", "openquery", "openrowset", "openxml",
        "option", "or", "order", "outer", "over", "percent", "pivot", "plan",
        "precision", "primary", "print", "proc", "procedure", "public",
        "raiserror", "read", "readtext", "reconfigure", "references",
        "replication", "restore", "restrict", "return", "revert", "revoke",
        "right", "rollback", "rowcount", "rowguidcol", "rule", "save", "schema",
        "securityaudit", "select", "session_user", "set", "setuser", "shutdown",
        "some", "statistics", "system_user", "table", "tablesample", "textsize",
        "then", "to", "top", "tran", "transaction", "trigger", "truncate",
        "try_convert", "tsequal", "union", "unique", "unpivot", "update", "use",
        "user", "values", "varying", "view", "waitfor", "when", "where",
        "while", "with", "writetext",
    };

    /// <summary>SQLite 3.45.3. Round 64 shipped no list for this dialect at all. Reserved in both positions (58 words).</summary>
    private static readonly HashSet<string> SqliteBoth = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "as", "autoincrement", "between", "case",
        "check", "collate", "commit", "constraint", "create", "default",
        "deferrable", "delete", "distinct", "drop", "else", "escape", "except",
        "exists", "foreign", "from", "group", "having", "in", "index", "insert",
        "intersect", "into", "is", "isnull", "join", "limit", "not", "nothing",
        "notnull", "null", "on", "or", "order", "primary", "references",
        "returning", "select", "set", "table", "then", "to", "transaction",
        "union", "unique", "update", "using", "values", "when", "where",
    };

    /// <summary>
    /// Reserved as a COLUMN name only. These parse as function calls, so the
    /// bare reference in a SELECT list is a syntax error while
    /// <c>FROM cast</c> is perfectly legal.
    /// </summary>
    private static readonly HashSet<string> SqliteColumnOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "cast", "current_date", "current_time", "current_timestamp", "raise",
    };

    /// <summary>
    /// True when <paramref name="name"/> is reserved in the target engine's own
    /// grammar (case-insensitive) for the given syntactic position, independent
    /// of whether JauntyQ's own tokenizer models it as a keyword. MariaDB rides
    /// the "mysql" dialect string, matching the rest of the codebase's
    /// convention. Unknown/unset dialects return false (no engine-specific info
    /// to check; the JauntyQ-keyword check the caller also runs is the only gate
    /// that applies).
    /// </summary>
    public static bool IsReservedInDialect(string name, string? dialect, SqlIdentifierPosition position)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dialect))
            return false;

        bool column = position == SqlIdentifierPosition.Column;

        if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            return PostgresBoth.Contains(name);
        if (string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase))
            return MySqlBoth.Contains(name) || (!column && MySqlObjectOnly.Contains(name));
        if (string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase))
            return SqlServerBoth.Contains(name);
        if (string.Equals(dialect, "sqlite", StringComparison.OrdinalIgnoreCase))
            return SqliteBoth.Contains(name) || (column && SqliteColumnOnly.Contains(name));

        return false;
    }

    /// <summary>
    /// Position-less overload: the union of both positions, i.e. the stricter
    /// answer. A caller that does not know where the name lands must get the
    /// conservative verdict, never the lax one.
    /// </summary>
    public static bool IsReservedInDialect(string name, string? dialect)
        => IsReservedInDialect(name, dialect, SqlIdentifierPosition.Column)
        || IsReservedInDialect(name, dialect, SqlIdentifierPosition.Object);

    /// <summary>
    /// True when <paramref name="name"/> cannot be emitted bare/unquoted for
    /// PostgreSQL specifically, because it would silently be case-folded to a
    /// different stored name. PostgreSQL lower-cases every unquoted identifier
    /// it parses; a name that was created quoted and contains any uppercase
    /// letter can therefore never be re-referenced correctly unquoted — this is
    /// not a probabilistic collation concern, it is a deterministic fold
    /// (verified live against postgres:16: an unquoted reference to a
    /// quoted-created "OrderNumber" sequence/table resolves to "ordernumber"
    /// instead, either erroring with "does not exist" or, worse, silently
    /// matching an unrelated same-named-but-lowercase object). Other dialects'
    /// unquoted-identifier folding (SQL Server/SQLite: case-insensitive match,
    /// no rename; MySQL: table-name case-sensitivity is OS/config-dependent but
    /// never silently renames the identifier itself) do not have this failure
    /// mode, so this check is PostgreSQL-only by design, not a general
    /// case-sensitivity policy.
    /// </summary>
    public static bool RequiresQuotingForCase(string name, string? dialect)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dialect))
            return false;
        if (!string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (char c in name)
        {
            if (char.IsUpper(c))
                return true;
        }
        return false;
    }
}
