using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application;

public static class ReconciliationDifferenceCalculator
{
    public static decimal CalculateAbsoluteDifferenceTry(
        IEnumerable<CashReconciliationLine> lines,
        IReadOnlyDictionary<Currency, decimal> ratesToTry)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(ratesToTry);

        decimal total = 0m;
        foreach (var line in lines)
        {
            if (!ratesToTry.TryGetValue(line.Currency, out var rate) || rate <= 0m)
            {
                throw new ArgumentException($"A positive {line.Currency}-to-TRY exchange rate is required.", nameof(ratesToTry));
            }

            total += decimal.Abs(line.DifferenceCount) * (int)line.Denomination * rate;
        }

        return total;
    }
}
