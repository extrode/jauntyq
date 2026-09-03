using System.Data.Common;

namespace Conduit.Differential.Tests;

/// <summary>
/// What one engine did with one stress case: rows, or the fact that it refused.
///
/// The message is carried for the report but takes no part in grouping. Two
/// engines that both refuse a construct belong in the same group -- the
/// wording of two providers' error text is not a claim this suite makes, which
/// is the rule 017 already applies when both sides throw.
/// </summary>
public sealed record StressOutcome(
    IReadOnlyList<IReadOnlyList<object?>>? Rows,
    string? Refusal)
{
    public bool Refused => Refusal is not null;

    public string GroupKey(bool ordered) =>
        Refused ? "<refused>" : ValueShape.RenderResult(Rows!, ordered);

    public string Describe(bool ordered) =>
        Refused ? $"refused: {Refusal}" : GroupKey(ordered);
}

public static class SqlRunner
{
    /// <summary>
    /// Executes the case's SQL for this engine and materializes every row by
    /// ordinal. A provider refusal is an outcome; anything else propagates,
    /// because it is a fault in the harness rather than a fact about the
    /// engine.
    /// </summary>
    public static StressOutcome Run(DbConnection connection, StressCase stressCase, string engine)
    {
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = stressCase.SqlFor(engine);

            using DbDataReader reader = command.ExecuteReader();
            var rows = new List<IReadOnlyList<object?>>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return new StressOutcome(rows, null);
        }
        catch (DbException ex)
        {
            return new StressOutcome(null, FirstLine(ex.Message));
        }
    }

    private static string FirstLine(string message)
    {
        int newline = message.IndexOfAny(['\r', '\n']);
        return (newline < 0 ? message : message[..newline]).Trim();
    }
}
