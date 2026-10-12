using System.Text.Json.Serialization;

namespace AtmCashManagement.Api.Domain.Enums;

public enum WithdrawalQuoteStatus
{
    [JsonStringEnumMemberName("ACTIVE")]
    Active,

    [JsonStringEnumMemberName("CONFIRMED")]
    Confirmed,

    [JsonStringEnumMemberName("CANCELLED")]
    Cancelled,

    [JsonStringEnumMemberName("EXPIRED")]
    Expired
}

public enum WithdrawalOptionKind
{
    [JsonStringEnumMemberName("EXACT")]
    Exact,

    [JsonStringEnumMemberName("LOWER")]
    Lower,

    [JsonStringEnumMemberName("UPPER")]
    Upper
}
