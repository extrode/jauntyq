using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Analysis.Migrations;

/// <summary>
/// Minimal DDL parser for migration files. Understands the statements that
/// change the table/column model (CREATE TABLE, DROP TABLE, ALTER TABLE
/// ADD/DROP/ALTER COLUMN, MySQL MODIFY, PostgreSQL ALTER COLUMN ... TYPE)
/// and classifies everything else as Ignored (cannot affect the model) or
/// Unsupported (JNT9001: the effective schema may be incomplete).
/// Statements split on top-level ';' and standalone GO.
/// </summary>
public static class MigrationParser
{
    public static List<MigrationStatement> Parse(string sql)
    {
        var statements = new List<MigrationStatement>();
        var tokens = SqlTokenizer.Tokenize(sql);

        var current = new List<Token>();
        int depth = 0;
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.End)
                break;
            if (token.Type == TokenType.Symbol && token.Value == "(")
                depth++;
            if (token.Type == TokenType.Symbol && token.Value == ")")
                depth--;
            if (token.Type == TokenType.Symbol && token.Value == ";" && depth == 0)
            {
                FlushStatement(current, statements);
                continue;
            }
            // GO batch separator: statements are commonly unterminated before
            // it, so it splits like ';'. Only at paren depth 0 — inside a
            // column list an identifier "go" is a legitimate name.
            if (depth == 0 && token.Type == TokenType.Identifier &&
                string.Equals(token.Value, "GO", StringComparison.OrdinalIgnoreCase))
            {
                FlushStatement(current, statements);
                continue;
            }
            current.Add(token);
        }
        FlushStatement(current, statements);

        return statements;
    }

    private static void FlushStatement(List<Token> tokens, List<MigrationStatement> statements)
    {
        if (tokens.Count == 0)
            return;
        statements.AddRange(ParseStatement(tokens));
        tokens.Clear();
    }

    private static List<MigrationStatement> ParseStatement(List<Token> tokens)
    {
        var tokenValues = new List<string>(tokens.Count);
        foreach (var t in tokens)
            tokenValues.Add(t.Value);
        string raw = string.Join(" ", tokenValues);

        if (Is(tokens, 0, "CREATE") && Is(tokens, 1, "TABLE"))
            return new List<MigrationStatement> { ParseCreateTable(tokens, raw) };

        if (Is(tokens, 0, "DROP") && Is(tokens, 1, "TABLE"))
            return new List<MigrationStatement> { ParseDropTable(tokens, raw) };

        // A single ALTER TABLE can carry several comma-separated actions
        // (ALTER TABLE t ADD a int, DROP COLUMN b, ADD c varchar(10)), each
        // independently applicable to the effective schema -- so this
        // expands into one MigrationStatement per action instead of one for
        // the whole clause.
        if (Is(tokens, 0, "ALTER") && Is(tokens, 1, "TABLE"))
            return ParseAlterTable(tokens, raw);

        // Statements that cannot change the table/column model.
        if ((Is(tokens, 0, "CREATE") && (Is(tokens, 1, "INDEX") || Is(tokens, 1, "UNIQUE"))) ||
            (Is(tokens, 0, "DROP") && Is(tokens, 1, "INDEX")) ||
            Is(tokens, 0, "SET") || Is(tokens, 0, "USE") ||
            Is(tokens, 0, "BEGIN") || Is(tokens, 0, "COMMIT") || Is(tokens, 0, "ROLLBACK") ||
            Is(tokens, 0, "GRANT") || Is(tokens, 0, "REVOKE") || Is(tokens, 0, "DENY"))
        {
            return new List<MigrationStatement> { new MigrationStatement { Kind = MigrationStatementKind.Ignored, RawText = raw } };
        }

        return new List<MigrationStatement> { new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = raw } };
    }

    private static MigrationStatement ParseCreateTable(List<Token> tokens, string raw)
    {
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.CreateTable, RawText = raw };
        int pos = 2;
        stmt.TableName = ReadObjectName(tokens, ref pos);
        if (stmt.TableName.Length == 0 || !IsSymbol(tokens, pos, "("))
            return Unsupported(raw);
        pos++; // skip (

        foreach (var def in SplitTopLevel(tokens, ref pos))
        {
            if (def.Count == 0)
                continue;

            // Table-level constraints: PRIMARY KEY (a, b) marks columns;
            // CONSTRAINT name PRIMARY KEY (...) likewise; others ignored.
            int c = 0;
            if (Is(def, 0, "CONSTRAINT"))
                c = 2; // skip CONSTRAINT <name>
            if (Is(def, c, "PRIMARY") && Is(def, c + 1, "KEY"))
            {
                foreach (var pkName in ReadParenNameList(def, c + 2))
                {
                    var col = stmt.Columns.Find(x => string.Equals(x.Name, pkName, StringComparison.OrdinalIgnoreCase));
                    if (col != null)
                    {
                        col.IsPrimaryKey = true;
                        col.IsNullable = false;
                    }
                }
                continue;
            }
            if (Is(def, c, "UNIQUE") || Is(def, c, "FOREIGN") || Is(def, c, "CHECK"))
                continue;

            var column = ParseColumnDef(def);
            if (column != null)
                stmt.Columns.Add(column);
        }

        return stmt.Columns.Count > 0 ? stmt : Unsupported(raw);
    }

    private static MigrationStatement ParseDropTable(List<Token> tokens, string raw)
    {
        int pos = 2;
        bool ifExists = false;
        if (Is(tokens, pos, "IF") && Is(tokens, pos + 1, "EXISTS"))
        {
            ifExists = true;
            pos += 2;
        }
        string name = ReadObjectName(tokens, ref pos);
        if (name.Length == 0)
            return Unsupported(raw);
        return new MigrationStatement
        {
            Kind = MigrationStatementKind.DropTable,
            TableName = name,
            IfExists = ifExists,
            RawText = raw
        };
    }

    /// <summary>One comma-separated action clause within an ALTER TABLE
    /// statement (ADD/DROP/ALTER/MODIFY), plus its column-def bodies. Bodies
    /// after the first are bare continuations (e.g. the "b int" in
    /// "ADD a int, b int") that belong to the same action rather than
    /// starting a new one.</summary>
    private sealed class AlterActionGroup
    {
        public string Action = string.Empty;
        public List<List<Token>> Bodies { get; } = new();
    }

    private static List<MigrationStatement> ParseAlterTable(List<Token> tokens, string raw)
    {
        int pos = 2;
        string tableName = ReadObjectName(tokens, ref pos);
        if (tableName.Length == 0)
            return new List<MigrationStatement> { Unsupported(raw) };

        var groups = GroupAlterActions(tokens, pos);
        if (groups == null || groups.Count == 0)
            return new List<MigrationStatement> { Unsupported(raw) };

        var results = new List<MigrationStatement>(groups.Count);
        foreach (var group in groups)
        {
            results.Add(group.Action switch
            {
                "ADD" => ParseAddAction(group.Bodies, tableName, raw),
                "DROP" => ParseDropAction(group.Bodies, tableName, raw),
                _ => ParseAlterOrModifyAction(group.Action, group.Bodies, tableName, raw)
            });
        }
        return results;
    }

    /// <summary>
    /// Splits the tokens after the table name on top-level commas, then
    /// groups those comma-separated clauses by which one opens a new action
    /// (ADD/DROP/ALTER/MODIFY) versus which is a bare continuation of the
    /// action before it. ADD/DROP/COLUMN aren't tokenizer keywords (they
    /// tokenize as plain identifiers), so without this grouping a second
    /// action in the same statement (e.g. "ADD a int, DROP COLUMN b") used
    /// to satisfy ParseColumnDef's "identifier identifier" shape and get
    /// misparsed as a phantom column instead of its own action.
    /// </summary>
    private static List<AlterActionGroup>? GroupAlterActions(List<Token> tokens, int pos)
    {
        var segments = SplitRemaining(tokens, pos);
        if (segments.Count == 0)
            return null;

        var groups = new List<AlterActionGroup>();
        foreach (var segment in segments)
        {
            if (segment.Count == 0)
                continue;

            string? action = Is(segment, 0, "ADD") ? "ADD"
                : Is(segment, 0, "DROP") ? "DROP"
                : Is(segment, 0, "ALTER") ? "ALTER"
                : Is(segment, 0, "MODIFY") ? "MODIFY"
                : null;

            if (action != null)
            {
                var group = new AlterActionGroup { Action = action };
                group.Bodies.Add(segment.GetRange(1, segment.Count - 1));
                groups.Add(group);
            }
            else if (groups.Count > 0)
            {
                groups[groups.Count - 1].Bodies.Add(segment);
            }
            else
            {
                // First clause has no recognizable action verb: malformed.
                return null;
            }
        }
        return groups;
    }

    // ALTER TABLE t ADD [COLUMN] <coldef> [, <coldef> ...]
    private static MigrationStatement ParseAddAction(List<List<Token>> bodies, string tableName, string raw)
    {
        var first = bodies[0];
        int p = 0;
        if (Is(first, p, "COLUMN"))
            p++;

        if (Is(first, p, "CONSTRAINT") || Is(first, p, "PRIMARY") ||
            Is(first, p, "UNIQUE") || Is(first, p, "FOREIGN") || Is(first, p, "CHECK"))
        {
            int constraintPos = p;
            if (Is(first, constraintPos, "CONSTRAINT"))
                constraintPos += 2; // skip CONSTRAINT <name>

            // ADD [CONSTRAINT name] PRIMARY KEY (a, b): the common
            // create-then-constrain pattern (a table created without an
            // inline PK, keyed later). Mirrors ParseCreateTable's
            // table-level PRIMARY KEY (...) handling instead of silently
            // dropping it — a table keyed only this way used to never
            // get IsPrimaryKey set, so UpsertKeyResolver silently skipped
            // auto-CRUD Upsert synthesis for it with no diagnostic at all.
            if (Is(first, constraintPos, "PRIMARY") && Is(first, constraintPos + 1, "KEY"))
            {
                var pkColumns = ReadParenNameList(first, constraintPos + 2);
                if (pkColumns.Count > 0)
                {
                    var pkStmt = new MigrationStatement { Kind = MigrationStatementKind.AddPrimaryKey, TableName = tableName, RawText = raw };
                    pkStmt.ColumnNames.AddRange(pkColumns);
                    return pkStmt;
                }
            }

            // UNIQUE/FOREIGN/CHECK constraints (and an unparseable ADD
            // PRIMARY KEY) aren't modeled — the simulator has no
            // secondary-index or FK population from migrations. Unsupported
            // means JNT9001 fires so the developer knows the effective
            // schema may be incomplete, instead of the change silently
            // vanishing, matching ALTER TABLE ... DROP CONSTRAINT below.
            return Unsupported(raw);
        }

        var stmt = new MigrationStatement { Kind = MigrationStatementKind.AddColumn, TableName = tableName, RawText = raw };
        var defs = new List<List<Token>> { first.GetRange(p, first.Count - p) };
        for (int i = 1; i < bodies.Count; i++)
            defs.Add(bodies[i]);

        foreach (var def in defs)
        {
            var column = ParseColumnDef(def);
            if (column == null)
                return Unsupported(raw);
            stmt.Columns.Add(column);
        }
        return stmt.Columns.Count > 0 ? stmt : Unsupported(raw);
    }

    // ALTER TABLE t DROP COLUMN a [, b ...]
    private static MigrationStatement ParseDropAction(List<List<Token>> bodies, string tableName, string raw)
    {
        var first = bodies[0];
        if (!Is(first, 0, "COLUMN"))
            return Unsupported(raw); // e.g. DROP CONSTRAINT — not modeled

        var stmt = new MigrationStatement { Kind = MigrationStatementKind.DropColumn, TableName = tableName, RawText = raw };
        for (int i = 1; i < first.Count; i++)
        {
            if (first[i].Type == TokenType.Identifier)
                stmt.ColumnNames.Add(BareName(first[i].Value));
        }
        for (int i = 1; i < bodies.Count; i++)
        {
            foreach (var t in bodies[i])
            {
                if (t.Type == TokenType.Identifier)
                    stmt.ColumnNames.Add(BareName(t.Value));
            }
        }
        return stmt.ColumnNames.Count > 0 ? stmt : Unsupported(raw);
    }

    // ALTER TABLE t ALTER COLUMN <coldef>            (SQL Server)
    // ALTER TABLE t ALTER COLUMN c TYPE <type>       (PostgreSQL)
    // ALTER TABLE t MODIFY [COLUMN] <coldef>         (MySQL)
    private static MigrationStatement ParseAlterOrModifyAction(string action, List<List<Token>> bodies, string tableName, string raw)
    {
        // Neither form supports comma-continuation in any dialect — each
        // additional column repeats "ALTER COLUMN"/"MODIFY" (its own group).
        if (bodies.Count != 1)
            return Unsupported(raw);

        var body = bodies[0];
        bool isAlterColumn = action == "ALTER";
        int p = 0;
        if (isAlterColumn)
        {
            if (!Is(body, p, "COLUMN"))
                return Unsupported(raw); // "ALTER TABLE t ALTER x" without COLUMN isn't modeled
            p++;
        }
        else if (Is(body, p, "COLUMN"))
        {
            p++;
        }

        // PostgreSQL: ALTER COLUMN c SET/DROP NOT NULL and ALTER COLUMN c
        // SET/DROP DEFAULT [expr] have no type token at all -- unlike
        // "ALTER COLUMN c TYPE newtype" below, or SQL Server's "ALTER
        // COLUMN c type NOT NULL" -- so they don't fit ParseColumnDef's
        // "name type ..." grammar. Falling through to it anyway used to
        // either misparse the literal keyword as the column's new DbType
        // and apply the OPPOSITE nullability (DROP NOT NULL: "DROP"
        // tokenizes as a plain identifier, so it satisfied the name+type
        // shape with DbType "drop", and the trailing "NOT NULL" tokens
        // were then read as narrowing to NOT NULL -- the reverse of what
        // dropping the constraint means) or fail outright (SET NOT NULL/
        // DEFAULT: "SET" tokenizes as a keyword, never an identifier, so
        // ParseColumnDef's def[1]-is-identifier check always fails),
        // reporting a fully-modelable nullability change or no-op as
        // JNT9001 unmodeled. Recognize the four forms directly instead.
        if (isAlterColumn && p < body.Count && body[p].Type == TokenType.Identifier &&
            (Is(body, p + 1, "SET") || Is(body, p + 1, "DROP")))
        {
            bool isDrop = Is(body, p + 1, "DROP");
            if (Is(body, p + 2, "NOT") && Is(body, p + 3, "NULL"))
            {
                var nullStmt = new MigrationStatement
                {
                    Kind = MigrationStatementKind.AlterColumnNullability,
                    TableName = tableName,
                    RawText = raw,
                    NullableAfter = isDrop
                };
                nullStmt.ColumnNames.Add(BareName(body[p].Value));
                return nullStmt;
            }
            if (Is(body, p + 2, "DEFAULT"))
            {
                // No schema-shape impact: ColumnSchema tracks no default value.
                return new MigrationStatement { Kind = MigrationStatementKind.Ignored, TableName = tableName, RawText = raw };
            }
        }

        var def = new List<Token>();
        for (int i = p; i < body.Count; i++)
        {
            // PostgreSQL: drop the TYPE keyword so the def reads name+type
            if (body[i].Type == TokenType.Identifier &&
                string.Equals(body[i].Value, "TYPE", StringComparison.OrdinalIgnoreCase) &&
                def.Count == 1)
                continue;
            def.Add(body[i]);
        }

        var column = ParseColumnDef(def);
        if (column == null)
            return Unsupported(raw);
        var stmt = new MigrationStatement { Kind = MigrationStatementKind.AlterColumn, TableName = tableName, RawText = raw };
        stmt.Columns.Add(column);
        return stmt;
    }

    /// <summary>
    /// Parses "name type[(facets)] [flags]" into a ColumnSchema. Facets fill
    /// maxLength for text/binary and precision/scale for decimal/numeric.
    /// Flags: NOT NULL, NULL, PRIMARY KEY, IDENTITY[(s,i)]; DEFAULT values
    /// are skipped. Returns null when the shape is unrecognizable.
    /// </summary>
    private static ColumnSchema? ParseColumnDef(List<Token> def)
    {
        if (def.Count < 2 || def[0].Type != TokenType.Identifier || def[1].Type != TokenType.Identifier)
            return null;

        var column = new ColumnSchema
        {
            Name = BareName(def[0].Value),
            DbType = def[1].Value.ToLowerInvariant(),
            IsNullable = true
        };

        // Postgres SERIAL/BIGSERIAL/SMALLSERIAL are sugar for an integer
        // column with a nextval() sequence default -- the type name itself
        // IS the identity signal (unlike SQL Server's "int IDENTITY", a
        // separate trailing keyword). The flag-scanning loop below also
        // checks for a literal "SERIAL" token, but that can never match
        // here: "serial" was just consumed above as the column's DbType
        // (def[1]), not seen again at pos 2+. Matches the live
        // PostgresExtractor, whose is_identity check is column_default LIKE
        // 'nextval(%'.
        if (column.DbType is "serial" or "bigserial" or "smallserial")
            column.IsIdentity = true;

        int pos = 2;

        // Some Postgres base types are multi-word ("double precision",
        // "character varying", "bit varying", "timestamp/time [with|without]
        // time zone"), matching DialectMapper.MapDbTypeToCSharp's own
        // multi-word case-arm strings. The facet paren for these (if any)
        // follows the LAST word, not the first, so the continuation words
        // must be absorbed into DbType before the "(facets)" check below --
        // otherwise it looks for "(" immediately after the first word and
        // silently never finds "character varying(255)"'s "(255)", losing
        // the facet entirely (a live pull of the same column would still
        // report MaxLength=255).
        if (column.DbType == "double" && Is(def, pos, "PRECISION"))
        {
            column.DbType += " precision";
            pos++;
        }
        else if ((column.DbType == "character" || column.DbType == "bit") && Is(def, pos, "VARYING"))
        {
            column.DbType += " varying";
            pos++;
        }
        else if (column.DbType is "timestamp" or "time")
        {
            if (Is(def, pos, "WITHOUT") && Is(def, pos + 1, "TIME") && Is(def, pos + 2, "ZONE"))
            {
                column.DbType += " without time zone";
                pos += 3;
            }
            else if (Is(def, pos, "WITH") && Is(def, pos + 1, "TIME") && Is(def, pos + 2, "ZONE"))
            {
                column.DbType += " with time zone";
                pos += 3;
            }
        }

        // (facets): (40), (10,2), (max)
        int? first = null, second = null;
        bool isMax = false;
        if (IsSymbol(def, pos, "("))
        {
            pos++;
            if (pos < def.Count && def[pos].Type == TokenType.Number && int.TryParse(def[pos].Value, out int f))
            {
                first = f;
                pos++;
                if (IsSymbol(def, pos, ",") && pos + 1 < def.Count &&
                    def[pos + 1].Type == TokenType.Number && int.TryParse(def[pos + 1].Value, out int sec))
                {
                    second = sec;
                    pos += 2;
                }
            }
            else if (Is(def, pos, "MAX"))
            {
                isMax = true;
                pos++;
            }
            if (IsSymbol(def, pos, ")"))
                pos++;
        }

        ApplyFacets(column, first, second, isMax);

        // flags
        while (pos < def.Count)
        {
            if (Is(def, pos, "NOT") && Is(def, pos + 1, "NULL"))
            {
                column.IsNullable = false;
                pos += 2;
                continue;
            }
            if (Is(def, pos, "NULL"))
            {
                column.IsNullable = true;
                pos++;
                continue;
            }
            if (Is(def, pos, "PRIMARY") && Is(def, pos + 1, "KEY"))
            {
                column.IsPrimaryKey = true;
                column.IsNullable = false;
                pos += 2;
                continue;
            }
            if (Is(def, pos, "IDENTITY") || Is(def, pos, "AUTO_INCREMENT") ||
                Is(def, pos, "AUTOINCREMENT") || Is(def, pos, "SERIAL"))
            {
                column.IsIdentity = true;
                pos++;
                // optional IDENTITY(seed, increment)
                if (IsSymbol(def, pos, "("))
                {
                    while (pos < def.Count && !IsSymbol(def, pos, ")"))
                        pos++;
                    if (pos < def.Count)
                        pos++;
                }
                continue;
            }
            if (Is(def, pos, "DEFAULT"))
            {
                // skip the default expression (single token or parenthesized)
                pos++;
                if (IsSymbol(def, pos, "("))
                {
                    int depth = 0;
                    while (pos < def.Count)
                    {
                        if (IsSymbol(def, pos, "(")) depth++;
                        if (IsSymbol(def, pos, ")") && --depth == 0) { pos++; break; }
                        pos++;
                    }
                }
                else if (pos < def.Count)
                {
                    pos++;
                }
                continue;
            }
            pos++; // unknown flag (COLLATE x, CONSTRAINT inline, ...) — skip token
        }

        return column;
    }

    private static void ApplyFacets(ColumnSchema column, int? first, int? second, bool isMax)
    {
        string t = column.DbType;
        bool isText = t.Contains("char") || t.Contains("text") || t.Contains("clob");
        bool isBinary = t.Contains("binary") || t == "bytea" || t.Contains("blob") || t == "image";
        bool isDecimal = t is "decimal" or "numeric" or "money" or "smallmoney";
        // Postgres "bit"/"bit varying": the (n) facet is the bit length, not
        // a byte count, but DialectMapper.MapDbTypeToCSharp's "bit" case only
        // needs a length vs. null distinction (bare/length<=1 -> bool,
        // otherwise falls through to the unmapped "object" case) -- without
        // capturing it here, a migration-declared "bit(5)" always parsed as
        // MaxLength null, which wrongly satisfies that "length is null"
        // guard and mistyped a 5-bit bitstring as bool. A live pull of the
        // same column reports MaxLength=5 and correctly leaves it unmapped.
        bool isBitString = t is "bit" or "bit varying";

        if (isText || isBinary)
        {
            column.MaxLength = isMax ? -1 : first;
            if (isText)
                column.IsUnicode = t.StartsWith("n", StringComparison.Ordinal);
        }
        else if (isDecimal)
        {
            column.Precision = first;
            column.Scale = second ?? 0;
        }
        else if (isBitString)
        {
            column.MaxLength = first;
        }
    }

    private static MigrationStatement Unsupported(string raw) =>
        new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = raw };

    private static bool Is(List<Token> tokens, int index, string word) =>
        index >= 0 && index < tokens.Count &&
        (tokens[index].Type == TokenType.Identifier || tokens[index].Type == TokenType.Keyword) &&
        string.Equals(tokens[index].Value, word, StringComparison.OrdinalIgnoreCase);

    private static bool IsSymbol(List<Token> tokens, int index, string symbol) =>
        index >= 0 && index < tokens.Count &&
        tokens[index].Type == TokenType.Symbol && tokens[index].Value == symbol;

    /// <summary>dbo.Gadgets -> Gadgets; bracket quoting already stripped by the tokenizer.</summary>
    private static string BareName(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    private static string ReadObjectName(List<Token> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier)
        {
            string name = BareName(tokens[pos].Value);
            pos++;
            // tokenizer may split [dbo].[Gadgets] into dbo . Gadgets
            while (IsSymbol(tokens, pos, ".") && pos + 1 < tokens.Count && tokens[pos + 1].Type == TokenType.Identifier)
            {
                name = BareName(tokens[pos + 1].Value);
                pos += 2;
            }
            return name;
        }
        return string.Empty;
    }

    /// <summary>Splits the tokens of a parenthesized body (starting after the
    /// opening paren) into top-level comma-separated chunks; advances pos past
    /// the closing paren.</summary>
    private static List<List<Token>> SplitTopLevel(List<Token> tokens, ref int pos)
    {
        var result = new List<List<Token>>();
        var current = new List<Token>();
        int depth = 0;
        while (pos < tokens.Count)
        {
            var t = tokens[pos];
            if (t.Type == TokenType.Symbol && t.Value == "(")
            {
                depth++;
                current.Add(t);
            }
            else if (t.Type == TokenType.Symbol && t.Value == ")")
            {
                if (depth == 0)
                {
                    pos++;
                    break;
                }
                depth--;
                current.Add(t);
            }
            else if (t.Type == TokenType.Symbol && t.Value == "," && depth == 0)
            {
                result.Add(new List<Token>(current));
                current.Clear();
            }
            else
            {
                current.Add(t);
            }
            pos++;
        }
        if (current.Count > 0)
            result.Add(current);
        return result;
    }

    /// <summary>Splits the remaining tokens into top-level comma-separated chunks.</summary>
    private static List<List<Token>> SplitRemaining(List<Token> tokens, int pos)
    {
        var result = new List<List<Token>>();
        var current = new List<Token>();
        int depth = 0;
        for (int i = pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Symbol && t.Value == "(") depth++;
            if (t.Type == TokenType.Symbol && t.Value == ")") depth--;
            if (t.Type == TokenType.Symbol && t.Value == "," && depth == 0)
            {
                result.Add(new List<Token>(current));
                current.Clear();
                continue;
            }
            current.Add(t);
        }
        if (current.Count > 0)
            result.Add(current);
        return result;
    }

    private static List<string> ReadParenNameList(List<Token> tokens, int pos)
    {
        var names = new List<string>();
        if (!IsSymbol(tokens, pos, "("))
            return names;
        for (int i = pos + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Symbol && tokens[i].Value == ")")
                break;
            if (tokens[i].Type == TokenType.Identifier)
                names.Add(BareName(tokens[i].Value));
        }
        return names;
    }
}
