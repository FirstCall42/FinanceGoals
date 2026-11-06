using System.Data;
using FinanceGoals.Application.Accounts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Accounts;

/// <summary>Reads and writes app.Account with the API's own connection (the app user).</summary>
internal sealed class SqlAccountStore : IAccountStore
{
    private const int _uniqueConstraintViolation = 2627;
    private const int _uniqueIndexViolation = 2601;

    private const string _columns = "Id, Name, Type, CurrencyCode, CASE WHEN ArchivedAt IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END";

    private readonly IConfiguration _configuration;

    public SqlAccountStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<Account>> ListAsync(int profileId, bool includeArchived, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"SELECT {_columns} FROM app.Account WHERE ProfileId = @ProfileId AND (@IncludeArchived = 1 OR ArchivedAt IS NULL) ORDER BY Name;",
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@IncludeArchived", SqlDbType.Bit) { Value = includeArchived });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<Account>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(Read(reader));
        }

        return accounts;
    }

    public async Task<(AccountOutcome Outcome, Account? Account)> CreateAsync(
        int profileId, string name, AccountType type, string currencyCode, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            INSERT INTO app.Account (ProfileId, Name, Type, CurrencyCode, CreatedBy, UpdatedBy)
            OUTPUT inserted.Id, inserted.Name, inserted.Type, inserted.CurrencyCode, CAST(0 AS bit)
            VALUES (@ProfileId, @Name, @Type, @CurrencyCode, @UserId, @UserId);
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 100) { Value = name });
        command.Parameters.Add(new SqlParameter("@Type", SqlDbType.VarChar, 20) { Value = type.ToString() });
        command.Parameters.Add(new SqlParameter("@CurrencyCode", SqlDbType.Char, 3) { Value = currencyCode });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        return await ReadOneAsync(command, cancellationToken);
    }

    public async Task<(AccountOutcome Outcome, Account? Account)> UpdateAsync(
        int profileId, int accountId, AccountChanges changes, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            UPDATE app.Account
            SET Name = COALESCE(@Name, Name),
                Type = COALESCE(@Type, Type),
                CurrencyCode = COALESCE(@CurrencyCode, CurrencyCode),
                ArchivedAt = CASE WHEN @Archived = 1 THEN COALESCE(ArchivedAt, SYSUTCDATETIME())
                                  WHEN @Archived = 0 THEN NULL
                                  ELSE ArchivedAt END,
                UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
            OUTPUT inserted.Id, inserted.Name, inserted.Type, inserted.CurrencyCode,
                   CASE WHEN inserted.ArchivedAt IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            WHERE Id = @AccountId AND ProfileId = @ProfileId;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 100) { Value = (object?)changes.Name ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Type", SqlDbType.VarChar, 20) { Value = (object?)changes.Type?.ToString() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@CurrencyCode", SqlDbType.Char, 3) { Value = (object?)changes.CurrencyCode ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Archived", SqlDbType.Bit) { Value = (object?)changes.Archived ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        return await ReadOneAsync(command, cancellationToken);
    }

    private static async Task<(AccountOutcome, Account?)> ReadOneAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? (AccountOutcome.Saved, Read(reader)) : (AccountOutcome.NotFound, null);
        }
        catch (SqlException exception) when (exception.Number is _uniqueConstraintViolation or _uniqueIndexViolation)
        {
            return (AccountOutcome.NameTaken, null);
        }
    }

    private static Account Read(SqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), Enum.Parse<AccountType>(reader.GetString(2)), reader.GetString(3), reader.GetBoolean(4));

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
