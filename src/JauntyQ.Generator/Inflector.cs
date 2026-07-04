namespace JauntyQ.Generator;

/// <summary>
/// Derives the canonical row-POCO name from a table name:
/// Shippers -> Shipper, Categories -> Category, OrderDetails -> OrderDetail.
/// Deliberately minimal English rules; when singularization produces the
/// same identifier as the entity accessor class (already-singular tables
/// like Region, or views like CurrentProductList), the caller falls back
/// to the collision-proof "&lt;Entity&gt;Row" form.
/// </summary>
public static class Inflector
{
    public static string Singularize(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // ...ies -> ...y   (Categories -> Category, Territories -> Territory)
        if (name.Length > 3 && name.EndsWith("ies", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 3) + "y";

        // ...xes/ches/shes/sses/zes -> drop "es"   (Boxes -> Box, Addresses -> Address)
        if (name.Length > 4 &&
            (name.EndsWith("xes", StringComparison.Ordinal) ||
             name.EndsWith("zes", StringComparison.Ordinal) ||
             name.EndsWith("ches", StringComparison.Ordinal) ||
             name.EndsWith("shes", StringComparison.Ordinal) ||
             name.EndsWith("sses", StringComparison.Ordinal)))
            return name.Substring(0, name.Length - 2);

        // ...s -> drop "s"   (Shippers -> Shipper, Employees -> Employee).
        // Words ending -ss/-us/-is (Status, Campus, Analysis) are not
        // plural-s; leave them alone so the Row-suffix fallback applies.
        if (name.Length > 1 && name.EndsWith("s", StringComparison.Ordinal)
            && !name.EndsWith("ss", StringComparison.Ordinal)
            && !name.EndsWith("us", StringComparison.Ordinal)
            && !name.EndsWith("is", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 1);

        return name;
    }

    /// <summary>
    /// Row-POCO name for a table: singularized entity name, or
    /// "&lt;Entity&gt;Row" when singularization collides with the entity
    /// accessor class name.
    /// </summary>
    public static string RowTypeName(string entityName)
    {
        string singular = Singularize(entityName);
        return string.Equals(singular, entityName, StringComparison.Ordinal)
            ? entityName + "Row"
            : singular;
    }
}
