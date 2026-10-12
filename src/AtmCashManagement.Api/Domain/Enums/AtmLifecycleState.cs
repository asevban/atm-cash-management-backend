using System.Text.Json.Serialization;

namespace AtmCashManagement.Api.Domain.Enums;

public enum AtmLifecycleState
{
    [JsonStringEnumMemberName("UNCONFIGURED")]
    Unconfigured,

    [JsonStringEnumMemberName("CONFIGURED")]
    Configured,

    [JsonStringEnumMemberName("LOADED")]
    Loaded,

    [JsonStringEnumMemberName("IN_SERVICE")]
    InService,

    [JsonStringEnumMemberName("MAINTENANCE")]
    Maintenance
}
