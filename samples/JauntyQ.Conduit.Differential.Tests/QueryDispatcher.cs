using System.Collections;
using System.Reflection;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// Invokes a generated query by name against a JauntyDb the harness may not
/// name in source.
///
/// The five referenced Conduit assemblies each declare their own
/// JauntyQ.Generated.JauntyDb, so the type is ambiguous to the compiler and
/// every call has to go through reflection. That constraint buys R9: a query
/// added to the Conduit corpus appears as a method on all five and is
/// discovered here rather than enumerated.
/// </summary>
public static class QueryDispatcher
{
    public sealed class DispatchException(string message) : Exception(message);

    /// <summary>Entity accessor properties on JauntyDb (Articles, Users, ...).</summary>
    public static IReadOnlyList<string> EntityNames(object db) =>
        db.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.PropertyType.Namespace == "JauntyQ.Generated")
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Query methods on one entity accessor, as "Entity/Method".</summary>
    public static IReadOnlyList<string> QueryNames(object db, string entity)
    {
        object accessor = Accessor(db, entity);
        return accessor.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => $"{entity}/{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> AllQueryNames(object db) =>
        EntityNames(db).SelectMany(e => QueryNames(db, e))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Runs "Entity/Method" with arguments bound by parameter name and
    /// materializes whatever comes back into rows.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Invoke(
        object db, string query, IReadOnlyDictionary<string, object?> arguments)
    {
        string[] parts = query.Split('/');
        if (parts.Length != 2)
            throw new DispatchException($"Query name '{query}' is not in Entity/Method form.");

        object accessor = Accessor(db, parts[0]);
        MethodInfo method = Method(accessor, parts[0], parts[1], arguments);

        var parameters = method.GetParameters();
        var bound = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            string name = parameters[i].Name!;
            if (arguments.TryGetValue(name, out object? supplied))
            {
                bound[i] = Coerce(supplied, parameters[i].ParameterType);
            }
            else if (parameters[i].HasDefaultValue)
            {
                bound[i] = parameters[i].DefaultValue;
            }
            else
            {
                throw new DispatchException(
                    $"'{query}' needs an argument named '{name}'; the set supplied "
                    + $"[{string.Join(", ", arguments.Keys.OrderBy(k => k, StringComparer.Ordinal))}] does not carry it.");
            }
        }

        object? result;
        try
        {
            result = method.Invoke(accessor, bound);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new DispatchException($"'{query}' threw on invoke: {ex.InnerException.Message}");
        }

        return Materialize(Unwrap(result));
    }

    private static object Accessor(object db, string entity)
    {
        PropertyInfo? property = db.GetType().GetProperty(
            entity, BindingFlags.Public | BindingFlags.Instance);

        if (property is null)
            throw new DispatchException(
                $"No entity '{entity}'. Available: {string.Join(", ", EntityNames(db))}.");

        return property.GetValue(db)
            ?? throw new DispatchException($"Entity accessor '{entity}' returned null.");
    }

    /// <summary>
    /// Auto-CRUD emits overloads -- Upsert(row) beside Upsert(col, col, ...),
    /// likewise Delete and Update -- so a name alone does not identify a
    /// method. The overload every one of whose parameters the corpus supplies
    /// by name is the one meant; ties break toward the fewest parameters.
    /// </summary>
    private static MethodInfo Method(
        object accessor, string entity, string name, IReadOnlyDictionary<string, object?> arguments)
    {
        var candidates = accessor.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && string.Equals(m.Name, name, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
            throw new DispatchException(
                $"No query '{entity}/{name}'. Available on '{entity}': "
                + string.Join(", ", MethodNames(accessor)));

        if (candidates.Count == 1)
            return candidates[0];

        var satisfiable = candidates
            .Where(m => m.GetParameters().All(p =>
                arguments.ContainsKey(p.Name!) || p.HasDefaultValue))
            .OrderBy(m => m.GetParameters().Length)
            .ToList();

        if (satisfiable.Count == 0)
            throw new DispatchException(
                $"'{entity}/{name}' has {candidates.Count} overloads and the supplied arguments "
                + $"[{string.Join(", ", arguments.Keys.OrderBy(k => k, StringComparer.Ordinal))}] "
                + "satisfy none of them: "
                + string.Join(" | ", candidates.Select(m =>
                    string.Join(", ", m.GetParameters().Select(p => p.Name)))));

        return satisfiable[0];
    }

    private static IReadOnlyList<string> MethodNames(object accessorOwner) =>
        accessorOwner.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Conduit's generated methods are synchronous, but @stream queries emit
    /// IAsyncEnumerable and an async variant exists elsewhere in the product,
    /// so a Task result is unwrapped rather than materialized as one row
    /// holding a Task.
    /// </summary>
    private static object? Unwrap(object? result)
    {
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
            PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }
        return result;
    }

    private static object? Coerce(object? value, Type target)
    {
        if (value is null)
            return null;

        Type bare = Nullable.GetUnderlyingType(target) ?? target;
        if (bare.IsInstanceOfType(value))
            return value;

        // @each loaders take an IEnumerable<int>; the corpus supplies int[].
        if (value is IEnumerable && value is not string && !bare.IsPrimitive)
            return value;

        return System.Convert.ChangeType(value, bare, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Materialize(object? result)
    {
        if (result is null)
            return Array.Empty<IReadOnlyDictionary<string, object?>>();

        // A scalar (@identity's int, a count, an affected-row count) is one
        // row of one column, so it compares by the same path as a result set.
        if (IsScalar(result))
            return new[] { Row(("value", result)) };

        if (result is IEnumerable sequence)
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            foreach (object? item in sequence)
                rows.Add(item is null ? Row(("value", null)) : RowFromObject(item));
            return rows;
        }

        return new[] { RowFromObject(result) };
    }

    private static bool IsScalar(object value) =>
        value is string || value is decimal || value is DateTime || value is DateTimeOffset
        || value is Guid || value.GetType().IsPrimitive;

    private static IReadOnlyDictionary<string, object?> RowFromObject(object item)
    {
        if (IsScalar(item))
            return Row(("value", item));

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetIndexParameters().Length == 0)
                row[property.Name] = property.GetValue(item);

        return row;
    }

    private static IReadOnlyDictionary<string, object?> Row(params (string Name, object? Value)[] cells)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var cell in cells)
            row[cell.Name] = cell.Value;
        return row;
    }
}
