using System.Text.Json;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;

namespace AtmCashManagement.Api.Application;

public static class LowCashThreshold
{
    public static bool WasCrossed(int previousAvailable, int currentAvailable, int capacity) =>
        previousAvailable >= 0 && currentAvailable >= 0 && capacity > 0 &&
        (long)previousAvailable * 10 >= capacity && (long)currentAvailable * 10 < capacity;
}

public sealed record LowCashAlertPayload(
    Guid AtmId,
    Guid CassetteId,
    string Currency,
    int Denomination,
    int PreviousAvailable,
    int CurrentAvailable,
    int Capacity,
    DateTimeOffset OccurredAt);

public sealed class LowCashEventRecorder(AtmDbContext db)
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public void RecordIfThresholdCrossed(
        Cassette cassette,
        CassetteBalance balance,
        int previousAvailable,
        DateTimeOffset occurredAt,
        string sourceReference)
    {
        if (cassette.Type != CassetteType.Recycling ||
            !LowCashThreshold.WasCrossed(previousAvailable, balance.AvailableCount, cassette.Capacity))
        {
            return;
        }

        var payload = new LowCashAlertPayload(
            cassette.AtmId,
            cassette.Id,
            balance.Currency.ToString(),
            (int)balance.Denomination,
            previousAvailable,
            balance.AvailableCount,
            cassette.Capacity,
            occurredAt.ToUniversalTime());
        var eventKey = $"LOW_CASH:{sourceReference}:{cassette.Id:N}:{balance.Currency}:{(int)balance.Denomination}";
        db.OutboxEvents.Add(new OutboxEvent(
            "LOW_CASH",
            cassette.AtmId,
            cassette.Id,
            eventKey,
            JsonSerializer.Serialize(payload, PayloadJsonOptions),
            occurredAt));
    }
}
