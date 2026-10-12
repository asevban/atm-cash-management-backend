using System.Text.Json.Serialization;

namespace AtmCashManagement.Api.Domain.Enums;

public enum CashReconciliationStatus
{
    [JsonStringEnumMemberName("COMPLETED")]
    Completed,

    [JsonStringEnumMemberName("AWAITING_APPROVAL")]
    AwaitingApproval,

    [JsonStringEnumMemberName("APPROVED")]
    Approved
}
