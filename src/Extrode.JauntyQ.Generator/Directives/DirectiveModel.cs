using System.Collections.Generic;

namespace Extrode.JauntyQ.Generator.Directives;

public class DirectiveModel
{
    /// <summary>
    /// Custom result type name from: -- @result TypeName
    /// </summary>
    public string? ResultTypeName { get; set; }

    /// <summary>
    /// True when: -- @result void
    /// </summary>
    public bool ResultIsVoid { get; set; }

    /// <summary>
    /// Inline column definitions from: -- @result (int Id, string Name)
    /// </summary>
    public List<InlineColumn>? InlineColumns { get; set; }

    /// <summary>
    /// Explicit parameter type overrides from: -- @params Id:int, Name:string
    /// </summary>
    public List<ExplicitParam>? ExplicitParams { get; set; }

    /// <summary>
    /// Explicit db-type declarations for expression projection items, from a
    /// repeatable directive: -- @type &lt;alias&gt; &lt;dbtype&gt;
    /// (e.g. -- @type has_passphrase boolean). The dbtype is resolved through
    /// DialectMapper. Null when no @type directive appears.
    /// </summary>
    public List<TypeDirective>? TypeDirectives { get; set; }

    /// <summary>
    /// Parameter names expanded as a SQL IN-list at runtime, from a repeatable
    /// directive: -- @each &lt;ParamName&gt; (e.g. -- @each Ids). The emitted
    /// C# parameter becomes IReadOnlyList&lt;T&gt; (T inferred the same way as
    /// a scalar bound parameter), and every "@ParamName" occurrence in the SQL
    /// text is expanded at runtime into "@ParamName0,@ParamName1,...".
    /// </summary>
    public List<string>? EachParams { get; set; }

    /// <summary>
    /// True when: -- @proc or -- @proc Name
    /// </summary>
    public bool IsProc { get; set; }

    /// <summary>
    /// Custom stored procedure name from: -- @proc CustomName
    /// Null when using default naming convention (Entity_Method).
    /// </summary>
    public string? ProcName { get; set; }

    /// <summary>
    /// True when: -- @first
    /// SELECT returns a single row (Row?) instead of List&lt;Row&gt;.
    /// </summary>
    public bool IsFirst { get; set; }

    /// <summary>
    /// True when: -- @identity
    /// INSERT returns the database-assigned identity value (typed per the
    /// key column) instead of the affected row count.
    /// </summary>
    public bool ReturnsIdentity { get; set; }

    /// <summary>
    /// Procedure name from: -- @call ProcName
    /// Binds this query file to a stored procedure that already exists in the
    /// database (captured in the schema snapshot). The file has no SQL body;
    /// the generator emits a CommandType.StoredProcedure call with typed
    /// parameters and result columns from the snapshot. Null when not a call.
    /// </summary>
    public string? CallProcName { get; set; }

    /// <summary>
    /// True when: -- @stream
    /// SELECT yields rows lazily as IAsyncEnumerable&lt;Row&gt; (async) and
    /// IEnumerable&lt;Row&gt; (sync) directly off the reader, instead of
    /// buffering the whole result set into a List&lt;Row&gt;. For large result
    /// sets this keeps memory constant. Mutually exclusive with @first.
    /// </summary>
    public bool IsStream { get; set; }

    /// <summary>
    /// The query this one declares it filters identically to, from:
    /// -- @mirrors ListPage  (or -- @mirrors Bookmarks.ListPage across entities)
    /// Checked at the aggregate stage, where the whole corpus is visible: the
    /// two WHERE clauses must reduce to the same set of predicate atoms
    /// (JNT8011), and a pairing that cannot be compared at all is reported
    /// rather than dropped (JNT3010). Null when the directive is absent.
    /// </summary>
    public string? MirrorsTarget { get; set; }

    /// <summary>
    /// The stated reason from: -- @allow-unindexed &lt;reason&gt;
    /// Suppresses JNT8004 (unindexed filter column) for THIS query only, and
    /// nothing else — every other diagnostic the query would raise still fires.
    ///
    /// The reason is mandatory (the bare directive is declined and reported as
    /// JNT3008) and it is stored rather than discarded, because the point of
    /// the directive is that the decision lives in the query file and shows up
    /// in the diff. A suppression whose justification is not written down is a
    /// NoWarn entry with extra steps.
    ///
    /// A directive on a query that has no unindexed filter is reported as
    /// unnecessary (JNT8012): an exemption that outlives the condition that
    /// justified it is exactly how an escape hatch becomes the default.
    /// Null when the directive is absent.
    /// </summary>
    public string? AllowUnindexedReason { get; set; }

    /// <summary>
    /// The stated reason from: -- @allow-sort &lt;reason&gt;
    /// Suppresses JNT8007 (unindexed ORDER BY) for THIS query only, on the same
    /// terms as <see cref="AllowUnindexedReason"/>: reason mandatory, nothing
    /// else silenced, and a directive that suppresses nothing reported as
    /// JNT8012.
    ///
    /// A SIBLING of @allow-unindexed rather than a widening of it, because the
    /// JNT8012 remedy is "remove the directive". Under one directive covering
    /// both codes that advice goes wrong the moment a query has both an
    /// unindexed filter and an unindexed sort and only one of them is fixed:
    /// the directive still suppresses the other, so JNT8012 stays silent and
    /// the stale half lives on. Two directives each expire on their own
    /// evidence, and "remove the directive" stays true of both.
    /// Null when the directive is absent.
    /// </summary>
    public string? AllowSortReason { get; set; }

    /// <summary>
    /// The stated reason from: -- @allow-n-plus-one &lt;reason&gt;
    /// Suppresses JNT8008 (cross-query N+1 access pattern) for THIS query only,
    /// on the same terms as its siblings <see cref="AllowUnindexedReason"/> and
    /// <see cref="AllowSortReason"/>: reason mandatory (the bare directive is
    /// declined and reported as JNT3008), nothing else silenced, and a directive
    /// that suppresses nothing reported as JNT8013.
    ///
    /// JNT8013 rather than JNT8012 because JNT8008 is decided at the aggregate
    /// stage, where the whole corpus is visible, so the dead-directive check
    /// lives there too — and a dedicated code keeps each check resting on its
    /// own evidence. Null when the directive is absent.
    /// </summary>
    public string? AllowNPlusOneReason { get; set; }

    /// <summary>
    /// True if any directives were specified.
    /// </summary>
    public bool HasDirectives =>
        ResultTypeName != null || ResultIsVoid || InlineColumns != null
        || ExplicitParams != null || IsProc || IsFirst || ReturnsIdentity || IsStream
        || CallProcName != null || TypeDirectives != null || EachParams != null
        || MirrorsTarget != null || AllowUnindexedReason != null || AllowSortReason != null
        || AllowNPlusOneReason != null;

    /// <summary>
    /// JNT3008 warning messages for directive-lookalike comment lines that
    /// parsed as nothing: a known value-taking directive with no value
    /// (bare <c>-- @each</c>), or a one-edit typo of a known directive
    /// (<c>-- @frist</c>). The lines stay plain comments in the cleaned SQL;
    /// these warnings tell the author their intent was dropped. Ordinary
    /// <c>@word</c> comments (<c>-- @author</c>) never register here.
    /// </summary>
    public List<string>? SuspiciousDirectives { get; set; }

    /// <summary>
    /// JNT3011 warning messages for a non-repeatable directive written more
    /// than once in one file. Every such directive stores into a single field,
    /// so the second line overwrites the first with no record that the first
    /// existed — the author's stated result type, procedure name or suppression
    /// reason simply stops being the one in force. Only <c>-- @type</c> and
    /// <c>-- @each</c> are repeatable by design and never register here.
    ///
    /// Last-wins is left in place rather than switched to first-wins: the
    /// defect was the silence, and changing which line takes effect would move
    /// existing builds' generated code on the strength of a warning they have
    /// not read yet.
    /// </summary>
    public List<string>? DuplicateDirectives { get; set; }
}

public class TypeDirective
{
    public string Alias { get; }
    public string DbType { get; }

    public TypeDirective(string alias, string dbType)
    {
        Alias = alias;
        DbType = dbType;
    }
}

public class InlineColumn
{
    public string Type { get; }
    public string Name { get; }

    public InlineColumn(string type, string name)
    {
        Type = type;
        Name = name;
    }
}

public class ExplicitParam
{
    public string Name { get; }
    public string CSharpType { get; }

    public ExplicitParam(string name, string csharpType)
    {
        Name = name;
        CSharpType = csharpType;
    }
}
