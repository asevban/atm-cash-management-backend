using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain.Entities;

public sealed class Atm
{
    private Atm()
    {
    }

    public Atm(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("ATM code is required.", nameof(code));
        }

        if (code.Trim().Length > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "ATM code cannot exceed 50 characters.");
        }

        Id = Guid.NewGuid();
        Code = code.Trim();
        State = AtmLifecycleState.Unconfigured;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public AtmLifecycleState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<Cassette> Cassettes { get; private set; } = new List<Cassette>();
}
