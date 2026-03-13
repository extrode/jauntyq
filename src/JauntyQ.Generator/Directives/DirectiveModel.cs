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
    /// True when: -- @proc or -- @proc Name
    /// </summary>
    public bool IsProc { get; set; }

    /// <summary>
    /// Custom stored procedure name from: -- @proc CustomName
    /// Null when using default naming convention (Entity_Method).
    /// </summary>
    public string? ProcName { get; set; }

    /// <summary>
    /// True if any directives were specified.
    /// </summary>
    public bool HasDirectives =>
        ResultTypeName != null || ResultIsVoid || InlineColumns != null
        || ExplicitParams != null || IsProc;
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
