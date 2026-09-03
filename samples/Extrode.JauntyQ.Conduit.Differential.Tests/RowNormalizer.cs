using System.Globalization;

namespace Conduit.Differential.Tests;

/// <summary>
/// Reduces a provider-returned value to a form comparable across engines.
///
/// Every rule here is a compatibility claim: it asserts that the difference it
/// erases carries no meaning. Rules that would erase a *meaningful* difference
/// -- a different number, a different row count, a null where another engine
/// returned a value -- are deliberately absent, and NormalizerTests pins that
/// by asserting pairs the normalizer must NOT merge.
/// </summary>
public static class RowNormalizer
{
    public static object? Normalize(object? value)
    {
        if (value is null || value is DBNull)
            return null;

        switch (value)
        {
            // Width and signedness are a provider choice, not a result. MySQL
            // hands back ulong for a COUNT(*) that Npgsql returns as long and
            // Microsoft.Data.Sqlite as long.
            case bool b:
                return b ? 1L : 0L;
            case sbyte or byte or short or ushort or int or uint or long:
                return System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case ulong ul:
                return ul <= long.MaxValue ? (long)ul : (object)ul.ToString(CultureInfo.InvariantCulture);

            // Trailing zeros are a scale artifact of the declared column type:
            // 1.10m from a decimal(10,2) and 1.1m from a decimal(10,1) are the
            // same number. 1.10m vs 1.2m stays a difference.
            case decimal d:
                return NormalizeDecimal(d);
            case float f:
                return NormalizeDecimal((decimal)f);
            case double dbl:
                return NormalizeDecimal((decimal)dbl);

            // Kind differs by provider (Npgsql Utc, SqlClient Unspecified) for
            // the same instant, and sub-second precision differs by column
            // type across engines. Seconds is the resolution the Conduit
            // schema stores.
            case DateTime dt:
                return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second)
                    .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return Normalize(dto.UtcDateTime);

            case byte[] bytes:
                return System.Convert.ToBase64String(bytes);

            // CHAR(n) pads to width on SQL Server and MySQL; VARCHAR does not.
            // Only trailing spaces are trimmed -- leading spaces are data.
            case string s:
                return s.TrimEnd(' ');

            case Guid g:
                return g.ToString("D");

            default:
                return value.ToString();
        }
    }

    private static object NormalizeDecimal(decimal d)
    {
        decimal trimmed = d / 1.000000000000000000000000000000000m;
        return trimmed.ToString(CultureInfo.InvariantCulture);
    }

    public static IReadOnlyDictionary<string, object?> NormalizeRow(IReadOnlyDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in row)
            result[kvp.Key] = Normalize(kvp.Value);
        return result;
    }

    public static string RenderRow(IReadOnlyDictionary<string, object?> row) =>
        "{" + string.Join(", ", row.OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => $"{k.Key}={Render(k.Value)}")) + "}";

    private static string Render(object? value) => value switch
    {
        null => "null",
        string s => $"'{s}'",
        _ => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
