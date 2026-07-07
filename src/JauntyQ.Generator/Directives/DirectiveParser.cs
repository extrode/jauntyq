using System;
using System.Collections.Generic;
using System.Text;

namespace JauntyQ.Generator.Directives;

public static class DirectiveParser
{
    /// <summary>
    /// Parses directive comments from raw SQL text and returns the directive model
    /// plus the cleaned SQL (with directive lines removed).
    /// Directives are lines matching: -- @directive value
    /// </summary>
    public static (DirectiveModel directives, string cleanedSql) Parse(string rawSql)
    {
        var directives = new DirectiveModel();
        var cleanedLines = new List<string>();

        var lines = rawSql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("--"))
            {
                var commentBody = trimmed.Substring(2).TrimStart();

                if (commentBody.StartsWith("@result ", StringComparison.OrdinalIgnoreCase))
                {
                    var value = commentBody.Substring(8).Trim();
                    ParseResultDirective(directives, value);
                    continue; // strip this line from cleaned SQL
                }

                if (commentBody.StartsWith("@params ", StringComparison.OrdinalIgnoreCase))
                {
                    var value = commentBody.Substring(8).Trim();
                    ParseParamsDirective(directives, value);
                    continue; // strip this line from cleaned SQL
                }

                if (commentBody.StartsWith("@type ", StringComparison.OrdinalIgnoreCase))
                {
                    var value = commentBody.Substring(6).Trim();
                    ParseTypeDirective(directives, value);
                    continue; // strip this line from cleaned SQL
                }

                if (commentBody.StartsWith("@each ", StringComparison.OrdinalIgnoreCase))
                {
                    var value = commentBody.Substring(6).Trim();
                    ParseEachDirective(directives, value);
                    continue; // strip this line from cleaned SQL
                }

                if (string.Equals(commentBody, "@first", StringComparison.OrdinalIgnoreCase))
                {
                    directives.IsFirst = true;
                    continue; // strip this line from cleaned SQL
                }

                if (string.Equals(commentBody, "@identity", StringComparison.OrdinalIgnoreCase))
                {
                    directives.ReturnsIdentity = true;
                    continue; // strip this line from cleaned SQL
                }

                if (string.Equals(commentBody, "@stream", StringComparison.OrdinalIgnoreCase))
                {
                    directives.IsStream = true;
                    continue; // strip this line from cleaned SQL
                }

                if (commentBody.StartsWith("@call", StringComparison.OrdinalIgnoreCase))
                {
                    // "@call" is 5 chars — the rest is the procedure name.
                    var rest = commentBody.Substring(5).Trim();
                    if (rest.Length > 0)
                        directives.CallProcName = rest;
                    continue; // strip this line from cleaned SQL
                }

                if (commentBody.StartsWith("@proc", StringComparison.OrdinalIgnoreCase))
                {
                    directives.IsProc = true;
                    // "@proc" is 5 chars — anything after is the optional name
                    var rest = commentBody.Substring(5).Trim();
                    if (rest.Length > 0)
                        directives.ProcName = rest;
                    continue; // strip this line from cleaned SQL
                }
            }

            cleanedLines.Add(line);
        }

        var cleanedSql = string.Join("\n", cleanedLines);
        return (directives, cleanedSql);
    }

    private static void ParseResultDirective(DirectiveModel directives, string value)
    {
        if (string.Equals(value, "void", StringComparison.OrdinalIgnoreCase))
        {
            directives.ResultIsVoid = true;
            return;
        }

        if (value.StartsWith("(") && value.EndsWith(")"))
        {
            // Inline column definitions: (int Id, string Name)
            var inner = value.Substring(1, value.Length - 2).Trim();
            directives.InlineColumns = ParseInlineColumns(inner);
            return;
        }

        // Simple type name
        directives.ResultTypeName = value;
    }

    private static List<InlineColumn> ParseInlineColumns(string inner)
    {
        var columns = new List<InlineColumn>();
        var parts = inner.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var spaceIndex = trimmed.LastIndexOf(' ');
            if (spaceIndex > 0)
            {
                var type = trimmed.Substring(0, spaceIndex).Trim();
                var name = trimmed.Substring(spaceIndex + 1).Trim();
                columns.Add(new InlineColumn(type, name));
            }
        }

        return columns;
    }

    /// <summary>
    /// Parses one "-- @type &lt;alias&gt; &lt;dbtype&gt;" directive: the first
    /// whitespace-delimited token is the expression alias, the remainder is the
    /// db type (which may itself contain spaces, e.g. "double precision").
    /// </summary>
    private static void ParseTypeDirective(DirectiveModel directives, string value)
    {
        int space = value.IndexOf(' ');
        if (space <= 0)
            return;
        var alias = value.Substring(0, space).Trim();
        var dbType = value.Substring(space + 1).Trim();
        if (alias.Length == 0 || dbType.Length == 0)
            return;

        directives.TypeDirectives ??= new List<TypeDirective>();
        directives.TypeDirectives.Add(new TypeDirective(alias, dbType));
    }

    /// <summary>
    /// Parses one "-- @each &lt;ParamName&gt;" directive. Repeatable: each line
    /// names one parameter to expand as an IN-list at runtime.
    /// </summary>
    private static void ParseEachDirective(DirectiveModel directives, string value)
    {
        if (value.Length == 0)
            return;

        directives.EachParams ??= new List<string>();
        directives.EachParams.Add(value);
    }

    private static void ParseParamsDirective(DirectiveModel directives, string value)
    {
        var explicitParams = new List<ExplicitParam>();
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = trimmed.Substring(0, colonIndex).Trim();
                var type = trimmed.Substring(colonIndex + 1).Trim();
                explicitParams.Add(new ExplicitParam(name, type));
            }
        }

        if (explicitParams.Count > 0)
        {
            directives.ExplicitParams = explicitParams;
        }
    }
}
