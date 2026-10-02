using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Extrode.JauntyQ.Schema;

public static class SchemaLoader
{
    public static DatabaseSchema Load(string json)
    {
        // The JSON literal `null` deserializes to a null DatabaseSchema without
        // a JsonException. Substituting an empty schema here (the old behavior)
        // made a corrupted/blanked snapshot indistinguishable from a database
        // with no tables: the generator would validate every query against
        // nothing and `schema verify` would report every table as new. A null
        // snapshot is a parse failure, and every caller already handles
        // JsonException from malformed input on this same path.
        var schema = JsonSerializer.Deserialize(json, SchemaJsonContext.Default.DatabaseSchema)
            ?? throw new JsonException("schema snapshot JSON was the literal 'null', not a snapshot object");

        // An explicit JSON null overwrites a property's initializer, so a
        // hand-edited "indexes": null or "dbType": null reached every generator
        // stage as a NullReferenceException. A null property means the same as
        // an omitted one; a null entry inside a collection has no omitted
        // equivalent and is a malformed snapshot.
        Normalize(schema);

        // Everything downstream looks an object up by its "name", and
        // SchemaSimulator's clone keys by it too. A hand-edited snapshot whose
        // dictionary key differs from that name ("Customers": { "name":
        // "customers" }) used to crash auto-CRUD emission with a
        // KeyNotFoundException, and with a pending migration it diffed as a
        // dropped table plus an added one. Functions and user types keep their
        // keys: a function's key carries its argument types.
        schema.Tables = Rekey(schema.Tables, t => t.Name, (t, n) => t.Name = n, "table");
        foreach (var table in schema.Tables.Values)
            table.Columns = Rekey(table.Columns, c => c.Name, (c, n) => c.Name = n, "column in table '" + table.Name + "'");
        schema.Procedures = Rekey(schema.Procedures, p => p.Name, (p, n) => p.Name = n, "procedure");
        schema.Sequences = Rekey(schema.Sequences, q => q.Name, (q, n) => q.Name = n, "sequence");
        schema.Enums = Rekey(schema.Enums, e => e.Name, (e, n) => e.Name = n, "enum");
        return schema;
    }

    private static void Normalize(DatabaseSchema schema)
    {
        schema.Dialect ??= string.Empty;
        schema.Tables = Entries(schema.Tables, "table");
        foreach (var table in schema.Tables.Values)
        {
            table.Name ??= string.Empty;
            table.Columns = Entries(table.Columns, "column");
            foreach (var column in table.Columns.Values)
                Normalize(column);
            table.Indexes = Items(table.Indexes, "index");
            foreach (var index in table.Indexes)
            {
                index.Name ??= string.Empty;
                index.Columns = Items(index.Columns, "index column");
            }
        }
        schema.ForeignKeys = Items(schema.ForeignKeys, "foreign key");
        foreach (var fk in schema.ForeignKeys)
        {
            fk.FromTable ??= string.Empty;
            fk.FromColumn ??= string.Empty;
            fk.ToTable ??= string.Empty;
            fk.ToColumn ??= string.Empty;
        }
        schema.Procedures = Entries(schema.Procedures, "procedure");
        foreach (var proc in schema.Procedures.Values)
        {
            proc.Name ??= string.Empty;
            proc.Params = Items(proc.Params, "procedure parameter");
            foreach (var p in proc.Params)
            {
                p.Name ??= string.Empty;
                p.DbType ??= string.Empty;
            }
            proc.Results = Items(proc.Results, "procedure result column");
            foreach (var column in proc.Results)
                Normalize(column);
        }
        schema.Sequences = Entries(schema.Sequences, "sequence");
        foreach (var sequence in schema.Sequences.Values)
            sequence.Name ??= string.Empty;
        schema.Enums = Entries(schema.Enums, "enum");
        foreach (var e in schema.Enums.Values)
        {
            e.Name ??= string.Empty;
            e.Members = Items(e.Members, "enum member");
            foreach (var m in e.Members)
            {
                m.Value ??= string.Empty;
                m.CSharpName ??= string.Empty;
            }
        }
        schema.Functions = Entries(schema.Functions, "function");
        foreach (var fn in schema.Functions.Values)
        {
            fn.Name ??= string.Empty;
            fn.Params = Items(fn.Params, "function parameter");
            foreach (var p in fn.Params)
            {
                p.Name ??= string.Empty;
                p.DbType ??= string.Empty;
            }
            fn.Return ??= new FunctionReturn();
            fn.Return.DbType ??= string.Empty;
        }
        schema.UserTypes = Entries(schema.UserTypes, "user type");
        foreach (var type in schema.UserTypes.Values)
        {
            type.Name ??= string.Empty;
            type.Members = Items(type.Members, "user type member");
            foreach (var column in type.Members)
                Normalize(column);
        }
    }

    private static void Normalize(ColumnSchema column)
    {
        column.Name ??= string.Empty;
        column.DbType ??= string.Empty;
    }

    private static Dictionary<string, T> Entries<T>(Dictionary<string, T>? source, string what) where T : class
    {
        if (source == null)
            return new Dictionary<string, T>();
        foreach (var pair in source)
            if (pair.Value == null)
                throw new JsonException("schema snapshot has a null " + what + " entry '" + pair.Key + "'");
        return source;
    }

    private static List<T> Items<T>(List<T>? source, string what) where T : class
    {
        if (source == null)
            return new List<T>();
        foreach (var item in source)
            if (item == null)
                throw new JsonException("schema snapshot has a null " + what + " in a list");
        return source;
    }

    /// <summary>
    /// Returns <paramref name="source"/> keyed by each value's name. A value
    /// with no name takes its key as its name; two values with the same name
    /// are a malformed snapshot, reported the way any other parse failure is.
    /// </summary>
    private static Dictionary<string, T> Rekey<T>(Dictionary<string, T> source, Func<T, string> getName, Action<T, string> setName, string what)
    {
        var rekeyed = new Dictionary<string, T>(source.Count);
        foreach (var pair in source)
        {
            if (string.IsNullOrEmpty(getName(pair.Value)))
                setName(pair.Value, pair.Key);
            string name = getName(pair.Value);
            if (rekeyed.ContainsKey(name))
                throw new JsonException("schema snapshot has two entries named '" + name + "' (" + what + ")");
            rekeyed[name] = pair.Value;
        }
        return rekeyed;
    }

    public static string Serialize(DatabaseSchema schema)
    {
        return JsonSerializer.Serialize(schema, SchemaJsonContext.Default.DatabaseSchema);
    }
}
