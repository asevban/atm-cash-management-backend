using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.Api.Domain;

public static class AtmLifecycleRules
{
    public static bool IsValidTransition(AtmLifecycleState current, AtmLifecycleState target) =>
        (current, target) switch
        {
            (AtmLifecycleState.Unconfigured, AtmLifecycleState.Configured) => true,
            (AtmLifecycleState.Configured, AtmLifecycleState.Loaded) => true,
            (AtmLifecycleState.Loaded, AtmLifecycleState.InService) => true,
            (AtmLifecycleState.InService, AtmLifecycleState.Maintenance) => true,
            (AtmLifecycleState.Maintenance, AtmLifecycleState.InService) => true,
            _ => false
        };

    public static bool CanConfigure(AtmLifecycleState state) =>
        state is AtmLifecycleState.Unconfigured or AtmLifecycleState.Maintenance;

    public static bool CanLoadCash(AtmLifecycleState state) =>
        state is AtmLifecycleState.Configured or AtmLifecycleState.Maintenance;

    public static bool CanReconcile(AtmLifecycleState state) =>
        state == AtmLifecycleState.Maintenance;

    public static bool CanPerformCustomerCashOperation(AtmLifecycleState state) =>
        state == AtmLifecycleState.InService;
}
