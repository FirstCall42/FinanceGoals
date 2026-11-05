using System.Data;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Domain.Currencies;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Currencies;

/// <summary>Rates in app.ExchangeRate, read and written with the API's own connection (the app user).</summary>
internal sealed class SqlExchangeRateStore : IExchangeRateStore
{
    // Only currencies the app knows (app.Currency); the ECB list could in theory gain a code first.
    // An unchanged rate is not touched, so a rerun for the same day changes nothing.
    private const string _mergeSql = """
        MERGE app.ExchangeRate WITH (HOLDLOCK) AS target
        USING (SELECT i.CurrencyCode, i.RateDate, i.UnitsPerEur
               FROM #Incoming AS i
               WHERE EXISTS (SELECT 1 FROM app.Currency AS c WHERE c.Code = i.CurrencyCode)) AS source
        ON target.CurrencyCode = source.CurrencyCode AND target.RateDate = source.RateDate
        WHEN MATCHED AND target.UnitsPerEur <> source.UnitsPerEur THEN
            UPDATE SET UnitsPerEur = source.UnitsPerEur
        WHEN NOT MATCHED BY TARGET THEN
            INSERT (CurrencyCode, RateDate, UnitsPerEur) VALUES (source.CurrencyCode, source.RateDate, source.UnitsPerEur);
        """;

    private const string _latestSql = """
        SELECT TOP (1) CurrencyCode, RateDate, UnitsPerEur
        FROM app.ExchangeRate
        WHERE CurrencyCode = @code AND RateDate <= @day
        ORDER BY RateDate DESC;
        """;

    private readonly IConfiguration _configuration;

    public SqlExchangeRateStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<int> SaveAsync(IReadOnlyList<EurRate> rates, CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using (var create = new SqlCommand(
            "CREATE TABLE #Incoming (CurrencyCode char(3) NOT NULL, RateDate date NOT NULL, UnitsPerEur decimal(19, 6) NOT NULL);",
            connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        using var table = new DataTable();
        table.Columns.Add("CurrencyCode", typeof(string));
        table.Columns.Add("RateDate", typeof(DateTime));
        table.Columns.Add("UnitsPerEur", typeof(decimal));
        foreach (var rate in rates)
        {
            table.Rows.Add(rate.CurrencyCode, rate.Date.ToDateTime(TimeOnly.MinValue), rate.UnitsPerEur);
        }

        using (var bulk = new SqlBulkCopy(connection) { DestinationTableName = "#Incoming" })
        {
            await bulk.WriteToServerAsync(table, cancellationToken);
        }

        await using var merge = new SqlCommand(_mergeSql, connection);
        return await merge.ExecuteNonQueryAsync(cancellationToken);
    }

    public EurRate? LatestOnOrBefore(string currencyCode, DateOnly day)
    {
        using var connection = new SqlConnection(ConnectionString());
        connection.Open();
        using var command = new SqlCommand(_latestSql, connection);
        command.Parameters.Add(new SqlParameter("@code", SqlDbType.Char, 3) { Value = currencyCode });
        command.Parameters.Add(new SqlParameter("@day", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new EurRate(reader.GetString(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2))
            : null;
    }

    private string ConnectionString() =>
        _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");
}
