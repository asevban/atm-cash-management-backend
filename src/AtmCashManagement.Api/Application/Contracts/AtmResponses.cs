using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record AtmResponse(
    Guid Id,
    string Code,
    AtmLifecycleState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? InitialLoadCompletedAt,
    IReadOnlyList<CassetteResponse> Cassettes);

public sealed record CassetteResponse(
    Guid Id,
    int Number,
    CassetteType Type,
    Currency? Currency,
    BanknoteDenomination? Denomination,
    int Capacity,
    int PhysicalCount,
    int ReservedCount,
    int AvailableCount,
    IReadOnlyList<CassetteBalanceResponse> Balances);

public sealed record CassetteBalanceResponse(
    Currency Currency,
    BanknoteDenomination Denomination,
    int PhysicalCount,
    int ReservedCount,
    int AvailableCount);

public sealed record AtmBalanceResponse(
    Guid AtmId,
    string Code,
    AtmLifecycleState State,
    IReadOnlyList<CassetteResponse> Cassettes,
    IReadOnlyList<CurrencyBalanceResponse> TotalsByCurrency);

public sealed record CurrencyBalanceResponse(Currency Currency, long TotalValue);
