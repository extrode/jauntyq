namespace Extrode.JauntyQ.Analysis.Scoping;

/// <summary>
/// One enforced scope (spec 021): <see cref="Table"/> is filtered by
/// <see cref="Column"/>. Both are spelled as the schema spells them, so
/// generated SQL and diagnostics use the real names whatever case the sidecar
/// was written in.
/// </summary>
public sealed class ScopeColumn
{
    public string Table { get; }
    public string Column { get; }

    public ScopeColumn(string table, string column)
    {
        Table = table;
        Column = column;
    }
}
