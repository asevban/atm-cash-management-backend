using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Application.Contracts;

public sealed record CreateAtmRequest(string? Code);

public sealed record ConfigureAtmRequest(IReadOnlyList<CassetteDefinitionRequest>? Cassettes);

public sealed record CassetteDefinitionRequest(
    int Number,
    CassetteType Type,
    Currency? Currency = null,
    BanknoteDenomination? Denomination = null,
    int Capacity = 100);

public sealed record LoadCashRequest(IReadOnlyList<CassetteLoadRequest>? Cassettes);

public sealed record CassetteLoadRequest(int CassetteNumber, IReadOnlyList<BanknoteLoadRequest>? Notes);

public sealed record BanknoteLoadRequest(Currency Currency, BanknoteDenomination Denomination, int Count);

public sealed record SetAtmStateRequest(AtmLifecycleState State);
