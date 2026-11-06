using System.Data;
using FinanceGoals.Application.Accounts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Accounts;

/// <summary>Reads and writes app.AccountAssumption with the API's own connection (the app user).</summary>
internal sealed class SqlAccountAssumptionStore : IAccountAssumptionStore
{
    private readonly IConfiguration _configuration;

    public SqlAccountAssumptionStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<AccountAssumption>> ListAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT AccountId, ExpectedYearlyReturn, MonthlyContribution FROM app.AccountAssumption WHERE ProfileId = @ProfileId ORDER BY AccountId;",
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<AccountAssumption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new AccountAssumption(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        }

        return list;
    }

    public async Task<bool> SaveAsync(int profileId, AccountAssumption assumption, int byUserId, CancellationToken cancellationToken)
    {
        // Update, then insert from the account row of this profile, under a key-range lock so two saves can't both insert.
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @Changed int;
            UPDATE app.AccountAssumption WITH (UPDLOCK, SERIALIZABLE)
               SET ExpectedYearlyReturn = @Return, MonthlyContribution = @Contribution, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
             WHERE AccountId = @AccountId AND ProfileId = @ProfileId;
            SET @Changed = @@ROWCOUNT;
            IF @Changed = 0
            BEGIN
                INSERT INTO app.AccountAssumption (AccountId, ProfileId, ExpectedYearlyReturn, MonthlyContribution, CreatedBy, UpdatedBy)
                SELECT Id, ProfileId, @Return, @Contribution, @UserId, @UserId FROM app.Account WHERE Id = @AccountId AND ProfileId = @ProfileId;
                SET @Changed = @@ROWCOUNT;
            END;
            COMMIT TRANSACTION;
            SELECT @Changed;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = assumption.AccountId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@Return", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = (object?)assumption.ExpectedYearlyReturn ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Contribution", SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = (object?)assumption.MonthlyContribution ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! > 0;
    }

    public async Task<bool> DeleteAsync(int profileId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM app.AccountAssumption WHERE AccountId = @AccountId AND ProfileId = @ProfileId;",
            connection);
        command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
