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
    public DateTimeOffset? InitialLoadCompletedAt { get; private set; }
    public ICollection<Cassette> Cassettes { get; private set; } = new List<Cassette>();

    public void MarkConfigured()
    {
        if (State == AtmLifecycleState.Unconfigured)
        {
            State = AtmLifecycleState.Configured;
            return;
        }

        if (State != AtmLifecycleState.Maintenance)
        {
            throw new InvalidOperationException("Cassettes can only be configured during initial setup or maintenance.");
        }
    }

    public void CompleteInitialLoad(DateTimeOffset completedAt)
    {
        if (State != AtmLifecycleState.Configured || InitialLoadCompletedAt is not null)
        {
            throw new InvalidOperationException("Initial cash loading can only be completed once after configuration.");
        }

        InitialLoadCompletedAt = completedAt;
        State = AtmLifecycleState.Loaded;
    }

    public void TransitionTo(AtmLifecycleState target)
    {
        if (!AtmLifecycleRules.IsValidTransition(State, target))
        {
            throw new InvalidOperationException($"The transition from {State} to {target} is not allowed.");
        }

        State = target;
    }
}
