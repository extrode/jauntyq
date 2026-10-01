using Extrode.JauntyQ.Generator.Directives;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class DirectiveModelTests
{
    public static TheoryData<string, Action<DirectiveModel>> OneDirectiveEach => new()
    {
        { "result type", d => d.ResultTypeName = "T" },
        { "result void", d => d.ResultIsVoid = true },
        { "inline columns", d => d.InlineColumns = new List<InlineColumn>() },
        { "params", d => d.ExplicitParams = new List<ExplicitParam>() },
        { "proc", d => d.IsProc = true },
        { "first", d => d.IsFirst = true },
        { "identity", d => d.ReturnsIdentity = true },
        { "stream", d => d.IsStream = true },
        { "call", d => d.CallProcName = "p" },
        { "type", d => d.TypeDirectives = new List<TypeDirective>() },
        { "each", d => d.EachParams = new List<string>() },
        { "mirrors", d => d.MirrorsTarget = "Q" },
        { "allow-unindexed", d => d.AllowUnindexedReason = "r" },
        { "allow-sort", d => d.AllowSortReason = "r" },
        { "allow-n-plus-one", d => d.AllowNPlusOneReason = "r" },
    };

    [Theory]
    [MemberData(nameof(OneDirectiveEach))]
    public void HasDirectives_TrueWhenOnlyOneDirectiveIsSet(string directive, Action<DirectiveModel> set)
    {
        var model = new DirectiveModel();
        set(model);

        Assert.True(model.HasDirectives, directive);
    }

    [Fact]
    public void HasDirectives_FalseForAnEmptyModel()
    {
        Assert.False(new DirectiveModel().HasDirectives);
    }
}
