using FinanceGoals.Application.Loans;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using ExtrasResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<FinanceGoals.Api.LoanExtrasView>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ValidationProblem,
    Microsoft.AspNetCore.Http.HttpResults.Conflict<Microsoft.AspNetCore.Mvc.ProblemDetails>>;

namespace FinanceGoals.Api;

internal sealed record AddOneOffExtra(DateOnly? Date = null, decimal? Amount = null);

internal sealed record AddMonthlyExtra(decimal? Amount = null, DateOnly? From = null, DateOnly? Until = null);

internal sealed record SaveLoanBalance(decimal? Amount = null);

internal sealed record OneOffExtraView(DateOnly Date, decimal Amount);

internal sealed record MonthlyExtraView(int Id, decimal Amount, DateOnly From, DateOnly? Until);

internal sealed record LoanBalanceView(DateOnly Date, decimal Amount);

/// <summary>A loan's extra payments and recorded balances, amounts in the loan's currency.</summary>
internal sealed record LoanExtrasView(string CurrencyCode, IReadOnlyList<OneOffExtraView> OneOff, IReadOnlyList<MonthlyExtraView> Monthly, IReadOnlyList<LoanBalanceView> Balances)
{
    public static LoanExtrasView From(string currencyCode, LoanExtras extras) => new(
        currencyCode,
        extras.OneOff.Select(e => new OneOffExtraView(e.Date, e.Amount)).ToArray(),
        extras.Monthly.Select(e => new MonthlyExtraView(e.Id, e.Amount, e.From, e.Until)).ToArray(),
        extras.Balances.Select(b => new LoanBalanceView(b.Date, b.Amount)).ToArray());
}

/// <summary>Extra payments and actual balances of a stored loan (#137), in the household endpoint group.</summary>
internal static class LoanExtraEndpoints
{
    public static RouteGroupBuilder MapLoanExtras(this RouteGroupBuilder profile)
    {
        profile.MapGet("/loans/{loanId:int}/extras", async Task<ExtrasResult> (int loanId, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                if (await loans.GetAsync(access.ProfileId, loanId, cancellationToken) is not { } stored
                    || await extras.GetAsync(access.ProfileId, loanId, cancellationToken) is not { } all)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(LoanExtrasView.From(stored.Loan.Principal.Currency.Code, all));
            })
            .WithName("GetLoanExtras")
            .WithSummary("The loan's one-off and monthly extra payments and the balances recorded from the bank.");

        profile.MapPost("/loans/{loanId:int}/extras/one-off", async Task<ExtrasResult> (
                int loanId, AddOneOffExtra request, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                if (await Load(access.ProfileId, loanId, loans, extras, cancellationToken) is not { } current)
                {
                    return TypedResults.NotFound();
                }

                if (request.Date is not { } date || request.Amount is not { } amount)
                {
                    return Problem("extra", "Give the day and the amount.");
                }

                if (current.Extras.OneOff.Any(e => e.Date == date))
                {
                    return TypedResults.Conflict(new ProblemDetails { Title = "There is already an extra payment on that day.", Status = StatusCodes.Status409Conflict });
                }

                if (Check(current.Loan, current.Extras, c => (new ExtraPayment(date, new Money(amount, c)), null)) is { } error)
                {
                    return Problem("extra", error);
                }

                return await Result(await extras.AddOneOffAsync(access.ProfileId, loanId, date, amount, access.UserId, cancellationToken), access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("AddLoanOneOffExtra")
            .WithSummary("Records a one-off extra payment on a day.");

        profile.MapDelete("/loans/{loanId:int}/extras/one-off/{date}", async Task<ExtrasResult> (
                int loanId, DateOnly date, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                return await Result(await extras.DeleteOneOffAsync(access.ProfileId, loanId, date, cancellationToken), access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("DeleteLoanOneOffExtra")
            .WithSummary("Deletes the one-off extra payment on a day.");

        profile.MapPost("/loans/{loanId:int}/extras/monthly", async Task<ExtrasResult> (
                int loanId, AddMonthlyExtra request, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                if (await Load(access.ProfileId, loanId, loans, extras, cancellationToken) is not { } current)
                {
                    return TypedResults.NotFound();
                }

                if (request.Amount is not { } amount || request.From is not { } from)
                {
                    return Problem("extra", "Give the amount and the day it starts.");
                }

                if (Check(current.Loan, current.Extras, c => (null, new MonthlyExtraPayment(new Money(amount, c), from, request.Until))) is { } error)
                {
                    return Problem("extra", error);
                }

                var (outcome, _) = await extras.AddMonthlyAsync(access.ProfileId, loanId, amount, from, request.Until, access.UserId, cancellationToken);
                return await Result(outcome, access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("AddLoanMonthlyExtra")
            .WithSummary("Records an extra payment made with every regular payment from a day, optionally until a day.");

        profile.MapDelete("/loans/{loanId:int}/extras/monthly/{extraId:int}", async Task<ExtrasResult> (
                int loanId, int extraId, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                return await Result(await extras.DeleteMonthlyAsync(access.ProfileId, loanId, extraId, cancellationToken), access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("DeleteLoanMonthlyExtra")
            .WithSummary("Deletes a monthly extra payment.");

        profile.MapPut("/loans/{loanId:int}/balances/{date}", async Task<ExtrasResult> (
                int loanId, DateOnly date, SaveLoanBalance request, HttpContext http, ILoanStore loans, ILoanExtraStore extras, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                if (await loans.GetAsync(access.ProfileId, loanId, cancellationToken) is not { } stored)
                {
                    return TypedResults.NotFound();
                }

                var loan = stored.Loan;
                var units = loan.Principal.Currency.MinorUnits;
                if (request.Amount is not { } amount || amount < 0 || amount > 1_000_000_000_000m || decimal.Round(amount, units) != amount)
                {
                    return Problem("amount", "The balance must be zero or more, in whole minor units (øre, cents).");
                }

                if (date < loan.Start || date > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1))
                {
                    return Problem("date", $"The balance date must be between {loan.Start:yyyy-MM-dd} (paid out) and today.");
                }

                return await Result(await extras.SaveBalanceAsync(access.ProfileId, loanId, date, amount, access.UserId, cancellationToken), access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("SaveLoanBalance")
            .WithSummary("Records the balance the bank reports on a day, replacing one already recorded for that day.");

        profile.MapDelete("/loans/{loanId:int}/balances/{date}", async Task<ExtrasResult> (
                int loanId, DateOnly date, HttpContext http, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                return await Result(await extras.DeleteBalanceAsync(access.ProfileId, loanId, date, cancellationToken), access.ProfileId, loanId, loans, extras, cancellationToken);
            })
            .WithName("DeleteLoanBalance")
            .WithSummary("Deletes the balance recorded on a day.");

        return profile;
    }

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<(Loan Loan, LoanExtras Extras)?> Load(int profileId, int loanId, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken) =>
        await loans.GetAsync(profileId, loanId, cancellationToken) is { } stored && await extras.GetAsync(profileId, loanId, cancellationToken) is { } all
            ? (stored.Loan, all)
            : null;

    /// <summary>The extras as the schedule engine takes them, with one more; its own checks say whether they fit the loan.</summary>
    private static string? Check(Loan loan, LoanExtras stored, Func<Currency, (ExtraPayment? OneOff, MonthlyExtraPayment? Monthly)> add)
    {
        var currency = loan.Principal.Currency;
        try
        {
            var (newOneOff, newMonthly) = add(currency);
            var oneOff = stored.OneOff.Select(e => new ExtraPayment(e.Date, new Money(e.Amount, currency))).Concat(newOneOff is null ? [] : [newOneOff]);
            var monthly = stored.Monthly.Select(e => new MonthlyExtraPayment(new Money(e.Amount, currency), e.From, e.Until)).Concat(newMonthly is null ? [] : [newMonthly]);
            LoanSchedule.Calculate(loan, new ExtraPayments(oneOff, monthly));
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
        {
            return e.Message;
        }
    }

    /// <summary>The saved extras after a write, or NotFound when the write found nothing.</summary>
    private static async Task<ExtrasResult> Result(
        LoanExtraOutcome outcome, int profileId, int loanId, ILoanStore loans, ILoanExtraStore extras, CancellationToken cancellationToken)
    {
        if (outcome == LoanExtraOutcome.NotFound
            || await loans.GetAsync(profileId, loanId, cancellationToken) is not { } stored
            || await extras.GetAsync(profileId, loanId, cancellationToken) is not { } all)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(LoanExtrasView.From(stored.Loan.Principal.Currency.Code, all));
    }
}
