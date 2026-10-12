using System.Text.Json.Serialization;

namespace AtmCashManagement.Api.Domain.Enums;

public enum CassetteMovementType
{
    [JsonStringEnumMemberName("LOAD")]
    Load,

    [JsonStringEnumMemberName("UNLOAD")]
    Unload,

    [JsonStringEnumMemberName("WITHDRAWAL")]
    Withdrawal,

    [JsonStringEnumMemberName("DEPOSIT")]
    Deposit,

    [JsonStringEnumMemberName("RESERVATION")]
    Reservation,

    [JsonStringEnumMemberName("RESERVATION_RELEASE")]
    ReservationRelease,

    [JsonStringEnumMemberName("RECONCILIATION_ADJUSTMENT")]
    ReconciliationAdjustment
}
