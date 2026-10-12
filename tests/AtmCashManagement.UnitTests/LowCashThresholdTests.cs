using AtmCashManagement.Api.Application;

namespace AtmCashManagement.UnitTests;

public sealed class LowCashThresholdTests
{
    [Theory]
    [InlineData(10, 10, 100, false)]
    [InlineData(12, 9, 100, true)]
    [InlineData(8, 7, 100, false)]
    [InlineData(10, 9, 100, true)]
    [InlineData(20, 19, 200, true)]
    [InlineData(0, 0, 0, false)]
    public void Crossing_requires_a_downward_transition_across_ten_percent(
        int previousAvailable,
        int currentAvailable,
        int capacity,
        bool expected) =>
        Assert.Equal(expected, LowCashThreshold.WasCrossed(previousAvailable, currentAvailable, capacity));
}
