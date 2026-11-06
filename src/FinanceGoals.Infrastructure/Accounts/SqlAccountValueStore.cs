using System.Data;
using FinanceGoals.Application.Accounts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Accounts;

/// <summary>Reads and writes app.AccountValue with the API's own connection (the app user).</summary>
internal sealed class SqlAccountValueStore : IAccountValueStore
{
    // Locks the account row first (held to the end of the transaction), so the currency can't change
    // between the check and the write: a currency change takes the same lock and waits, or sees the
    // balance. Returns 1 saved, 0 not in the profile, -1 the account's currency is not the expected one.
    // Replaces the balance of that account and date, or adds it.
    private const string _saveSql = """
        DECLARE @Saved int = 0;
        DECLARE @Currency char(3) = (SELECT CurrencyCode FROM app.Account WITH (UPDLOCK, HOLDLOCK) WHERE Id = @AccountId AND ProfileId = @ProfileId);
        IF @Currency IS NULL
            SET @Saved = 0;
        ELSE IF @Currency <> @CurrencyCode
            SET @Saved = -1;
        ELSE
        BEGIN
            UPDATE app.AccountValue WITH (UPDLOCK, SERIALIZABLE)
            SET Amount = @Amount, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
            WHERE AccountId = @AccountId AND ProfileId = @ProfileId AND ValueDate = @Date;
            IF @@ROWCOUNT > 0
                SET @Saved = 1;
            ELSE
            BEGIN
                INSERT INTO app.AccountValue (AccountId, ValueDate, ProfileId, Amount, CreatedBy, UpdatedBy)
                VALUES (@AccountId, @Date, @ProfileId, @Amount, @UserId, @UserId);
                SET @Saved = 1;
            END;
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
        int profileId, DateOnly day, IReadOnlyList<(int AccountId, decimal Amount, string CurrencyCode)> values, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        // Accounts are locked one after the other, always in id order, so two snapshots can't deadlock.
        foreach (var (accountId, amount, currencyCode) in values.OrderBy(v => v.AccountId))
        {
            await using var command = new SqlCommand(_saveSql, connection, transaction);
            command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
            command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId });
            command.Parameters.Add(new SqlParameter("@Date", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });
            command.Parameters.Add(new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = amount });
            command.Parameters.Add(new SqlParameter("@CurrencyCode", SqlDbType.Char, 3) { Value = currencyCode });
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
            var saved = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (saved != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return saved == 0 ? ValueOutcome.UnknownAccount : ValueOutcome.CurrencyChanged;
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
