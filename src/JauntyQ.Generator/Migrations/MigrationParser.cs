using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator.Migrations;

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
        statements.Add(ParseStatement(tokens));
        tokens.Clear();
    }

    private static MigrationStatement ParseStatement(List<Token> tokens)
    {
        string raw = string.Join(" ", tokens.Select(t => t.Value));

        if (Is(tokens, 0, "CREATE") && Is(tokens, 1, "TABLE"))
            return ParseCreateTable(tokens, raw);

        if (Is(tokens, 0, "DROP") && Is(tokens, 1, "TABLE"))
            return ParseDropTable(tokens, raw);

        if (Is(tokens, 0, "ALTER") && Is(tokens, 1, "TABLE"))
            return ParseAlterTable(tokens, raw);

        // Statements that cannot change the table/column model.
        if ((Is(tokens, 0, "CREATE") && (Is(tokens, 1, "INDEX") || Is(tokens, 1, "UNIQUE"))) ||
            (Is(tokens, 0, "DROP") && Is(tokens, 1, "INDEX")) ||
            Is(tokens, 0, "SET") || Is(tokens, 0, "USE") ||
            Is(tokens, 0, "BEGIN") || Is(tokens, 0, "COMMIT") || Is(tokens, 0, "ROLLBACK") ||
            Is(tokens, 0, "GRANT") || Is(tokens, 0, "REVOKE") || Is(tokens, 0, "DENY"))
        {
            return new MigrationStatement { Kind = MigrationStatementKind.Ignored, RawText = raw };
        }

        return new MigrationStatement { Kind = MigrationStatementKind.Unsupported, RawText = raw };
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

    private static MigrationStatement ParseAlterTable(List<Token> tokens, string raw)
    {
        int pos = 2;
        string tableName = ReadObjectName(tokens, ref pos);
        if (tableName.Length == 0)
            return Unsupported(raw);

        // ALTER TABLE t ADD [COLUMN] <coldef> [, <coldef> ...]
        if (Is(tokens, pos, "ADD"))
        {
            pos++;
            if (Is(tokens, pos, "COLUMN"))
                pos++;
            if (Is(tokens, pos, "CONSTRAINT") || Is(tokens, pos, "PRIMARY") ||
                Is(tokens, pos, "UNIQUE") || Is(tokens, pos, "FOREIGN") || Is(tokens, pos, "CHECK"))
                return new MigrationStatement { Kind = MigrationStatementKind.Ignored, RawText = raw };

            var stmt = new MigrationStatement { Kind = MigrationStatementKind.AddColumn, TableName = tableName, RawText = raw };
            foreach (var def in SplitRemaining(tokens, pos))
            {
                var column = ParseColumnDef(def);
                if (column == null)
                    return Unsupported(raw);
                stmt.Columns.Add(column);
            }
            return stmt.Columns.Count > 0 ? stmt : Unsupported(raw);
        }

        // ALTER TABLE t DROP COLUMN a [, b ...]
        if (Is(tokens, pos, "DROP") && Is(tokens, pos + 1, "COLUMN"))
        {
            var stmt = new MigrationStatement { Kind = MigrationStatementKind.DropColumn, TableName = tableName, RawText = raw };
            for (int i = pos + 2; i < tokens.Count; i++)
            {
                if (tokens[i].Type == TokenType.Identifier)
                    stmt.ColumnNames.Add(BareName(tokens[i].Value));
            }
            return stmt.ColumnNames.Count > 0 ? stmt : Unsupported(raw);
        }

        // ALTER TABLE t ALTER COLUMN <coldef>            (SQL Server)
        // ALTER TABLE t ALTER COLUMN c TYPE <type>       (PostgreSQL)
        // ALTER TABLE t MODIFY [COLUMN] <coldef>         (MySQL)
        bool isAlterColumn = Is(tokens, pos, "ALTER") && Is(tokens, pos + 1, "COLUMN");
        bool isModify = Is(tokens, pos, "MODIFY");
        if (isAlterColumn || isModify)
        {
            pos += isAlterColumn ? 2 : 1;
            if (isModify && Is(tokens, pos, "COLUMN"))
                pos++;

            var def = new List<Token>();
            for (int i = pos; i < tokens.Count; i++)
            {
                // PostgreSQL: drop the TYPE keyword so the def reads name+type
                if (tokens[i].Type == TokenType.Identifier &&
                    string.Equals(tokens[i].Value, "TYPE", StringComparison.OrdinalIgnoreCase) &&
                    def.Count == 1)
                    continue;
                def.Add(tokens[i]);
            }

            var column = ParseColumnDef(def);
            if (column == null)
                return Unsupported(raw);
            var stmt = new MigrationStatement { Kind = MigrationStatementKind.AlterColumn, TableName = tableName, RawText = raw };
            stmt.Columns.Add(column);
            return stmt;
        }

        return Unsupported(raw);
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

        int pos = 2;

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

    private static IEnumerable<string> ReadParenNameList(List<Token> tokens, int pos)
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
