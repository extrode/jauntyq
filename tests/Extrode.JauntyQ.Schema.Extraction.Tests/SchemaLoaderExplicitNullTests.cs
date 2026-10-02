using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaLoaderExplicitNullTests
{
    private int _counter;

    private object Fill(Type type, string path)
    {
        object instance = Activator.CreateInstance(type)!;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length > 0)
                continue;
            Type t = prop.PropertyType;
            string p = path + "." + prop.Name;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(t)!;
                Type item = t.GetGenericArguments()[0];
                list.Add(item == typeof(string) ? "v" + ++_counter : Fill(item, p + "[0]"));
                prop.SetValue(instance, list);
                continue;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dict = (IDictionary)Activator.CreateInstance(t)!;
                object value = Fill(t.GetGenericArguments()[1], p + "[0]");
                dict[value.GetType().GetProperty("Name")!.GetValue(value)!] = value;
                prop.SetValue(instance, dict);
                continue;
            }
            Type u = Nullable.GetUnderlyingType(t) ?? t;
            int n = ++_counter;
            object v = u == typeof(string) ? "v" + n
                : u == typeof(bool) ? true
                : u == typeof(int) ? 1000 + n
                : u == typeof(long) ? 100000L + n
                : u.IsEnum ? Enum.GetValues(u).GetValue(Enum.GetValues(u).Length - 1)!
                : u.IsClass ? Fill(u, p)
                : throw new InvalidOperationException("Unhandled property type " + t + " at " + p);
            prop.SetValue(instance, v);
        }
        return instance;
    }

    private static IEnumerable<string> NullablePaths(JsonNode node, string path)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                string p = path + "." + pair.Key;
                if (pair.Value is JsonObject or JsonArray || pair.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    yield return p;
                if (pair.Value != null)
                    foreach (var inner in NullablePaths(pair.Value, p))
                        yield return inner;
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is JsonObject)
                    yield return path + "[" + i + "]";
                foreach (var inner in NullablePaths(arr[i]!, path + "[" + i + "]"))
                    yield return inner;
            }
        }
    }

    private static string WithNullAt(string json, string path)
    {
        var root = JsonNode.Parse(json)!;
        var segments = path.Substring(1).Replace("[", ".[").Split('.');
        JsonNode parent = root;
        for (int i = 0; i < segments.Length - 1; i++)
            parent = segments[i].StartsWith("[") ? parent[int.Parse(segments[i].Trim('[', ']'))]! : parent[segments[i]]!;
        string last = segments[^1];
        if (last.StartsWith("["))
            parent.AsArray()[int.Parse(last.Trim('[', ']'))] = null;
        else
            parent.AsObject()[last] = null;
        return root.ToJsonString();
    }

    private static void CollectNulls(object node, string path, NullabilityInfoContext context, List<string> nulls)
    {
        if (node is string || node.GetType().IsValueType)
            return;
        if (node is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
            {
                if (e.Value == null) nulls.Add(path + "[" + e.Key + "]");
                else CollectNulls(e.Value, path + "[" + e.Key + "]", context, nulls);
            }
            return;
        }
        if (node is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) nulls.Add(path + "[" + i + "]");
                else CollectNulls(list[i]!, path + "[" + i + "]", context, nulls);
            }
            return;
        }
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            object? v = prop.GetValue(node);
            if (v == null)
            {
                if (!prop.PropertyType.IsValueType && context.Create(prop).ReadState == NullabilityState.NotNull)
                    nulls.Add(path + "." + prop.Name);
            }
            else
                CollectNulls(v, path + "." + prop.Name, context, nulls);
        }
    }

    private string FullSnapshot() => SchemaLoader.Serialize((DatabaseSchema)Fill(typeof(DatabaseSchema), "db"));

    [Fact]
    public void ANullAnywhereInTheSnapshot_LoadsWithNoNullMembers_OrIsRejectedAsMalformed()
    {
        string json = FullSnapshot();
        var paths = NullablePaths(JsonNode.Parse(json)!, "").ToList();
        var context = new NullabilityInfoContext();
        var failures = new List<string>();

        foreach (var path in paths)
        {
            DatabaseSchema schema;
            try
            {
                schema = SchemaLoader.Load(WithNullAt(json, path));
            }
            catch (JsonException)
            {
                continue;
            }
            catch (Exception ex)
            {
                failures.Add(path + " threw " + ex.GetType().Name);
                continue;
            }
            var nulls = new List<string>();
            CollectNulls(schema, "db", context, nulls);
            if (nulls.Count > 0)
                failures.Add(path + " left " + string.Join(", ", nulls));
        }

        Assert.True(paths.Count > 60, paths.Count.ToString());
        Assert.Empty(failures);
    }

    [Fact]
    public void ANullCollection_LoadsAsAnEmptyOne()
    {
        var schema = SchemaLoader.Load(@"{ ""foreignKeys"": null, ""procedures"": null, ""tables"": { ""t"": { ""name"": ""t"",
            ""columns"": { ""id"": { ""name"": ""id"", ""dbType"": null } }, ""indexes"": null } } }");

        Assert.Empty(schema.ForeignKeys);
        Assert.Empty(schema.Procedures);
        Assert.Empty(schema.Tables["t"].Indexes);
        Assert.Equal("", schema.Tables["t"].Columns["id"].DbType);
    }

    [Fact]
    public void ANullFunctionReturn_LoadsAsTheDefaultReturn()
    {
        var schema = SchemaLoader.Load(@"{ ""functions"": { ""f"": { ""name"": ""f"", ""return"": null } } }");

        Assert.Equal("", schema.Functions["f"].Return.DbType);
        Assert.True(schema.Functions["f"].Return.IsNullable);
    }

    [Theory]
    [InlineData(@"{ ""tables"": { ""t"": null } }", "null table entry 't'")]
    [InlineData(@"{ ""tables"": { ""t"": { ""columns"": { ""id"": null } } } }", "null column entry 'id'")]
    [InlineData(@"{ ""foreignKeys"": [ null ] }", "null foreign key")]
    [InlineData(@"{ ""tables"": { ""t"": { ""indexes"": [ { ""columns"": [ ""a"", null ] } ] } } }", "null index column")]
    [InlineData(@"{ ""userTypes"": { ""u"": null } }", "null user type entry 'u'")]
    public void ANullEntryInACollection_IsAMalformedSnapshot(string json, string message)
    {
        var ex = Assert.Throws<JsonException>(() => SchemaLoader.Load(json));

        Assert.Contains(message, ex.Message);
    }
}
