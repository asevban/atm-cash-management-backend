using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class Cassette
{
    private Cassette()
    {
    }

    public Cassette(
        Guid atmId,
        int number,
        CassetteType type,
        Currency? currency = null,
        BanknoteDenomination? denomination = null,
        int capacity = 100)
    {
        if (atmId == Guid.Empty)
        {
            throw new ArgumentException("An ATM identifier is required.", nameof(atmId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Unsupported cassette type.");
        }

        if (currency.HasValue && !Enum.IsDefined(currency.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Unsupported currency.");
        }

        if (denomination.HasValue && !Enum.IsDefined(denomination.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(denomination), "Unsupported banknote denomination.");
        }

        if (number is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Cassette number must be between 1 and 5.");
        }

        if (capacity is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Cassette capacity must be between 1 and 100 notes.");
        }

        if (type == CassetteType.DepositOnly && (currency is not null || denomination is not null))
        {
            throw new ArgumentException("A deposit-only cassette cannot be restricted to one currency or denomination.");
        }

        if (type == CassetteType.Recycling && (currency is null || denomination is null))
        {
            throw new ArgumentException("A recycling cassette requires a currency and denomination.");
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        Number = number;
        Type = type;
        Currency = currency;
        Denomination = denomination;
        Capacity = capacity;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public int Number { get; private set; }
    public CassetteType Type { get; private set; }
    public Currency? Currency { get; private set; }
    public BanknoteDenomination? Denomination { get; private set; }
    public int Capacity { get; private set; }
    public Atm Atm { get; private set; } = null!;
    public ICollection<CassetteBalance> Balances { get; private set; } = new List<CassetteBalance>();

    public void Reconfigure(CassetteType type, Currency? currency, BanknoteDenomination? denomination, int capacity)
    {
        ValidateConfiguration(type, currency, denomination, capacity);
        Type = type;
        Currency = currency;
        Denomination = denomination;
        Capacity = capacity;
    }

    private static void ValidateConfiguration(
        CassetteType type,
        Currency? currency,
        BanknoteDenomination? denomination,
        int capacity)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Unsupported cassette type.");
        }

        if (currency.HasValue && !Enum.IsDefined(currency.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), "Unsupported currency.");
        }

        if (denomination.HasValue && !Enum.IsDefined(denomination.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(denomination), "Unsupported banknote denomination.");
        }

        if (capacity is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Cassette capacity must be between 1 and 100 notes.");
        }

        if (type == CassetteType.DepositOnly && (currency is not null || denomination is not null))
        {
            throw new ArgumentException("A deposit-only cassette cannot be restricted to one currency or denomination.");
        }

        if (type == CassetteType.Recycling && (currency is null || denomination is null))
        {
            throw new ArgumentException("A recycling cassette requires a currency and denomination.");
        }
    }
}
