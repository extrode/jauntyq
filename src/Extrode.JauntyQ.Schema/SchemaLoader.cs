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

    /// <summary>
    /// Returns <paramref name="source"/> keyed by each value's name. A value
    /// with no name takes its key as its name; two values with the same name
    /// are a malformed snapshot, reported the way any other parse failure is.
    /// </summary>
    private static Dictionary<string, T> Rekey<T>(Dictionary<string, T> source, Func<T, string> getName, Action<T, string> setName, string what)
    {
        bool aligned = true;
        foreach (var pair in source)
        {
            if (string.IsNullOrEmpty(getName(pair.Value)))
                setName(pair.Value, pair.Key);
            if (!string.Equals(pair.Key, getName(pair.Value), StringComparison.Ordinal))
                aligned = false;
        }
        if (aligned)
            return source;

        var rekeyed = new Dictionary<string, T>(source.Count);
        foreach (var pair in source)
        {
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
