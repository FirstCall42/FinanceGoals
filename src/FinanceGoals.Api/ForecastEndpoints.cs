using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Loans;
using FinanceGoals.Application.NetWorth;
using FinanceGoals.Application.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinanceGoals.Api;

/// <summary>What the household expects of an account; the return is in percent per year, like the loan rates.</summary>
internal sealed record SaveAssumption(decimal? ExpectedYearlyReturnPercent = null, decimal? MonthlyContribution = null);

internal sealed record AssumptionView(int AccountId, decimal? ExpectedYearlyReturnPercent, decimal? MonthlyContribution);

/// <summary>The balance forecast (#119) and the expectations behind it, in the household endpoint group.</summary>
internal static class ForecastEndpoints
{
    public static RouteGroupBuilder MapForecast(this RouteGroupBuilder profile)
    {
        profile.MapGet("/net-worth/forecast", async Task<Results<Ok<ForecastResult>, ValidationProblem>> (
                string? currency, int? months, HttpContext http, IProfileStore profiles, IAccountStore accounts, IAccountValueStore values,
                IAccountAssumptionStore assumptions, ILoanStore loanStore, ILoanExtraStore loanExtras, ICurrencyCatalog currencies, IRateHistory rates, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                var count = months ?? 120;
                if (count is < 1 or > BalanceForecast.MaxMonths)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["months"] = [$"Ask for 1 to {BalanceForecast.MaxMonths} months."] });
                }

                var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                var context = await NetWorthEndpoints.LoadAsync(currency, today, http, profiles, accounts, values, loanStore, loanExtras, currencies, rates, cancellationToken);
                if (context.Error is { } error)
                {
                    return TypedResults.ValidationProblem(error);
                }

                var saved = await assumptions.ListAsync(http.Access().ProfileId, cancellationToken);
                var projected = BalanceForecast.Project(today, count, context.Target, context.Accounts, context.Values, saved, context.MinorUnits, context.Converter);

                // Until loans follow their schedules in the forecast (#142), today's debts are held flat, so the
                // forecast continues the net history instead of jumping up by what the loans owe.
                var debts = NetWorthCalculator.On(today, context.Target, context.Accounts, context.Values, context.MinorUnits, context.Converter, context.Loans).Debts;
                var owed = debts.Sum(d => d.Converted ?? 0m);
                var missing = projected.MissingRates.Concat(debts.Where(d => d.Converted is null).Select(d => d.CurrencyCode)).Distinct().Order(StringComparer.Ordinal).ToArray();
                return TypedResults.Ok(projected with
                {
                    MissingRates = missing,
                    Points = [.. projected.Points.Select(p => p with { Low = p.Low - owed, Expected = p.Expected - owed, High = p.High - owed })],
                });
            })
            .WithName("GetBalanceForecast")
            .WithSummary("Balances month by month from each account's latest balance, with a low, expected and high return, in a currency at today's rates. Today's loan balances are subtracted and held flat until loans follow their schedules.");

        profile.MapGet("/account-assumptions", async (HttpContext http, IAccountAssumptionStore assumptions, CancellationToken cancellationToken) =>
                TypedResults.Ok((await assumptions.ListAsync(http.Access().ProfileId, cancellationToken)).Select(View).ToList()))
            .WithName("ListAccountAssumptions")
            .WithSummary("The expected yearly return and monthly contribution saved for accounts.");

        profile.MapPut("/accounts/{accountId:int}/assumption", async Task<Results<Ok<AssumptionView>, NotFound, ValidationProblem>> (
                int accountId, SaveAssumption request, HttpContext http, IAccountStore accounts, IAccountAssumptionStore assumptions, ICurrencyCatalog currencies, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                if (request.ExpectedYearlyReturnPercent is null && request.MonthlyContribution is null)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["assumption"] = ["Give an expected return, a monthly contribution, or both."] });
                }

                var errors = new Dictionary<string, string[]>();
                if (request.ExpectedYearlyReturnPercent is { } percent && (percent < -50 || percent > 100 || decimal.Round(percent, 4) != percent))
                {
                    errors["expectedYearlyReturnPercent"] = ["The return must be between -50 and 100 % with at most four decimals."];
                }

                var account = (await accounts.ListAsync(access.ProfileId, includeArchived: true, cancellationToken)).FirstOrDefault(a => a.Id == accountId);
                if (account is null)
                {
                    return TypedResults.NotFound();
                }

                var units = (await currencies.ListAsync(cancellationToken)).First(c => c.Code == account.CurrencyCode).MinorUnits;

                if (request.MonthlyContribution is { } contribution
                    && (contribution < 0 || contribution > 1_000_000_000_000m || decimal.Round(contribution, units) != contribution))
                {
                    errors["monthlyContribution"] = ["The contribution must be zero or more, in whole minor units (øre, cents)."];
                }

                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var saved = new AccountAssumption(accountId, request.ExpectedYearlyReturnPercent / 100m, request.MonthlyContribution);
                return await assumptions.SaveAsync(access.ProfileId, saved, access.UserId, cancellationToken) ? TypedResults.Ok(View(saved)) : TypedResults.NotFound();
            })
            .WithName("SaveAccountAssumption")
            .WithSummary("Saves the expected yearly return (percent) and monthly contribution (in the account's currency) of an account, replacing earlier ones.");

        profile.MapDelete("/accounts/{accountId:int}/assumption", async Task<Results<NoContent, NotFound>> (
                int accountId, HttpContext http, IAccountAssumptionStore assumptions, CancellationToken cancellationToken) =>
                await assumptions.DeleteAsync(http.Access().ProfileId, accountId, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteAccountAssumption")
            .WithSummary("Removes the expectations saved for an account.");

        return profile;
    }

    private static AssumptionView View(AccountAssumption a) => new(a.AccountId, a.ExpectedYearlyReturn * 100m, a.MonthlyContribution);
}
