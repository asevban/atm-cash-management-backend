using System.Data;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtmCashManagement.Api.Application;

public sealed class OperationsReportService(AtmDbContext db)
{
    public async Task<OperationsReportResponse> GetAsync(Guid? atmId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rates = await db.ExchangeRates.AsNoTracking().ToListAsync(cancellationToken);
        var ratesToTry = ValidateRates(rates);

        var query = db.Atms.AsNoTracking()
            .Include(atm => atm.Cassettes)
            .ThenInclude(cassette => cassette.Balances)
            .AsQueryable();
        if (atmId.HasValue)
        {
            query = query.Where(atm => atm.Id == atmId.Value);
        }

        var atms = await query.OrderBy(atm => atm.Code).ToListAsync(cancellationToken);
        if (atmId.HasValue && atms.Count == 0)
        {
            throw AtmApiException.NotFound(atmId.Value);
        }

        var reports = atms.Select(atm => BuildAtmReport(atm, ratesToTry)).ToArray();
        var networkTotals = BuildTotals(reports.SelectMany(report => report.Cassettes).SelectMany(cassette => cassette.Balances), ratesToTry);
        await transaction.CommitAsync(cancellationToken);
        return new OperationsReportResponse(
            DateTimeOffset.UtcNow,
            reports,
            networkTotals,
            networkTotals.Sum(total => total.PhysicalValueTry));
    }

    private static IReadOnlyDictionary<Currency, decimal> ValidateRates(IReadOnlyCollection<ExchangeRate> rates)
    {
        var requiredCurrencies = Enum.GetValues<Currency>();
        var ratesToTry = new Dictionary<Currency, decimal>();
        foreach (var currency in requiredCurrencies)
        {
            var rate = rates.SingleOrDefault(item => item.BaseCurrency == currency && item.QuoteCurrency == Currency.TRY);
            if (rate is null || rate.Rate <= 0 || (currency == Currency.TRY && rate.Rate != 1m))
            {
                throw AtmApiException.ServiceUnavailable(
                    "operations_report_exchange_rate_unavailable",
                    $"A valid {currency}/TRY exchange rate is required to calculate operations totals.");
            }

            ratesToTry[currency] = rate.Rate;
        }

        return ratesToTry;
    }

    private static AtmOperationsReport BuildAtmReport(Atm atm, IReadOnlyDictionary<Currency, decimal> ratesToTry)
    {
        var cassettes = atm.Cassettes.OrderBy(cassette => cassette.Number)
            .Select(cassette => BuildCassetteReport(cassette))
            .ToArray();
        var totals = BuildTotals(cassettes.SelectMany(cassette => cassette.Balances), ratesToTry);
        return new AtmOperationsReport(
            atm.Id,
            atm.Code,
            atm.State,
            cassettes,
            totals,
            totals.Sum(total => total.PhysicalValueTry));
    }

    private static CassetteOperationsReport BuildCassetteReport(Cassette cassette)
    {
        var balances = cassette.Balances
            .OrderBy(balance => balance.Currency)
            .ThenBy(balance => balance.Denomination)
            .Select(balance => ToBalanceReport(cassette, balance.Currency, balance.Denomination,
                balance.PhysicalCount, balance.ReservedCount))
            .ToList();

        if (cassette.Type == CassetteType.Recycling && balances.Count == 0)
        {
            balances.Add(ToBalanceReport(
                cassette,
                cassette.Currency!.Value,
                cassette.Denomination!.Value,
                physicalCount: 0,
                reservedCount: 0));
        }

        return new CassetteOperationsReport(
            cassette.Id,
            cassette.Number,
            cassette.Type,
            cassette.Capacity,
            cassette.Currency,
            cassette.Denomination,
            balances.Sum(balance => balance.PhysicalCount),
            balances);
    }

    private static OperationsBalanceReport ToBalanceReport(
        Cassette cassette,
        Currency currency,
        BanknoteDenomination denomination,
        int physicalCount,
        int reservedCount)
    {
        var availableCount = physicalCount - reservedCount;
        var faceValue = (int)denomination;
        return new OperationsBalanceReport(
            currency,
            denomination,
            cassette.Type == CassetteType.Recycling,
            physicalCount,
            reservedCount,
            availableCount,
            (decimal)physicalCount * faceValue,
            (decimal)reservedCount * faceValue,
            (decimal)availableCount * faceValue);
    }

    private static IReadOnlyList<OperationsCurrencyTotal> BuildTotals(
        IEnumerable<OperationsBalanceReport> balances,
        IReadOnlyDictionary<Currency, decimal> ratesToTry)
    {
        var items = balances.ToArray();
        return Enum.GetValues<Currency>().Select(currency =>
        {
            var matching = items.Where(item => item.Currency == currency).ToArray();
            var physical = matching.Sum(item => item.PhysicalValue);
            return new OperationsCurrencyTotal(
                currency,
                physical,
                matching.Sum(item => item.ReservedValue),
                matching.Sum(item => item.AvailableValue),
                matching.Where(item => item.WithdrawalEligible).Sum(item => item.AvailableValue),
                ratesToTry[currency],
                physical * ratesToTry[currency]);
        }).ToArray();
    }
}
