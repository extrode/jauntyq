using System.Linq;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

[Trait("Category", "AuditRegression")]
public class ReferencedObjectsParameterScopeTests
{
    [Fact]
    public void AParameterBoundInsideASubquery_IsAttributedToTheSubquerysTable()
    {
        var model = SqlParser.SqlParser.Parse(
            SqlTokenizer.Tokenize("delete from orders where exists (select 1 from customers where email = @Email)"), "q");

        var refs = ReferencedObjects.Resolve(model);

        Assert.Contains(refs.Columns, c => c.Table == "customers" && c.Column == "email");
        Assert.DoesNotContain(refs.Columns, c => c.Table == "orders" && c.Column == "email");
    }
}
