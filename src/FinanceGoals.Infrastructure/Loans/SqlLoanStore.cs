using System.Data;
using System.Globalization;
using FinanceGoals.Application.Loans;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Loans;

/// <summary>Reads and writes the loan tables with the API's own connection (the app user).</summary>
internal sealed class SqlLoanStore : ILoanStore
{
    private const int _uniqueConstraintViolation = 2627;
    private const int _uniqueIndexViolation = 2601;

    private readonly IConfiguration _configuration;

    public SqlLoanStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<StoredLoan>> ListAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadAsync(connection, loanId: null, profileId, cancellationToken);
    }

    public async Task<StoredLoan?> GetAsync(int profileId, int loanId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await ReadAsync(connection, loanId, profileId, cancellationToken)).SingleOrDefault();
    }

    public async Task<(LoanOutcome Outcome, StoredLoan? Loan)> CreateAsync(int profileId, Loan loan, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            int id;
            await using (var insert = new SqlCommand(
                """
                INSERT INTO app.Loan (ProfileId, Name, CurrencyCode, Principal, PaidOut, FirstPaymentDate, TermMonths, FeePerPayment, DayCount, CreatedBy, UpdatedBy)
                OUTPUT inserted.Id
                VALUES (@ProfileId, @Name, @Currency, @Principal, @PaidOut, @FirstPaymentDate, @TermMonths, @Fee, @DayCount, @UserId, @UserId);
                """,
                connection, transaction))
            {
                insert.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
                insert.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 100) { Value = loan.Name });
                insert.Parameters.Add(new SqlParameter("@Currency", SqlDbType.Char, 3) { Value = loan.Principal.Currency.Code });
                insert.Parameters.Add(Amount("@Principal", loan.Principal.Amount));
                insert.Parameters.Add(Date("@PaidOut", loan.Start));
                insert.Parameters.Add(Date("@FirstPaymentDate", loan.FirstPaymentDate));
                insert.Parameters.Add(new SqlParameter("@TermMonths", SqlDbType.Int) { Value = loan.TermMonths });
                insert.Parameters.Add(Amount("@Fee", loan.FeePerPayment.Amount));
                insert.Parameters.Add(new SqlParameter("@DayCount", SqlDbType.VarChar, 12) { Value = loan.DayCount.ToString() });
                insert.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
                id = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
            }

            foreach (var period in loan.Periods)
            {
                await using var insertPeriod = new SqlCommand(
                    """
                    INSERT INTO app.LoanPeriod (LoanId, StartDate, ProfileId, RateType, RepaymentType, CreatedBy, UpdatedBy)
                    VALUES (@LoanId, @Start, @ProfileId, @RateType, @RepaymentType, @UserId, @UserId);
                    """,
                    connection, transaction);
                insertPeriod.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = id });
                insertPeriod.Parameters.Add(Date("@Start", period.Start));
                insertPeriod.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
                insertPeriod.Parameters.Add(new SqlParameter("@RateType", SqlDbType.VarChar, 10) { Value = period.RateType.ToString() });
                insertPeriod.Parameters.Add(new SqlParameter("@RepaymentType", SqlDbType.VarChar, 12) { Value = period.RepaymentType.ToString() });
                insertPeriod.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
                await insertPeriod.ExecuteNonQueryAsync(cancellationToken);

                foreach (var rate in period.Rates)
                {
                    await InsertRateAsync(connection, transaction, profileId, id, period.Start, rate, byUserId, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return (LoanOutcome.Saved, new StoredLoan(id, loan));
        }
        catch (SqlException e) when (e.Number is _uniqueConstraintViolation or _uniqueIndexViolation)
        {
            return (LoanOutcome.NameTaken, null);
        }
    }

    public async Task<LoanOutcome> AddRateChangeAsync(
        int profileId, int loanId, DateOnly periodStart, RateChange change, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // The period must belong to this profile's loan; the key on (LoanId, EffectiveFrom) refuses a second change that day.
            await using var check = new SqlCommand(
                "SELECT COUNT(*) FROM app.LoanPeriod WITH (UPDLOCK) WHERE LoanId = @LoanId AND ProfileId = @ProfileId AND StartDate = @Start;",
                connection, transaction);
            check.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
            check.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
            check.Parameters.Add(Date("@Start", periodStart));
            if ((int)(await check.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                return LoanOutcome.NotFound;
            }

            await InsertRateAsync(connection, transaction, profileId, loanId, periodStart, change, byUserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return LoanOutcome.Saved;
        }
        catch (SqlException e) when (e.Number is _uniqueConstraintViolation or _uniqueIndexViolation)
        {
            return LoanOutcome.RateDateTaken;
        }
    }

    private static async Task InsertRateAsync(
        SqlConnection connection, SqlTransaction transaction, int profileId, int loanId, DateOnly periodStart, RateChange rate, int byUserId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            INSERT INTO app.LoanRate (LoanId, EffectiveFrom, ProfileId, PeriodStart, AnnualRate, CreatedBy, UpdatedBy)
            VALUES (@LoanId, @EffectiveFrom, @ProfileId, @PeriodStart, @AnnualRate, @UserId, @UserId);
            """,
            connection, transaction);
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = loanId });
        command.Parameters.Add(Date("@EffectiveFrom", rate.EffectiveFrom));
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(Date("@PeriodStart", periodStart));
        command.Parameters.Add(new SqlParameter("@AnnualRate", SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = rate.AnnualRate });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlParameter Amount(string name, decimal value) =>
        new(name, SqlDbType.Decimal) { Precision = 19, Scale = 4, Value = value };

    private static SqlParameter Date(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };

    /// <summary>Reads the profile's loans (or one) with their periods and rates. The loan's own rows only ever grow, and the Loan constructor re-checks them on the way out.</summary>
    private static async Task<IReadOnlyList<StoredLoan>> ReadAsync(SqlConnection connection, int? loanId, int profileId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT l.Id, l.Name, l.CurrencyCode, c.MinorUnits, l.Principal, l.PaidOut, l.FirstPaymentDate, l.TermMonths, l.FeePerPayment, l.DayCount
                FROM app.Loan l JOIN app.Currency c ON c.Code = l.CurrencyCode
                WHERE l.ProfileId = @ProfileId AND (@LoanId IS NULL OR l.Id = @LoanId) ORDER BY l.Name, l.Id;
            SELECT p.LoanId, p.StartDate, p.RateType, p.RepaymentType FROM app.LoanPeriod p
                WHERE p.ProfileId = @ProfileId AND (@LoanId IS NULL OR p.LoanId = @LoanId) ORDER BY p.LoanId, p.StartDate;
            SELECT r.LoanId, r.PeriodStart, r.EffectiveFrom, r.AnnualRate FROM app.LoanRate r
                WHERE r.ProfileId = @ProfileId AND (@LoanId IS NULL OR r.LoanId = @LoanId) ORDER BY r.LoanId, r.EffectiveFrom;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@LoanId", SqlDbType.Int) { Value = (object?)loanId ?? DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var heads = new List<(int Id, string Name, Currency Currency, decimal Principal, DateOnly PaidOut, DateOnly First, int Term, decimal Fee, DayCount DayCount)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var currency = new Currency(reader.GetString(2).Trim(), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture));
            heads.Add((reader.GetInt32(0), reader.GetString(1), currency, reader.GetDecimal(4), DateOnly.FromDateTime(reader.GetDateTime(5)),
                DateOnly.FromDateTime(reader.GetDateTime(6)), reader.GetInt32(7), reader.GetDecimal(8), Enum.Parse<DayCount>(reader.GetString(9))));
        }

        await reader.NextResultAsync(cancellationToken);
        var periods = new List<(int LoanId, DateOnly Start, RateType RateType, RepaymentType Repayment)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            periods.Add((reader.GetInt32(0), DateOnly.FromDateTime(reader.GetDateTime(1)), Enum.Parse<RateType>(reader.GetString(2)), Enum.Parse<RepaymentType>(reader.GetString(3))));
        }

        await reader.NextResultAsync(cancellationToken);
        var rates = new List<(int LoanId, DateOnly PeriodStart, RateChange Change)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rates.Add((reader.GetInt32(0), DateOnly.FromDateTime(reader.GetDateTime(1)), new RateChange(DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetDecimal(3))));
        }

        return heads.Select(h => new StoredLoan(
            h.Id,
            new Loan(
                h.Name,
                new Money(h.Principal, h.Currency),
                h.First,
                h.Term,
                periods.Where(p => p.LoanId == h.Id)
                    .Select(p => new LoanPeriod(p.Start, p.RateType, p.Repayment, rates.Where(r => r.LoanId == h.Id && r.PeriodStart == p.Start).Select(r => r.Change).ToArray()))
                    .ToArray(),
                new Money(h.Fee, h.Currency),
                h.PaidOut,
                h.DayCount))).ToList();
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
