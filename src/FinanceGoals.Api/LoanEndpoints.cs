using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Loans;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FinanceGoals.Api;

internal sealed record RateInput(DateOnly? EffectiveFrom = null, decimal? AnnualRatePercent = null);

internal sealed record PeriodInput(DateOnly? Start = null, string? RateType = null, string? RepaymentType = null, List<RateInput>? Rates = null);

internal sealed record CreateLoan(
    string? Name = null, string? CurrencyCode = null, decimal? Principal = null, DateOnly? PaidOut = null, DateOnly? FirstPaymentDate = null,
    int? TermMonths = null, decimal? FeePerPayment = null, string? DayCount = null, List<PeriodInput>? Periods = null,
    bool? SecuredOnProperty = null);

internal sealed record SetSecured(bool? SecuredOnProperty = null);

internal sealed record AddRateChange(DateOnly? EffectiveFrom = null, decimal? AnnualRatePercent = null);

internal sealed record RateView(DateOnly EffectiveFrom, decimal AnnualRatePercent);

internal sealed record PeriodView(DateOnly Start, string RateType, string RepaymentType, IReadOnlyList<RateView> Rates);

/// <summary>A stored loan as the API shows it: rates in percent, like the calculator.</summary>
internal sealed record LoanView(
    int Id, string Name, string CurrencyCode, decimal Principal, DateOnly PaidOut, DateOnly FirstPaymentDate, int TermMonths,
    decimal FeePerPayment, string DayCount, bool SecuredOnProperty, IReadOnlyList<PeriodView> Periods)
{
    public static LoanView From(StoredLoan stored)
    {
        var loan = stored.Loan;
        return new LoanView(
            stored.Id, loan.Name, loan.Principal.Currency.Code, loan.Principal.Amount, loan.Start, loan.FirstPaymentDate, loan.TermMonths,
            loan.FeePerPayment.Amount, loan.DayCount.ToString(), loan.SecuredOnProperty,
            loan.Periods.Select(p => new PeriodView(
                p.Start, p.RateType.ToString(), p.RepaymentType.ToString(), p.Rates.Select(r => new RateView(r.EffectiveFrom, r.AnnualRate * 100m)).ToArray())).ToArray());
    }
}

/// <summary>The household's stored loans (#107), in the household endpoint group, so membership is checked and other households get 404.</summary>
internal static class LoanEndpoints
{
    public static RouteGroupBuilder MapLoans(this RouteGroupBuilder profile)
    {
        profile.MapGet("/loans", async (HttpContext http, ILoanStore loans, CancellationToken cancellationToken) =>
                TypedResults.Ok((await loans.ListAsync(http.Access().ProfileId, cancellationToken)).Select(LoanView.From).ToList()))
            .WithName("ListLoans")
            .WithSummary("The household's loans, by name, with their periods and rate changes.");

        profile.MapGet("/loans/{loanId:int}", async Task<Results<Ok<LoanView>, NotFound>> (int loanId, HttpContext http, ILoanStore loans, CancellationToken cancellationToken) =>
                await loans.GetAsync(http.Access().ProfileId, loanId, cancellationToken) is { } stored ? TypedResults.Ok(LoanView.From(stored)) : TypedResults.NotFound())
            .WithName("GetLoan")
            .WithSummary("One loan of the household.");

        profile.MapPost("/loans", async Task<Results<Created<LoanView>, ValidationProblem, Conflict<ProblemDetails>>> (
                CreateLoan request, HttpContext http, ILoanStore loans, ICurrencyCatalog currencies, CancellationToken cancellationToken) =>
            {
                var catalog = await currencies.ListAsync(cancellationToken);
                var (loan, errors) = Build(request, catalog.ToDictionary(c => c.Code, c => (int)c.MinorUnits));
                if (loan is null)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var access = http.Access();
                var (outcome, stored) = await loans.CreateAsync(access.ProfileId, loan, access.UserId, cancellationToken);
                return outcome == LoanOutcome.NameTaken
                    ? TypedResults.Conflict(new ProblemDetails { Title = "A loan with that name already exists.", Status = StatusCodes.Status409Conflict })
                    : TypedResults.Created($"/api/profiles/{access.ProfileId}/loans/{stored!.Id}", LoanView.From(stored));
            })
            .WithName("CreateLoan")
            .WithSummary("Saves a loan with its periods and their rate changes. The rules are those of the schedule engine.");

        profile.MapPut("/loans/{loanId:int}/secured-on-property", async Task<Results<Ok<LoanView>, NotFound, ValidationProblem>> (
                int loanId, SetSecured request, HttpContext http, ILoanStore loans, CancellationToken cancellationToken) =>
            {
                if (request.SecuredOnProperty is not { } secured)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["securedOnProperty"] = ["Say whether the loan is secured on property."] });
                }

                var access = http.Access();
                if (!await loans.SetSecuredOnPropertyAsync(access.ProfileId, loanId, secured, access.UserId, cancellationToken))
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(LoanView.From((await loans.GetAsync(access.ProfileId, loanId, cancellationToken))!));
            })
            .WithName("SetLoanSecuredOnProperty")
            .WithSummary("Marks a loan as secured (or not) on property such as the home, so liquid net worth leaves it out.");

        profile.MapPost("/loans/{loanId:int}/rate-changes", async Task<Results<Ok<LoanView>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> (
                int loanId, AddRateChange request, HttpContext http, ILoanStore loans, CancellationToken cancellationToken) =>
            {
                if (request.EffectiveFrom is not { } from || request.AnnualRatePercent is not { } percent || percent < 0 || percent > 100 || decimal.Round(percent, 4) != percent)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rate"] = ["Give the day the rate takes effect and a rate between 0 and 100 %."] });
                }

                var access = http.Access();
                if (await loans.GetAsync(access.ProfileId, loanId, cancellationToken) is not { } stored)
                {
                    return TypedResults.NotFound();
                }

                Loan changed;
                try
                {
                    changed = stored.Loan.WithRateChange(new RateChange(from, percent / 100m));
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rate"] = [e.Message] });
                }

                var period = stored.Loan.PeriodOn(from);
                if (period.Rates.Any(r => r.EffectiveFrom == from))
                {
                    return TypedResults.Conflict(new ProblemDetails { Title = "The loan already has a rate change on that day.", Status = StatusCodes.Status409Conflict });
                }

                var outcome = await loans.AddRateChangeAsync(access.ProfileId, loanId, period.Start, new RateChange(from, percent / 100m), access.UserId, cancellationToken);
                return outcome switch
                {
                    LoanOutcome.NotFound => TypedResults.NotFound(),
                    LoanOutcome.RateDateTaken => TypedResults.Conflict(new ProblemDetails { Title = "The loan already has a rate change on that day.", Status = StatusCodes.Status409Conflict }),
                    _ => TypedResults.Ok(LoanView.From(new StoredLoan(loanId, changed))),
                };
            })
            .WithName("AddLoanRateChange")
            .WithSummary("Records that the bank changed the rate of a variable-rate period, from a date.");

        return profile;
    }

    // Only the exact names: Enum.TryParse also takes numbers ("7") and comma lists, which the database would refuse with a 500.
    private static bool TryName<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, out value);
    }

    private static (Loan? Loan, Dictionary<string, string[]> Errors) Build(CreateLoan r, Dictionary<string, int> minorUnits)
    {
        var errors = new Dictionary<string, string[]>();
        var name = r.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors["name"] = ["The name must be 1 to 100 characters."];
        }

        if (r.CurrencyCode is null || !minorUnits.ContainsKey(r.CurrencyCode))
        {
            errors["currencyCode"] = ["Pick one of the supported currencies."];
        }

        if (r.Principal is not > 0 or > 1_000_000_000_000m
            || (r.CurrencyCode is not null && minorUnits.TryGetValue(r.CurrencyCode, out var units) && decimal.Round(r.Principal.Value, units) != r.Principal))
        {
            errors["principal"] = ["The principal must be positive, in whole minor units (øre, cents)."];
        }

        if (r.PaidOut is null || r.FirstPaymentDate is null)
        {
            errors["dates"] = ["Give the day the loan was paid out and the first payment day."];
        }

        if (r.TermMonths is not (>= 1 and <= 600))
        {
            errors["termMonths"] = ["The term must be 1 to 600 months."];
        }

        if (r.FeePerPayment is { } feeAmount
            && (feeAmount < 0 || feeAmount > 1_000_000_000_000m
                || (r.CurrencyCode is not null && minorUnits.TryGetValue(r.CurrencyCode, out var feeUnits) && decimal.Round(feeAmount, feeUnits) != feeAmount)))
        {
            errors["feePerPayment"] = ["The fee cannot be negative, and is in whole minor units (øre, cents)."];
        }

        if (r.Periods is null or { Count: 0 })
        {
            errors["periods"] = ["Give at least one period."];
        }

        var dayCount = DayCount.ActualActual;
        if (r.DayCount is not null && !TryName(r.DayCount, out dayCount))
        {
            errors["dayCount"] = ["Use ActualActual or Thirty360."];
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        try
        {
            var currency = new Currency(r.CurrencyCode!, minorUnits[r.CurrencyCode!]);
            var periods = r.Periods!.Select(p =>
            {
                if (p.Start is not { } start || p.Rates is null || !TryName(p.RateType, out RateType rateType) || !TryName(p.RepaymentType, out RepaymentType repayment))
                {
                    throw new ArgumentException("Each period needs a start day, a rate type (Variable or Fixed), a repayment type (Annuity, StraightLine or InterestOnly) and rates.");
                }

                var rates = p.Rates.Select(rate =>
                    rate.EffectiveFrom is { } from && rate.AnnualRatePercent is >= 0 and <= 100 && decimal.Round(rate.AnnualRatePercent.Value, 4) == rate.AnnualRatePercent
                        ? new RateChange(from, rate.AnnualRatePercent.Value / 100m)
                        : throw new ArgumentException("Each rate needs a day it takes effect and a rate between 0 and 100 % with at most four decimals.")).ToArray();
                return new LoanPeriod(start, rateType, repayment, rates);
            }).ToArray();
            var fee = new Money(r.FeePerPayment ?? 0m, currency);
            var loan = new Loan(name, new Money(r.Principal!.Value, currency), r.FirstPaymentDate!.Value, r.TermMonths!.Value, periods, fee, r.PaidOut, dayCount, r.SecuredOnProperty ?? false);
            return (loan, errors);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            errors["loan"] = [e.Message];
            return (null, errors);
        }
    }
}
