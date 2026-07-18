using System;
using System.Collections.Generic;

namespace JauntyQ.Analysis;

/// <summary>
/// AUD-R64-01: <see cref="AutoCrud"/>'s "can this name be emitted bare/unquoted"
/// gate previously validated only against <c>JauntyQ.SqlParser.SqlTokenizer</c>'s
/// own ~55-word internal keyword list (the subset of SQL this project's own
/// tokenizer models) — not against the TARGET ENGINE's actual reserved-word
/// grammar. A column literally named <c>user</c> or <c>key</c> is not a JauntyQ
/// keyword, so the old gate passed it through, but PostgreSQL/MySQL/SQL Server
/// all reserve those words: emitted bare, PostgreSQL silently parses <c>user</c>
/// as the niladic <c>current_user</c> function (wrong data on every row, zero
/// diagnostic anywhere — verified live against postgres:16), and MySQL raises a
/// runtime syntax error (verified live against mysql:8.0). This is a second,
/// independent check from <see cref="JauntyQ.SqlParser.SqlTokenizer.IsReservedKeyword"/>
/// (which still matters for JauntyQ's own tokenizer/parser correctness) — a name
/// must clear BOTH to be safely emitted bare.
///
/// These lists are curated from each engine's published reserved-word grammar,
/// not exhaustively generated from the engine itself; the goal is to catch the
/// realistic collision surface (ordinary short nouns a schema author would
/// plausibly choose), not to be a byte-for-byte copy of each product's full
/// SQL:2016 reserved-words appendix.
/// </summary>
public static class DialectReservedWords
{
    private static readonly HashSet<string> Postgres = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc",
        "asymmetric", "both", "case", "cast", "check", "collate", "column",
        "constraint", "create", "current_catalog", "current_date",
        "current_role", "current_time", "current_timestamp", "current_user",
        "default", "deferrable", "desc", "distinct", "do", "else", "end",
        "except", "false", "fetch", "for", "foreign", "from", "grant",
        "group", "groups", "having", "in", "initially", "intersect", "into",
        "lateral", "leading", "limit", "localtime", "localtimestamp", "not",
        "null", "offset", "on", "only", "or", "order", "over", "placing",
        "primary", "references", "returning", "row", "select",
        "session_user", "some", "symmetric", "table", "then", "to",
        "trailing", "true", "union", "unique", "user", "using", "variadic",
        "when", "where", "window", "with", "within",
    };

    private static readonly HashSet<string> MySql = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessible", "add", "all", "alter", "analyze", "and", "as", "asc",
        "asensitive", "before", "between", "bigint", "binary", "blob",
        "both", "by", "call", "cascade", "case", "change", "char",
        "character", "check", "collate", "column", "condition",
        "constraint", "continue", "convert", "create", "cross",
        "current_date", "current_time", "current_timestamp",
        "current_user", "cursor", "database", "databases", "day_hour",
        "day_microsecond", "day_minute", "day_second", "dec", "decimal",
        "declare", "default", "delayed", "delete", "dense_rank", "desc",
        "describe", "deterministic", "distinct", "distinctrow", "div",
        "double", "drop", "dual", "each", "else", "elseif", "empty",
        "enclosed", "escaped", "exists", "exit", "explain", "false",
        "fetch", "float", "float4", "float8", "for", "force", "foreign",
        "from", "fulltext", "function", "generated", "get", "grant",
        "group", "groups", "having", "high_priority", "hour_microsecond",
        "hour_minute", "hour_second", "if", "ignore", "in", "index",
        "infile", "inner", "inout", "insensitive", "insert", "int", "int1",
        "int2", "int3", "int4", "int8", "integer", "interval", "into",
        "is", "iterate", "join", "key", "keys", "kill", "lag", "lateral",
        "lead", "leading", "leave", "left", "like", "limit", "linear",
        "lines", "load", "localtime", "localtimestamp", "lock", "long",
        "longblob", "longtext", "loop", "low_priority", "match",
        "maxvalue", "mediumblob", "mediumint", "mediumtext", "middleint",
        "minute_microsecond", "minute_second", "mod", "modifies",
        "natural", "not", "no_write_to_binlog", "null", "numeric", "of",
        "on", "optimize", "optimizer_costs", "option", "optionally", "or",
        "order", "out", "outer", "outfile", "over", "partition",
        "precision", "primary", "procedure", "purge", "rank", "range",
        "read", "reads", "read_write", "real", "recursive", "references",
        "regexp", "release", "rename", "repeat", "replace", "require",
        "resignal", "restrict", "return", "revoke", "right", "rlike",
        "row", "row_number", "rows", "schema", "schemas", "second",
        "second_microsecond", "select", "sensitive", "separator", "set",
        "show", "signal", "smallint", "spatial", "specific", "sql",
        "sqlexception", "sqlstate", "sqlwarning", "sql_big_result",
        "sql_calc_found_rows", "sql_small_result", "ssl", "starting",
        "stored", "straight_join", "table", "terminated", "then",
        "tinyblob", "tinyint", "tinytext", "to", "trailing", "trigger",
        "true", "undo", "union", "unique", "unlock", "unsigned", "update",
        "usage", "use", "user", "using", "utc_date", "utc_time",
        "utc_timestamp", "values", "varbinary", "varchar", "varcharacter",
        "varying", "virtual", "when", "where", "while", "window", "with",
        "write", "xor", "year_month", "zerofill",
    };

    /// <summary>
    /// Deliberately excludes "tran"/"transaction" despite both appearing on
    /// Microsoft's published T-SQL reserved-keyword list: this project's own
    /// existing, real-compile-verified regression test
    /// (<c>TransactionNameCollisionGuardTests.
    /// AutoCrud_TableWithColumnLiterallyNamedTransaction_
    /// PocoOverloadsCompileClean</c>) establishes that a column literally
    /// named <c>transaction</c> synthesizes and compiles cleanly today, and
    /// the published reserved-word list is known to include words "reserved
    /// for future use" that the engine does not currently enforce in every
    /// syntactic position (column-name position in particular is often more
    /// permissive than the list implies). Since a false-positive here (an
    /// actually-safe name skipped) is merely conservative while a
    /// false-negative is the exact defect class this file exists to close,
    /// words are excluded rather than included on uncertain footing.
    /// </summary>
    private static readonly HashSet<string> SqlServer = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "all", "alter", "and", "any", "as", "asc", "authorization",
        "backup", "begin", "between", "break", "browse", "bulk", "by",
        "cascade", "case", "check", "checkpoint", "close", "clustered",
        "coalesce", "collate", "column", "commit", "compute", "constraint",
        "contains", "containstable", "continue", "convert", "create",
        "cross", "current", "current_date", "current_time",
        "current_timestamp", "current_user", "cursor", "database", "dbcc",
        "deallocate", "declare", "default", "delete", "deny", "desc",
        "disk", "distinct", "distributed", "double", "drop", "dump",
        "else", "end", "errlvl", "escape", "except", "exec", "execute",
        "exists", "exit", "external", "fetch", "file", "fillfactor",
        "for", "foreign", "freetext", "freetexttable", "from", "full",
        "function", "goto", "grant", "group", "having", "holdlock",
        "identity", "identity_insert", "identitycol", "if", "in", "index",
        "inner", "insert", "intersect", "into", "is", "join", "key",
        "kill", "left", "like", "lineno", "load", "merge", "national",
        "nocheck", "nonclustered", "not", "null", "nullif", "of", "off",
        "offsets", "on", "open", "opendatasource", "openquery",
        "openrowset", "openxml", "option", "or", "order", "outer", "over",
        "percent", "pivot", "plan", "precision", "primary", "print",
        "proc", "procedure", "public", "raiserror", "read", "readtext",
        "reconfigure", "references", "replication", "restore", "restrict",
        "return", "revert", "revoke", "right", "rollback", "rowcount",
        "rowguidcol", "rule", "save", "schema", "securityaudit", "select",
        "session_user", "set", "setuser", "shutdown", "some",
        "statistics", "system_user", "table", "tablesample", "textsize",
        "then", "to", "top", "trigger", "truncate",
        "try_convert", "tsequal", "union", "unique", "unpivot", "update",
        "use", "user", "values", "varying", "view", "waitfor", "when",
        "where", "while", "with", "writetext",
    };

    /// <summary>
    /// True when <paramref name="name"/> is reserved in the target engine's own
    /// grammar (case-insensitive), independent of whether JauntyQ's own
    /// tokenizer models it as a keyword. MariaDB rides the "mysql" dialect
    /// string, matching the rest of the codebase's convention (it shares
    /// MySQL's reserved-word grammar for this purpose). Unknown/unset dialects
    /// return false (no engine-specific info to check; the JauntyQ-keyword
    /// check the caller also runs is the only gate that applies).
    /// </summary>
    public static bool IsReservedInDialect(string name, string? dialect)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dialect))
            return false;

        if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            return Postgres.Contains(name);
        if (string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase))
            return MySql.Contains(name);
        if (string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase))
            return SqlServer.Contains(name);

        return false;
    }

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
