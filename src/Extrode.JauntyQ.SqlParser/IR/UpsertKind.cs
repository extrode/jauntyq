namespace Extrode.JauntyQ.SqlParser.IR;

/// <summary>The update branch an INSERT carries, if any.</summary>
public enum UpsertKind
{
    None,

    /// <summary><c>ON CONFLICT ... DO UPDATE</c> (PostgreSQL, SQLite).</summary>
    OnConflictUpdate,

    /// <summary><c>ON DUPLICATE KEY UPDATE</c> (MySQL), which takes no WHERE.</summary>
    OnDuplicateKeyUpdate
}
