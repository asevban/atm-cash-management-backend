using AtmCashManagement.Api.Application;

namespace AtmCashManagement.IntegrationTests;

public sealed class ManualTestClock : IClock
{
    public static DateTimeOffset DefaultUtcNow { get; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private long _utcTicks = DefaultUtcNow.Ticks;

    public DateTimeOffset UtcNow => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _utcTicks, elapsed.Ticks);

    public void SetUtcNow(DateTimeOffset instant)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Test clock values must be UTC.", nameof(instant));
        }

        Interlocked.Exchange(ref _utcTicks, instant.Ticks);
    }
}
