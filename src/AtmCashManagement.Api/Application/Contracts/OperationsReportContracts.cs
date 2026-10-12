using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record OperationsReportResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<AtmOperationsReport> Atms,
    IReadOnlyList<OperationsCurrencyTotal> NetworkTotalsByCurrency,
    decimal NetworkPhysicalValueTry);

public sealed record AtmOperationsReport(
    Guid AtmId,
    string Code,
    AtmLifecycleState State,
    IReadOnlyList<CassetteOperationsReport> Cassettes,
    IReadOnlyList<OperationsCurrencyTotal> TotalsByCurrency,
    decimal PhysicalValueTry);

public sealed record CassetteOperationsReport(
    Guid CassetteId,
    int Number,
    CassetteType Type,
    int Capacity,
    Currency? ConfiguredCurrency,
    BanknoteDenomination? ConfiguredDenomination,
    int TotalBanknoteCount,
    IReadOnlyList<OperationsBalanceReport> Balances);

public sealed record OperationsBalanceReport(
    Currency Currency,
    BanknoteDenomination Denomination,
    bool WithdrawalEligible,
    int PhysicalCount,
    int ReservedCount,
    int AvailableCount,
    decimal PhysicalValue,
    decimal ReservedValue,
    decimal AvailableValue);

public sealed record OperationsCurrencyTotal(
    Currency Currency,
    decimal PhysicalValue,
    decimal ReservedValue,
    decimal AvailableValue,
    decimal WithdrawableAvailableValue,
    decimal RateToTry,
    decimal PhysicalValueTry);
