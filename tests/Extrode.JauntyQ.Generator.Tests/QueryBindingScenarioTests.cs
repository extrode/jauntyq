using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class QueryBindingScenarioTests
{
    internal static readonly (string Path, string Text)[] EnumEachQueries =
    {
        ("db/tables/Customers/ByStatuses.sql", "-- @each Statuses\nselect id from customers where status in (@Statuses)"),
        ("db/tables/Customers/ByPriors.sql", "-- @each Priors\nselect id from customers where prior_status in (@Priors)"),
    };

    private static string Generate(string dialect, params (string Path, string Text)[] extra)
        => GeneratedOutputApprovalTests.Generate(
            Path.Combine(GeneratedOutputApprovalTests.ApprovedDir(), dialect), null, extra).Replace("\r\n", "\n");

    [Fact]
    public void Postgres_EnumEach_BindsOneUnknownTypedParameterPerElement()
    {
        string output = Generate("postgres", EnumEachQueries);

        Assert.Contains(
            "                for (int __ib_Statuses = 0; __ib_Statuses < Statuses.Count; __ib_Statuses++)\n" +
            "                {\n" +
            "                    var __p0 = new NpgsqlParameter { ParameterName = \"@Statuses\" + __ib_Statuses };\n" +
            "                    __p0.NpgsqlDbType = NpgsqlDbType.Unknown;\n" +
            "                    __p0.Value = CustomerStatusValues.ToWire(Statuses[__ib_Statuses]);\n" +
            "                    __cmd.Parameters.Add(__p0);\n" +
            "                }\n\n", output);
        Assert.Contains(
            "                    __p0.Value = Priors[__ib_Priors] is null ? (object)DBNull.Value : CustomerStatusValues.ToWire(Priors[__ib_Priors].Value);\n" +
            "                    __cmd.Parameters.Add(__p0);\n" +
            "                }\n\n", output);
    }

    [Fact]
    public void MySql_EnumEach_BindsOneStringParameterPerElement()
    {
        string output = Generate("mysql", EnumEachQueries);

        Assert.Contains(
            "                for (int __ib_Statuses = 0; __ib_Statuses < Statuses.Count; __ib_Statuses++)\n" +
            "                {\n" +
            "                    DbParameter __p0 = __cmd.CreateParameter();\n" +
            "                    __p0.ParameterName = \"@Statuses\" + __ib_Statuses;\n" +
            "                    __p0.DbType = DbType.String;\n" +
            "                    __p0.Value = CustomersStatusValues.ToWire(Statuses[__ib_Statuses]);\n" +
            "                    __cmd.Parameters.Add(__p0);\n" +
            "                }\n", output);
        Assert.Contains(
            "                    __p0.Value = Priors[__ib_Priors] is null ? (object)DBNull.Value : CustomersPriorStatusValues.ToWire(Priors[__ib_Priors].Value);\n",
            output);
    }

    [Fact]
    public void ParametersNamedConnAndCancellationToken_RenameTheBookkeepingParameters()
    {
        string output = Generate("sqlserver",
            ("db/tables/Customers/Clash.sql", "select id from customers where id = @conn and code = @cancellationToken"),
            ("db/tables/Customers/PurgeClash.sql", "delete from customers where id = @conn and code = @cancellationToken"));

        Assert.Contains("public List<Result.Clash> Clash(int conn, string cancellationToken)", output);
        Assert.Contains("public static List<Result.Clash> Clash(DbConnection __conn, int conn, string cancellationToken, DbTransaction? transaction = null)", output);
        Assert.Contains("public async Task<List<Result.Clash>> ClashAsync(int conn, string cancellationToken, CancellationToken __cancellationToken = default)", output);
        Assert.Contains(" PurgeClash(DbConnection __conn, int conn, string cancellationToken, DbTransaction? transaction = null)", output);
        Assert.Contains(" PurgeClashAsync(int conn, string cancellationToken, CancellationToken __cancellationToken = default)", output);
    }

    [Fact]
    public void CrudWhoseEveryParameterIsNullable_DefaultsThemAll()
    {
        string output = Generate("sqlserver", ("db/tables/Customers/PurgeByEmail.sql", "delete from customers where email = @Email"));

        Assert.Contains(" PurgeByEmail(string? Email = default)", output);
        Assert.Contains(" PurgeByEmail(DbConnection conn, string? Email = default, DbTransaction? transaction = null)", output);
    }

    [Fact]
    public void FirstRowEachQuery_ReturnsNullForAnEmptyList()
    {
        string output = Generate("sqlite", ("db/tables/Customers/FirstOfIds.sql", "-- @each Ids\n-- @first\nselect id, name from customers where id in (@Ids)"));

        Assert.Contains("            if (Ids.Count == 0)\n                return null;\n", output);
    }
}
