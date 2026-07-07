using System.Collections.Generic;

namespace JauntyQ.Generator.Directives;

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
    /// True if any directives were specified.
    /// </summary>
    public bool HasDirectives =>
        ResultTypeName != null || ResultIsVoid || InlineColumns != null
        || ExplicitParams != null || IsProc || IsFirst || ReturnsIdentity || IsStream
        || CallProcName != null || TypeDirectives != null || EachParams != null;
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
