using System.Globalization;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// The coarse kind of a returned value, carried alongside the value itself so
/// that a type difference is comparable rather than erased.
///
/// 017's <see cref="RowNormalizer"/> deliberately collapses a bool to 1/0,
/// because for its corpus the difference carries no meaning. For a corpus
/// written to stress boolean handling that collapse erases the finding: a
/// PostgreSQL bool, a MySQL integer and a SQL Server refusal would all render
/// as agreement on 1. Coarse rather than the CLR type name is the point in the
/// other direction -- SQL Server returning int where PostgreSQL returns bigint
/// for the same count is a provider detail and must stay agreement.
/// </summary>
public enum Shape
{
    Null,
    Boolean,
    Integer,
    Fractional,
    Text,
    Temporal,
    Binary,
    Other,
}

public static class ValueShape
{
    public static Shape Classify(object? value) => value switch
    {
        null or DBNull => Shape.Null,
        bool => Shape.Boolean,
        sbyte or byte or short or ushort or int or uint or long or ulong => Shape.Integer,
        decimal or float or double => Shape.Fractional,
        string or char => Shape.Text,
        DateTime or DateTimeOffset or TimeSpan or DateOnly or TimeOnly => Shape.Temporal,
        byte[] => Shape.Binary,
        _ => Shape.Other,
    };

    /// <summary>
    /// The value half of the pair. Reuses 017's rules, which are right once the
    /// shape travels beside them: integer widths collapse, decimal trailing
    /// zeros are a scale artifact, a timestamp compares to the second.
    /// </summary>
    public static string RenderValue(object? value)
    {
        object? normalized = RowNormalizer.Normalize(value);
        return normalized switch
        {
            null => "null",
            string s => $"'{s}'",
            _ => Convert.ToString(normalized, CultureInfo.InvariantCulture) ?? "",
        };
    }

    public static string Render(object? value) =>
        $"{Classify(value).ToString().ToLowerInvariant()}:{RenderValue(value)}";

    /// <summary>
    /// Rows are rendered by ordinal, never by column name. Engines name the
    /// column of an unaliased expression differently -- PostgreSQL ?column?,
    /// MySQL the expression text, SQL Server nothing at all -- and that naming
    /// is not a claim this suite makes.
    /// </summary>
    public static string RenderRow(IReadOnlyList<object?> row) =>
        "[" + string.Join(", ", row.Select(Render)) + "]";

    /// <summary>
    /// The whole result, as the string two engines are grouped by. Unordered
    /// results are sorted so that row order does not split a group when the
    /// case makes no ordering claim.
    /// </summary>
    public static string RenderResult(IReadOnlyList<IReadOnlyList<object?>> rows, bool ordered)
    {
        var rendered = rows.Select(RenderRow).ToList();
        if (!ordered)
            rendered.Sort(StringComparer.Ordinal);
        return rendered.Count == 0 ? "<no rows>" : string.Join("; ", rendered);
    }
}
