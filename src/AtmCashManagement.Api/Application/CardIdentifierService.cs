using System.Security.Cryptography;
using System.Text;

namespace AtmCashManagement.Api.Application;

/// <summary>Produces a stable keyed identifier without persisting or logging card numbers.</summary>
public sealed class CardIdentifierService
{
    private readonly byte[] _key;

    public CardIdentifierService(IConfiguration configuration)
    {
        var key = configuration["CardSecurity:CardNumberHmacKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("CardSecurity:CardNumberHmacKey must be configured with at least 32 bytes.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public string CreateIdentifier(string cardNumber) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(cardNumber))).ToLowerInvariant();
}
