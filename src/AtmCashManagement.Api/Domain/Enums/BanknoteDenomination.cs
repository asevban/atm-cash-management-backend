using System.Text.Json.Serialization;

namespace AtmCashManagement.Api.Domain.Enums;

public enum BanknoteDenomination
{
    [JsonStringEnumMemberName("20")]
    Twenty = 20,

    [JsonStringEnumMemberName("50")]
    Fifty = 50,

    [JsonStringEnumMemberName("100")]
    OneHundred = 100,

    [JsonStringEnumMemberName("500")]
    FiveHundred = 500
}
