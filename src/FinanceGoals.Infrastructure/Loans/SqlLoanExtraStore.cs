using System.Data;
using FinanceGoals.Application.Loans;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Loans;

/// <summary>
/// Reads and writes a loan's extra payments and balances. Every write is one statement that selects
/// from app.Loan with the profile, so a loan of another household is never touched.
/// </summary>
internal sealed class SqlLoanExtraStore : ILoanExtraStore
{
    private const int _uniqueConstraintViolation = 2627;
    private const int _uniqueIndexViolation = 2601;

    private readonly IConfiguration _configuration;

    public SqlLoanExtraStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<LoanExtras?> GetAsync(int profileId, int loanId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*) FROM app.Loan WHERE Id = @LoanId AND ProfileId = @ProfileId;
            SELECT ExtraDate, Amount FROM app.LoanExtraPayment WHERE LoanId = @LoanId AND ProfileId = @ProfileId ORDER BY ExtraDate;
            SELECT Id, Amount, FromDate, UntilDate FROM app.LoanMonthlyExtra WHERE LoanId = @LoanId AND ProfileId = @ProfileId ORDER BY FromDate, Id;
            SELECT BalanceDate, Amount FROM app.LoanBalance WHERE LoanId = @LoanId AND ProfileId = @ProfileId ORDER BY BalanceDate;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt32(0) == 0)
        {
            return null;
        }

        await reader.NextResultAsync(cancellationToken);
        var oneOff = new List<StoredExtra>();
        while (await reader.ReadAsync(cancellationToken))
        {
            oneOff.Add(new StoredExtra(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetDecimal(1)));
        }

        await reader.NextResultAsync(cancellationToken);
        var monthly = new List<StoredMonthlyExtra>();
        while (await reader.ReadAsync(cancellationToken))
        {
            monthly.Add(new StoredMonthlyExtra(
                reader.GetInt32(0), reader.GetDecimal(1), DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3))));
        }

        await reader.NextResultAsync(cancellationToken);
        var balances = new List<StoredBalance>();
        while (await reader.ReadAsync(cancellationToken))
        {
            balances.Add(new StoredBalance(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetDecimal(1)));
        }

        return new LoanExtras(oneOff, monthly, balances);
    }

    public async Task<LoanExtraOutcome> AddOneOffAsync(int profileId, int loanId, DateOnly date, decimal amount, int byUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(
                """
                INSERT INTO app.LoanExtraPayment (LoanId, ExtraDate, ProfileId, Amount, CreatedBy, UpdatedBy)
                SELECT Id, @Date, ProfileId, @Amount, @UserId, @UserId FROM app.Loan WHERE Id = @LoanId AND ProfileId = @ProfileId;
                """,
                profileId, loanId, date, amount, byUserId, cancellationToken);
        }
        catch (SqlException e) when (e.Number is _uniqueConstraintViolation or _uniqueIndexViolation)
        {
            return LoanExtraOutcome.Duplicate;
        }
    }

    public Task<LoanExtraOutcome> DeleteOneOffAsync(int profileId, int loanId, DateOnly date, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM app.LoanExtraPayment WHERE LoanId = @LoanId AND ProfileId = @ProfileId AND ExtraDate = @Date;",
            profileId, loanId, date, amount: null, byUserId: null, cancellationToken);

    public async Task<(LoanExtraOutcome Outcome, int Id)> AddMonthlyAsync(
        int profileId, int loanId, decimal amount, DateOnly from, DateOnly? until, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            INSERT INTO app.LoanMonthlyExtra (LoanId, ProfileId, Amount, FromDate, UntilDate, CreatedBy, UpdatedBy)
            OUTPUT inserted.Id
            SELECT Id, ProfileId, @Amount, @From, @Until, @UserId, @UserId FROM app.Loan WHERE Id = @LoanId AND ProfileId = @ProfileId;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(Amount("@Amount", amount));
        command.Parameters.Add(Date("@From", from));
        command.Parameters.Add(new SqlParameter("@Until", SqlDbType.Date) { Value = until is { } u ? u.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is int created ? (LoanExtraOutcome.Saved, created) : (LoanExtraOutcome.NotFound, 0);
    }

    public async Task<LoanExtraOutcome> DeleteMonthlyAsync(int profileId, int loanId, int id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM app.LoanMonthlyExtra WHERE Id = @Id AND LoanId = @LoanId AND ProfileId = @ProfileId;",
            connection);
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? LoanExtraOutcome.NotFound : LoanExtraOutcome.Saved;
    }

    public async Task<LoanExtraOutcome> SaveBalanceAsync(int profileId, int loanId, DateOnly date, decimal amount, int byUserId, CancellationToken cancellationToken)
    {
        // Update-then-insert under a key-range lock, so two saves of one day can't both insert.
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @Changed int;
            UPDATE app.LoanBalance WITH (UPDLOCK, SERIALIZABLE)
               SET Amount = @Amount, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
             WHERE LoanId = @LoanId AND ProfileId = @ProfileId AND BalanceDate = @Date;
            SET @Changed = @@ROWCOUNT;
            IF @Changed = 0
            BEGIN
                INSERT INTO app.LoanBalance (LoanId, BalanceDate, ProfileId, Amount, CreatedBy, UpdatedBy)
                SELECT Id, @Date, ProfileId, @Amount, @UserId, @UserId FROM app.Loan WHERE Id = @LoanId AND ProfileId = @ProfileId;
                SET @Changed = @@ROWCOUNT;
            END;
            COMMIT TRANSACTION;
            SELECT @Changed;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(Date("@Date", date));
        command.Parameters.Add(Amount("@Amount", amount));
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 0 ? LoanExtraOutcome.NotFound : LoanExtraOutcome.Saved;
    }

    public Task<LoanExtraOutcome> DeleteBalanceAsync(int profileId, int loanId, DateOnly date, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "DELETE FROM app.LoanBalance WHERE LoanId = @LoanId AND ProfileId = @ProfileId AND BalanceDate = @Date;",
            profileId, loanId, date, amount: null, byUserId: null, cancellationToken);

    private async Task<LoanExtraOutcome> ExecuteAsync(
        string sql, int profileId, int loanId, DateOnly date, decimal? amount, int? byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(Date("@Date", date));
        if (amount is { } value)
        {
            command.Parameters.Add(Amount("@Amount", value));
        }

        if (byUserId is { } user)
        {
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = user });
        }

        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? LoanExtraOutcome.NotFound : LoanExtraOutcome.Saved;
    }

    private static SqlParameter Amount(string name, decimal value) =>
        new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = value };

    private static SqlParameter Date(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };

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
