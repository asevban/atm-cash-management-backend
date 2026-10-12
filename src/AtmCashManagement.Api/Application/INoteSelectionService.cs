using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application;

public interface INoteSelectionService
{
    NoteSelectionResult Select(
        Currency currency,
        int requestedAmount,
        IReadOnlyCollection<CassetteStockInput> cassetteStocks,
        int? remainingDailyLimit = null);
}
