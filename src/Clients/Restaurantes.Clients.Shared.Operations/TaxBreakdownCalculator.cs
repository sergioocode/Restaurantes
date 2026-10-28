namespace Restaurantes.Clients.Shared.Operations;

public sealed record TaxBreakdownLine(decimal Rate, decimal TaxableBase, decimal TaxAmount);

public static class TaxBreakdownCalculator
{
    public static IReadOnlyList<TaxBreakdownLine> Calculate(
        IEnumerable<(decimal? TaxRate, decimal GrossAmount)> amounts,
        decimal discountPercentage
    )
    {
        SortedDictionary<decimal, decimal> grossByRate = [];
        bool hasUnratedLines = false;
        foreach ((decimal? taxRate, decimal grossAmount) in amounts)
        {
            if (taxRate is not decimal rate)
            {
                hasUnratedLines = true;
                continue;
            }

            grossByRate[rate] = grossByRate.GetValueOrDefault(rate) + grossAmount;
        }

        if (hasUnratedLines || grossByRate.Count == 0)
        {
            return [];
        }

        decimal factor = 1m - discountPercentage / 100m;
        return grossByRate
            .Select(pair =>
            {
                decimal gross = Math.Round(pair.Value * factor, 2, MidpointRounding.AwayFromZero);
                decimal tax = Math.Round(
                    gross * pair.Key / (100m + pair.Key),
                    2,
                    MidpointRounding.AwayFromZero
                );
                return new TaxBreakdownLine(pair.Key, gross - tax, tax);
            })
            .ToArray();
    }
}
