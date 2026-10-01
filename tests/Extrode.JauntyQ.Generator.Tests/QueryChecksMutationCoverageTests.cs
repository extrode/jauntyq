using System.Collections.Generic;
using System.Linq;
using Extrode.JauntyQ.Generator.Directives;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class QueryChecksMutationCoverageTests
{
    private static QueryModel WithParameters(params string[] names)
    {
        var model = new QueryModel();
        foreach (var name in names)
            model.Parameters.Add(new ParameterRef { Name = name });
        return model;
    }

    private static string? ParameterMessage(params string[] names) =>
        JauntyQGenerator.ValidateParameterNames(WithParameters(names), "Widgets", "Find")?.ToDiagnostic().GetMessage();

    [Fact]
    public void ValidateParameterNames_AcceptsDistinctValidNames()
        => Assert.Null(ParameterMessage("id", "_name", "x1"));

    [Fact]
    public void ValidateParameterNames_InvalidIdentifier()
        => Assert.Equal(
            "Parameter '@1x' in Widgets.Find is not a valid C# identifier. Rename it to letters, digits, and underscores, not starting with a digit.",
            ParameterMessage("id", "1x"));

    [Fact]
    public void ValidateParameterNames_ReservedDoubleUnderscorePrefix()
        => Assert.Equal(
            "Parameter '@__conn' in Widgets.Find uses the reserved '__' prefix. "
            + "JauntyQ emits its own bookkeeping identifiers (__conn, __cmd, __reader, __weOpened and others) in that namespace, "
            + "so a '__'-prefixed parameter can silently collide with generated code. Rename it without the leading double underscore.",
            ParameterMessage("__conn"));

    [Fact]
    public void ValidateParameterNames_RepeatedSpelling()
        => Assert.Equal(
            "Parameters '@id' and '@id' in Widgets.Find both emit the C# parameter 'id'. Rename one of them.",
            ParameterMessage("id", "name", "id"));

    private static List<string> ExpressionMessages(string sql, string? directiveLines = null)
    {
        var model = SqlParser.SqlParser.Parse(SqlTokenizer.Tokenize(sql), "Q");
        var schema = new DatabaseSchema { Dialect = "postgres" };
        var table = new TableSchema { Name = "t" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int" };
        schema.Tables["t"] = table;
        var directives = directiveLines == null ? null : DirectiveParser.Parse(directiveLines + "\nselect 1").directives;
        var projection = ProjectionBuilder.Build(model, schema, directives);
        return JauntyQGenerator.ValidateExpressionTypes(model.Columns, projection, directives)
            .Select(d => d.ToDiagnostic().GetMessage()).ToList();
    }

    [Fact]
    public void ValidateExpressionTypes_UnresolvedExpression()
        => Assert.Equal(
            "Expression 'v' has no inferable type. Declare it with: -- @type v <dbtype>.",
            Assert.Single(ExpressionMessages("select mystery(id) as v from t")));

    private const string PlainColumnMessage =
        "-- @type names alias '{0}', which this SELECT projects as a plain column, not an expression. "
        + "-- @type only types expression items in this statement's own projection list, so it cannot reach "
        + "an expression computed inside a CTE (or any other subquery) and re-projected here. Write the "
        + "expression in this SELECT's projection list, where the directive can see it.";

    private const string MisspeltMessage =
        "-- @type names alias '{0}', but no expression projection item uses 'AS {0}'. Check the alias spelling.";

    [Theory]
    [InlineData("select id from t", "id", PlainColumnMessage)]
    [InlineData("select id as total from t", "total", PlainColumnMessage)]
    [InlineData("select id as total from t", "id", MisspeltMessage)]
    [InlineData("select * from t", "*", MisspeltMessage)]
    [InlineData("select count(*) as n from t", "m", MisspeltMessage)]
    public void ValidateExpressionTypes_UnmatchedTypeDirective(string sql, string alias, string format)
        => Assert.Equal(
            string.Format(format, alias),
            Assert.Single(ExpressionMessages(sql, $"-- @type {alias} int")));

    [Fact]
    public void ValidateExpressionTypes_MatchedTypeDirectiveIsSilent()
        => Assert.Empty(ExpressionMessages("select mystery(id) as v from t", "-- @type v int"));

    [Fact]
    public void TryResolveProcedure_MatchesExactThenCaseInsensitively()
    {
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        var proc = new ProcedureSchema { Name = "GetWidgets" };
        schema.Procedures["GetWidgets"] = proc;

        Assert.True(JauntyQGenerator.TryResolveProcedure(schema, "GetWidgets", out var exact));
        Assert.Same(proc, exact);
        Assert.True(JauntyQGenerator.TryResolveProcedure(schema, "getwidgets", out var folded));
        Assert.Same(proc, folded);
        Assert.False(JauntyQGenerator.TryResolveProcedure(schema, "GetGadgets", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void ComputeFingerprint_FoldsCaseAndSpacing()
        => Assert.Equal(
            "SELECT id , name FROM t WHERE x = @p AND y = 'Lit' ;",
            JauntyQGenerator.ComputeFingerprint(SqlTokenizer.Tokenize("Select  Id,Name\n from T where X = @P and Y = 'Lit';")));
}
