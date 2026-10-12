using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CardDailyLimitUsage
{
    private CardDailyLimitUsage()
    {
    }

    public CardDailyLimitUsage(string cardIdentifier, Currency currency, DateOnly businessDate)
    {
        if (string.IsNullOrWhiteSpace(cardIdentifier) || cardIdentifier.Length != 64 || !Enum.IsDefined(currency))
        {
            throw new ArgumentException("A keyed card identifier and supported currency are required.");
        }

        Id = Guid.NewGuid();
        CardIdentifier = cardIdentifier;
        Currency = currency;
        BusinessDate = businessDate;
    }

    public Guid Id { get; private set; }
    public string CardIdentifier { get; private set; } = string.Empty;
    public Currency Currency { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public int ConfirmedAmount { get; private set; }
    public int ReservedAmount { get; private set; }

    public void Reserve(int amount, int dailyLimit)
    {
        if (amount <= 0 || (long)ConfirmedAmount + ReservedAmount + amount > dailyLimit)
        {
            throw new InvalidOperationException("The card's daily limit would be exceeded.");
        }

        ReservedAmount += amount;
    }

    public void ConfirmReservation(int reservedAmount, int confirmedAmount)
    {
        if (reservedAmount <= 0 || confirmedAmount <= 0 || confirmedAmount > reservedAmount || ReservedAmount < reservedAmount)
        {
            throw new InvalidOperationException("The daily limit reservation cannot be confirmed in this state.");
        }

        ReservedAmount -= reservedAmount;
        ConfirmedAmount = checked(ConfirmedAmount + confirmedAmount);
    }

    public void Release(int amount)
    {
        if (amount <= 0 || ReservedAmount < amount)
        {
            throw new InvalidOperationException("The daily limit reservation cannot be released in this state.");
        }

        ReservedAmount -= amount;
    }
}
