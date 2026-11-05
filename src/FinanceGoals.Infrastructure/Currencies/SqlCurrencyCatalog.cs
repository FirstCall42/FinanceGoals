using FinanceGoals.Application.Currencies;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Currencies;

/// <summary>Reads app.Currency with the API's own connection (the app user).</summary>
internal sealed class SqlCurrencyCatalog : ICurrencyCatalog
{
    private const string _sql = "SELECT Code, NumericCode, MinorUnits, Name FROM app.Currency ORDER BY Code;";

    private readonly IConfiguration _configuration;

    public SqlCurrencyCatalog(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<CurrencyInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var currencies = new List<CurrencyInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            currencies.Add(new CurrencyInfo(
                reader.GetString(0),
                reader.GetInt16(1),
                reader.GetByte(2),
                reader.GetString(3)));
        }

        return currencies;
    }
}
