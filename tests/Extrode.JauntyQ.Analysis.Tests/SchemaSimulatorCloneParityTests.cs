using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Analysis.Migrations;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaSimulatorCloneParityTests
{
    private int _counter;
    private readonly HashSet<Type> _visited = new();

    private object Fill(Type type, string path)
    {
        object instance = Activator.CreateInstance(type)!;
        _visited.Add(type);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            Type t = prop.PropertyType;
            string p = path + "." + prop.Name;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)(prop.GetValue(instance) ?? Activator.CreateInstance(t)!);
                Type item = t.GetGenericArguments()[0];
                list.Add(item == typeof(string) ? p + "#0" : Fill(item, p + "[0]"));
                list.Add(item == typeof(string) ? p + "#1" : Fill(item, p + "[1]"));
                if (prop.CanWrite) prop.SetValue(instance, list);
                continue;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dict = (IDictionary)(prop.GetValue(instance) ?? Activator.CreateInstance(t)!);
                Type item = t.GetGenericArguments()[1];
                for (int i = 0; i < 2; i++)
                {
                    object value = Fill(item, p + "[" + i + "]");
                    var nameProp = item.GetProperty("Name");
                    string key = nameProp != null ? (string)nameProp.GetValue(value)! : p + "#" + i;
                    dict[key] = value;
                }
                if (prop.CanWrite) prop.SetValue(instance, dict);
                continue;
            }
            if (!prop.CanWrite)
                continue;
            prop.SetValue(instance, Scalar(t, prop.GetValue(instance), p));
        }
        return instance;
    }

    private object Scalar(Type t, object? current, string path)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;
        int n = ++_counter;
        if (u == typeof(string)) return path;
        if (u == typeof(bool)) return current is bool b ? !b : true;
        if (u == typeof(int)) return 1000 + n;
        if (u == typeof(long)) return 100000L + n;
        if (u.IsEnum)
        {
            var values = Enum.GetValues(u).Cast<object>().Where(v => !v.Equals(current)).ToArray();
            return values[n % values.Length];
        }
        if (u.IsClass) return Fill(u, path);
        throw new InvalidOperationException("Unhandled property type " + t + " at " + path);
    }

    [Fact]
    public void Clone_CarriesEveryPropertyOfEveryTypeReachableFromDatabaseSchema()
    {
        var snapshot = (DatabaseSchema)Fill(typeof(DatabaseSchema), "db");
        var errors = new List<AnalysisDiagnostic>();

        var clone = SchemaSimulator.Apply(snapshot, Array.Empty<(string, List<MigrationStatement>)>(), errors);

        Assert.Empty(errors);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(clone));
    }

    [Fact]
    public void Clone_SharesNoMutableObjectWithTheSnapshot()
    {
        var snapshot = (DatabaseSchema)Fill(typeof(DatabaseSchema), "db");

        var clone = SchemaSimulator.Apply(snapshot, Array.Empty<(string, List<MigrationStatement>)>(), new List<AnalysisDiagnostic>());

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Collect(snapshot, seen);
        var shared = new List<string>();
        FindShared(clone, seen, "db", shared);
        Assert.Empty(shared);
    }

    [Fact]
    public void Walker_ReachesEveryModelTypeInTheSchemaAssembly()
    {
        Fill(typeof(DatabaseSchema), "db");

        var modelTypes = typeof(DatabaseSchema).Assembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Schema", StringComparison.Ordinal))
            .ToList();
        Assert.All(modelTypes, t => Assert.Contains(t, _visited));
    }

    private static void Collect(object node, HashSet<object> seen)
    {
        if (node is string || node.GetType().IsValueType || !seen.Add(node))
            return;
        foreach (object child in Children(node))
            Collect(child, seen);
    }

    private static void FindShared(object node, HashSet<object> seen, string path, List<string> shared)
    {
        if (node is string || node.GetType().IsValueType)
            return;
        if (seen.Contains(node))
        {
            shared.Add(path);
            return;
        }
        int i = 0;
        foreach (object child in Children(node))
            FindShared(child, seen, path + "/" + i++, shared);
    }

    private static IEnumerable<object> Children(object node)
    {
        if (node is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
                if (e.Value != null) yield return e.Value;
            yield break;
        }
        if (node is IList list)
        {
            foreach (object? o in list)
                if (o != null) yield return o;
            yield break;
        }
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            object? v = prop.GetValue(node);
            if (v != null) yield return v;
        }
    }
}
