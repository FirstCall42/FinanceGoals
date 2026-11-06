using System.Data;
using FinanceGoals.Application.Accounts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Accounts;

/// <summary>Reads and writes app.AccountValue with the API's own connection (the app user).</summary>
internal sealed class SqlAccountValueStore : IAccountValueStore
{
    // Replaces the balance of that account and date, or adds it. The insert checks the account is in
    // the profile, so a balance for another household's account saves nothing.
    private const string _saveSql = """
        DECLARE @Saved int = 0;
        UPDATE app.AccountValue WITH (UPDLOCK, SERIALIZABLE)
        SET Amount = @Amount, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
        WHERE AccountId = @AccountId AND ProfileId = @ProfileId AND ValueDate = @Date;
        IF @@ROWCOUNT > 0
            SET @Saved = 1;
        ELSE
        BEGIN
            INSERT INTO app.AccountValue (AccountId, ValueDate, ProfileId, Amount, CreatedBy, UpdatedBy)
            SELECT a.Id, @Date, a.ProfileId, @Amount, @UserId, @UserId
            FROM app.Account a
            WHERE a.Id = @AccountId AND a.ProfileId = @ProfileId;
            SET @Saved = @@ROWCOUNT;
        END;
        SELECT @Saved;
        """;

    private readonly IConfiguration _configuration;

    public SqlAccountValueStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<AccountValue>> ListAsync(int profileId, int? accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT AccountId, ValueDate, Amount FROM app.AccountValue
            WHERE ProfileId = @ProfileId AND (@AccountId IS NULL OR AccountId = @AccountId)
            ORDER BY ValueDate DESC, AccountId;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = (object?)accountId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<AccountValue>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new AccountValue(reader.GetInt32(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2)));
        }

        return values;
    }

    public async Task<ValueOutcome> SaveAsync(
        int profileId, DateOnly day, IReadOnlyList<(int AccountId, decimal Amount)> values, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var (accountId, amount) in values)
        {
            await using var command = new SqlCommand(_saveSql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
            command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId });
            command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = amount });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
            if ((int)(await command.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ValueOutcome.UnknownAccount;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ValueOutcome.Saved;
    }

    public async Task<bool> DeleteAsync(int profileId, int accountId, DateOnly day, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM app.AccountValue WHERE ProfileId = @ProfileId AND AccountId = @AccountId AND ValueDate = @Date;",
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId });
        command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> DeleteSnapshotAsync(int profileId, DateOnly day, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM app.AccountValue WHERE ProfileId = @ProfileId AND ValueDate = @Date;",
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
        return await command.ExecuteNonQueryAsync(cancellationToken);
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
