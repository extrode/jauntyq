using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Northwind.Tests;

[Collection("Northwind")]
public class EmployeesTests
{
    private readonly NorthwindFixture _fixture;
    public EmployeesTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns9Employees()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Employees.GetAll();
        Assert.Equal(9, results.Count);
    }

    [Fact]
    public void GetById_ReturnsNancyDavolio()
    {
        if (!_fixture.Available) return;
        var employee = _fixture.Db.Employees.GetById(1);
        Assert.NotNull(employee);
        Assert.Equal("Davolio", employee.LastName);
        Assert.Equal("Nancy", employee.FirstName);
    }

    [Fact]
    public void GetWithManager_ReturnsAllWithManagerInfo()
    {
        if (!_fixture.Available) return;
        var results = _fixture.Db.Employees.GetWithManager();
        Assert.Equal(9, results.Count);
        // Andrew Fuller (EmployeeId=2) is VP and reports to nobody
        var andrew = results.First(e => e.EmployeeId == 2);
        Assert.Equal("Fuller", andrew.LastName);
    }
}
