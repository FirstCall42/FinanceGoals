using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Loans;
using FinanceGoals.Application.NetWorth;
using FinanceGoals.Application.Profiles;
using FinanceGoals.Domain.Currencies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinanceGoals.Api;

/// <summary>Net worth of a household (#37), in the household endpoint group, so membership is checked and other households get 404.</summary>
internal static class NetWorthEndpoints
{
    private static readonly DateOnly _earliest = new(1990, 1, 1);

    public static RouteGroupBuilder MapNetWorth(this RouteGroupBuilder profile)
    {
        profile.MapGet("/net-worth", async Task<Results<Ok<NetWorthResult>, ValidationProblem>> (
                string? currency, DateOnly? date, HttpContext http, IProfileStore profiles, IAccountStore accounts, IAccountValueStore values,
                ILoanStore loanStore, ILoanExtraStore loanExtras, ICurrencyCatalog currencies, IRateHistory rates, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                var day = date ?? Today(clock);
                if (day < _earliest || day > Today(clock).AddDays(1))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["date"] = [$"The date must be between {_earliest:yyyy-MM-dd} and tomorrow."] });
                }

                var context = await LoadAsync(currency, day, http, profiles, accounts, values, loanStore, loanExtras, currencies, rates, cancellationToken);
                if (context.Error is { } error)
                {
                    return TypedResults.ValidationProblem(error);
                }

                return TypedResults.Ok(NetWorthCalculator.On(day, context.Target, context.Accounts, context.Values, context.MinorUnits, context.Converter, context.Loans));
            })
            .WithName("GetNetWorth")
            .WithSummary("Net worth on a date (default today) in a currency (default the household's main currency): each account's latest balance on or before the date, converted with that day's rates. Missing rates are listed, never replaced by 1.");

        profile.MapGet("/net-worth/history", async Task<Results<Ok<IReadOnlyList<NetWorthPoint>>, ValidationProblem>> (
                string? currency, DateOnly? to, HttpContext http, IProfileStore profiles, IAccountStore accounts, IAccountValueStore values,
                ILoanStore loanStore, ILoanExtraStore loanExtras, ICurrencyCatalog currencies, IRateHistory rates, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                var end = to ?? Today(clock);
                if (end < _earliest || end > Today(clock).AddDays(1))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = [$"The date must be between {_earliest:yyyy-MM-dd} and tomorrow."] });
                }

                var context = await LoadAsync(currency, end, http, profiles, accounts, values, loanStore, loanExtras, currencies, rates, cancellationToken);
                if (context.Error is { } error)
                {
                    return TypedResults.ValidationProblem(error);
                }

                return TypedResults.Ok(NetWorthCalculator.Monthly(end, context.Target, context.Accounts, context.Values, context.MinorUnits, context.Converter, context.Loans));
            })
            .WithName("GetNetWorthHistory")
            .WithSummary("Net worth at the end of each month from the first balance, and at the given day (default today), in a currency.");

        return profile;
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    internal sealed record Context(
        Currency Target,
        IReadOnlyList<Account> Accounts,
        IReadOnlyList<AccountValue> Values,
        IReadOnlyList<LoanDebtInput> Loans,
        IReadOnlyDictionary<string, int> MinorUnits,
        CurrencyConverter Converter,
        Dictionary<string, string[]>? Error);

    /// <summary>Everything a net worth calculation needs for one request, with all rates read in one query.</summary>
    internal static async Task<Context> LoadAsync(
        string? currency, DateOnly upTo, HttpContext http, IProfileStore profiles, IAccountStore accounts, IAccountValueStore values,
        ILoanStore loanStore, ILoanExtraStore loanExtras, ICurrencyCatalog currencies, IRateHistory rates, CancellationToken cancellationToken)
    {
        var access = http.Access();
        var minorUnits = (await currencies.ListAsync(cancellationToken)).ToDictionary(c => c.Code, c => (int)c.MinorUnits);
        var code = currency ?? (await profiles.GetAsync(access.ProfileId, access.Role, cancellationToken))?.MainCurrency;
        if (code is null || !minorUnits.TryGetValue(code, out var targetUnits))
        {
            return new Context(default, [], [], [], minorUnits, null!, new Dictionary<string, string[]> { ["currency"] = ["Pick one of the supported currencies."] });
        }

        var allAccounts = await accounts.ListAsync(access.ProfileId, includeArchived: true, cancellationToken);
        var allValues = await values.ListAsync(access.ProfileId, accountId: null, cancellationToken);
        var loans = new List<LoanDebtInput>();
        foreach (var stored in await loanStore.ListAsync(access.ProfileId, cancellationToken))
        {
            var extras = await loanExtras.GetAsync(access.ProfileId, stored.Id, cancellationToken);
            loans.Add(new LoanDebtInput(
                stored.Id, stored.Loan.Name, stored.Loan.Principal.Currency.Code, stored.Loan.SecuredOnProperty, stored.Loan.Start, extras?.Balances ?? []));
        }

        var codes = allAccounts.Select(a => a.CurrencyCode).Concat(loans.Select(l => l.CurrencyCode)).Append(code).Distinct().ToArray();
        var converter = new CurrencyConverter(new InMemoryEurRates(await rates.ListAsync(codes, upTo, cancellationToken)));
        return new Context(new Currency(code, targetUnits), allAccounts, allValues, loans, minorUnits, converter, null);
    }
}
