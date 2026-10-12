using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class ExchangeRate
{
    private ExchangeRate()
    {
    }

    public ExchangeRate(Currency baseCurrency, Currency quoteCurrency, decimal rate)
    {
        if (!Enum.IsDefined(baseCurrency) || !Enum.IsDefined(quoteCurrency))
        {
            throw new ArgumentOutOfRangeException(nameof(baseCurrency), "Supported currencies are required.");
        }

        ValidateRate(baseCurrency, quoteCurrency, rate);
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        Rate = rate;
    }

    public Currency BaseCurrency { get; private set; }
    public Currency QuoteCurrency { get; private set; }
    public decimal Rate { get; private set; }

    public void ReviseRate(decimal rate)
    {
        ValidateRate(BaseCurrency, QuoteCurrency, rate);
        Rate = rate;
    }

    private static void ValidateRate(Currency baseCurrency, Currency quoteCurrency, decimal rate)
    {
        if (quoteCurrency != Currency.TRY || rate <= 0 ||
            (baseCurrency == Currency.TRY && rate != 1m) ||
            (baseCurrency is not (Currency.TRY or Currency.USD or Currency.EUR)))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "A positive supported currency-to-TRY rate is required; TRY to TRY must equal 1.");
        }
    }
}
