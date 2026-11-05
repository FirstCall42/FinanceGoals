using System.Globalization;
using System.Xml.Linq;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Infrastructure.Currencies;

/// <summary>
/// The ECB's euro reference rates (units per EUR), published on working days around 16:00 CET.
/// The 90-day feed fills any days the import missed; it is free and needs no key.
/// </summary>
internal sealed class EcbExchangeRateSource : IExchangeRateSource
{
    public const string DefaultUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist-90d.xml";

    private static readonly XNamespace _ecb = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    private readonly HttpClient _http;

    public EcbExchangeRateSource(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<EurRate>> FetchAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _http.GetStreamAsync(_http.BaseAddress, cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        return Parse(document);
    }

    /// <summary>Reads &lt;Cube time="..."&gt;&lt;Cube currency="..." rate="..."/&gt;&lt;/Cube&gt; elements.</summary>
    internal static IReadOnlyList<EurRate> Parse(XDocument document) =>
        [.. document.Descendants(_ecb + "Cube")
            .Where(day => day.Attribute("time") is not null)
            .SelectMany(day => day.Elements(_ecb + "Cube").Select(rate => new EurRate(
                (string)rate.Attribute("currency")!,
                DateOnly.ParseExact((string)day.Attribute("time")!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal.Parse((string)rate.Attribute("rate")!, NumberStyles.Number, CultureInfo.InvariantCulture))))];
}
