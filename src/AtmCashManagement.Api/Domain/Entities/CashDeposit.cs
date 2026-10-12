namespace AtmCashManagement.Api.Domain.Entities;

public sealed class CashDeposit
{
    private CashDeposit()
    {
    }

    public CashDeposit(Guid atmId, DateTimeOffset occurredAt, int unrecognizedNoteCount)
    {
        if (atmId == Guid.Empty)
        {
            throw new ArgumentException("An ATM identifier is required.", nameof(atmId));
        }

        if (unrecognizedNoteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unrecognizedNoteCount));
        }

        Id = Guid.NewGuid();
        AtmId = atmId;
        OccurredAt = occurredAt;
        UnrecognizedNoteCount = unrecognizedNoteCount;
    }

    public Guid Id { get; private set; }
    public Guid AtmId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public int UnrecognizedNoteCount { get; private set; }
    public ICollection<CashDepositNote> Notes { get; private set; } = new List<CashDepositNote>();

    public void AddNote(CashDepositNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.CashDepositId != Id)
        {
            throw new ArgumentException("The deposit note must belong to this deposit.", nameof(note));
        }

        Notes.Add(note);
    }
}
