using JauntyQ.Analysis.Impact;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// AUD-R88-04. <see cref="Classification"/> is compared with <c>&gt;</c> and
/// <c>&gt;=</c> at <c>MigrationImpactReport.cs:37</c> and
/// <c>ReportRenderer.cs:70</c>, the latter deciding the CI exit code — so its
/// members carry an ordering, not just identity. The values are explicit and
/// both behaviors are tested, which makes a declaration <em>reorder</em>
/// harmless. What is unprotected is an <em>inserted</em> member: C# permits
///
/// <code>Safe = 0, Risky = 1, Urgent = 1, Breaking = 2</code>
///
/// without a word of complaint, and a duplicate or out-of-order value silently
/// re-sorts severity underneath every <c>--fail-on</c> comparison in the
/// product. Nothing asserted this before.
/// </summary>
[Trait("Category", "AuditRegression")]
public class ClassificationOrderingTests
{
    [Fact]
    public void SeverityIsOrderedAndDense()
    {
        Assert.Equal(0, (int)Classification.Safe);
        Assert.Equal(1, (int)Classification.Risky);
        Assert.Equal(2, (int)Classification.Breaking);
    }

    /// <summary>
    /// The cardinality half. Without it, a fourth member appended at 3 (or, worse,
    /// slotted at 1) leaves the three assertions above green while changing what
    /// <c>--fail-on risky</c> catches. A new member is a deliberate act and should
    /// arrive with a decision about where in the order it sits; failing here is
    /// how that decision gets made rather than defaulted.
    /// </summary>
    [Fact]
    public void ExactlyThreeMembers_WithNoDuplicateValues()
    {
        var values = Enum.GetValues<Classification>();
        Assert.Equal(3, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(values.Length, Enum.GetNames<Classification>().Length);
    }

    /// <summary>
    /// The comparison the exit code actually performs, stated directly. If the
    /// ordering ever inverts, this fails in the same shape as the production
    /// bug rather than as an arithmetic surprise.
    /// </summary>
    [Fact]
    public void BreakingOutranksRisky_WhichOutranksSafe()
    {
        Assert.True(Classification.Breaking > Classification.Risky);
        Assert.True(Classification.Risky > Classification.Safe);
        Assert.True(Classification.Breaking >= Classification.Risky);
        Assert.False(Classification.Safe >= Classification.Risky);
    }
}
