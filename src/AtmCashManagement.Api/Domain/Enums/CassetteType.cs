namespace AtmCashManagement.Api.Domain.Enums;

public enum CassetteType
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("DEPOSIT_ONLY")]
    DepositOnly,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("RECYCLING")]
    Recycling
}
