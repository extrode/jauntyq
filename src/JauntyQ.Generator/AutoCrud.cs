using JauntyQ.Schema;

namespace JauntyQ.Generator;

/// <summary>
/// Synthesizes CRUD SQL for every table in the schema snapshot so consumers get
/// GetAll / GetById / Insert / Update / Delete without writing a line of SQL.
/// The synthesized SQL is fed through the exact same pipeline as user .sql files
/// (tokenizer, parser, validator, emitter), so directives-driven behavior
/// (@first on GetById), async twins, the shape guard and DbType binding all
/// apply uniformly. A user .sql file with the same entity + method name always
/// wins over the synthetic one.
/// </summary>
public static class AutoCrud
{
    public sealed class SyntheticQuery
    {
        public string EntityName { get; }
        public string MethodName { get; }
        public string Sql { get; }

        public SyntheticQuery(string entityName, string methodName, string sql)
        {
            EntityName = entityName;
            MethodName = methodName;
            Sql = sql;
        }
    }

    public static List<SyntheticQuery> Synthesize(DatabaseSchema schema)
    {
        var result = new List<SyntheticQuery>();

        foreach (var table in schema.Tables.Values)
        {
            // v1 synthesizes bare (unquoted) identifiers so the SQL is exactly
            // what a user would write by hand and flows through the minimal
            // parser unchanged. Tables/columns that need quoting are skipped.
            if (!IsBareIdentifier(table.Name))
                continue;

            var columns = new List<ColumnSchema>();
            bool allColumnsUsable = true;
            foreach (var col in table.Columns.Values)
            {
                if (!IsBareIdentifier(col.Name))
                {
                    allColumnsUsable = false;
                    break;
                }
                columns.Add(col);
            }
            if (!allColumnsUsable || columns.Count == 0)
                continue;

            string entityName = DialectMapper.ToPascalCase(table.Name);
            string colList = string.Join(", ", columns.Select(c => c.Name));

            // GetAll — always (works for views and PK-less tables too)
            result.Add(new SyntheticQuery(entityName, "GetAll",
                $"select {colList}\nfrom {table.Name}"));

            var pkCols = columns.Where(c => c.IsPrimaryKey).ToList();
            if (pkCols.Count == 0)
                continue; // views / heap tables: read-only beyond GetAll

            string pkWhere = string.Join(" and ", pkCols.Select(c => $"{table.Name}.{c.Name} = @{c.Name}"));

            // GetById — single row by primary key
            result.Add(new SyntheticQuery(entityName, "GetById",
                $"-- @first\nselect {colList}\nfrom {table.Name}\nwhere {pkWhere}"));

            // Insert — identity columns are database-assigned, never bound.
            // When the table has a single identity key and the snapshot knows
            // the dialect, the synthetic Insert returns the new id (-- @identity).
            var insertCols = columns.Where(c => !c.IsIdentity).ToList();
            if (insertCols.Count > 0)
            {
                string insertColList = string.Join(", ", insertCols.Select(c => c.Name));
                string insertParams = string.Join(", ", insertCols.Select(c => $"@{c.Name}"));
                bool returnsIdentity = !string.IsNullOrEmpty(schema.Dialect)
                    && columns.Count(c => c.IsIdentity) == 1;
                string prefix = returnsIdentity ? "-- @identity\n" : "";
                result.Add(new SyntheticQuery(entityName, "Insert",
                    $"{prefix}insert into {table.Name} ({insertColList})\nvalues ({insertParams})"));
            }

            // Update — SET every non-PK column, WHERE the full primary key
            var setCols = columns.Where(c => !c.IsPrimaryKey).ToList();
            if (setCols.Count > 0)
            {
                string setList = string.Join(", ", setCols.Select(c => $"{c.Name} = @{c.Name}"));
                string updateWhere = string.Join(" and ", pkCols.Select(c => $"{c.Name} = @{c.Name}"));
                result.Add(new SyntheticQuery(entityName, "Update",
                    $"update {table.Name}\nset {setList}\nwhere {updateWhere}"));
            }

            // Delete — WHERE the full primary key
            string deleteWhere = string.Join(" and ", pkCols.Select(c => $"{c.Name} = @{c.Name}"));
            result.Add(new SyntheticQuery(entityName, "Delete",
                $"delete from {table.Name}\nwhere {deleteWhere}"));
        }

        return result;
    }

    private static bool IsBareIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }
        return true;
    }
}
