using System.Net;
using System.Net.Http.Json;
using FinanceGoals.Application.Loans;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class LoanCalculatorEndpointTests
{
    private static readonly Uri _endpoint = new("/api/loans/calculate", UriKind.Relative);

    [TestMethod]
    public async Task Calculates_a_loan_without_the_database()
    {
        // Nothing is stored, so the calculator works while the database is unreachable.
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);
        var request = new LoanCalculationRequest("NOK", 2, 1_000_000m, new DateOnly(2026, 2, 1), 300, 5m,
            Extras: [new ExtraPaymentInput(new DateOnly(2026, 6, 1), 100_000m)]);

        var response = await api.CreateClient().PostAsJsonAsync(_endpoint, request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<LoanCalculationResult>()).ShouldNotBeNull();
        result.WithoutExtraSchedule[0].Payment.ShouldBe(5_845.90m);
        result.MonthsSaved.ShouldBeGreaterThan(0);
    }

    [TestMethod]
    public async Task Invalid_input_is_a_validation_problem()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);
        var request = new LoanCalculationRequest("NOK", 2, 1_000_000m, new DateOnly(2026, 2, 1), 0, 5m);

        var response = await api.CreateClient().PostAsJsonAsync(_endpoint, request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("The term must be 1 to 600 months.");
    }
}
