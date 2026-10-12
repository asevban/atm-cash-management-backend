using System.Net;
using System.Net.Http.Json;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Domain.Enums;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtmCashManagement.IntegrationTests;

public sealed class ReconciliationApprovalTests(AtmApiFixture fixture) : IClassFixture<AtmApiFixture>
{
    [Fact]
    public async Task Exact_threshold_completes_without_second_approval_and_stores_operator_and_rates()
    {
        var atmId = await CreateMaintenanceAtmAsync(
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 10)]
            });

        using var response = await Phase6TestSupport.PostReconciliationAsync(
            fixture,
            atmId,
            Snapshot((2, [])),
            "  operator-one  ");
        using var result = await Phase6TestSupport.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("COMPLETED", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("operator-one", result.RootElement.GetProperty("createdBy").GetString());
        Assert.Equal(1000m, result.RootElement.GetProperty("totalAbsoluteDifferenceTry").GetDecimal());
        Assert.Equal(1m, result.RootElement.GetProperty("tryToTryRateSnapshot").GetDecimal());
        Assert.Equal(35m, result.RootElement.GetProperty("usdToTryRateSnapshot").GetDecimal());
        Assert.Equal(38m, result.RootElement.GetProperty("eurToTryRateSnapshot").GetDecimal());

        using var service = await SetStateAsync(atmId, AtmLifecycleState.InService);
        Assert.Equal(HttpStatusCode.OK, service.StatusCode);
    }

    [Fact]
    public async Task Absolute_net_zero_difference_requires_four_eyes_and_preserves_ledger_through_approval()
    {
        var atmId = await CreateMaintenanceAtmAsync(
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 15)]
            });
        var snapshot = Snapshot(
            (1, [new(Currency.TRY, BanknoteDenomination.FiveHundred, 3)]),
            (2, []));

        using var reconciliationResponse = await Phase6TestSupport.PostReconciliationAsync(
            fixture, atmId, snapshot, "  operator-one  ");
        using var reconciliation = await Phase6TestSupport.ReadAsync(reconciliationResponse);
        Assert.Equal(HttpStatusCode.OK, reconciliationResponse.StatusCode);
        var reconciliationId = reconciliation.RootElement.GetProperty("reconciliationId").GetGuid();
        Assert.Equal("AWAITING_APPROVAL", reconciliation.RootElement.GetProperty("status").GetString());
        Assert.Equal("operator-one", reconciliation.RootElement.GetProperty("createdBy").GetString());
        Assert.Equal(3000m, reconciliation.RootElement.GetProperty("totalAbsoluteDifferenceTry").GetDecimal());
        Assert.Equal(0, reconciliation.RootElement.GetProperty("differenceValueByCurrency").EnumerateArray()
            .Single(item => item.GetProperty("currency").GetString() == "TRY")
            .GetProperty("differenceValue").GetInt64());
        Assert.Equal(2, await CountAdjustmentsAsync(atmId));
        Assert.Equal(3, await GetBalanceAsync(atmId, 1, Currency.TRY, BanknoteDenomination.FiveHundred));
        Assert.Equal(0, await GetBalanceAsync(atmId, 2, Currency.TRY, BanknoteDenomination.OneHundred));
        Assert.Equal("MAINTENANCE", await GetAtmStateAsync(atmId));
        await AssertLedgerConsistentAsync(atmId);

        using (var serviceAttempt = await SetStateAsync(atmId, AtmLifecycleState.InService))
        {
            Assert.Equal(HttpStatusCode.Conflict, serviceAttempt.StatusCode);
            Assert.Equal("reconciliation_approval_required", await ReadProblemCodeAsync(serviceAttempt));
        }

        using (var secondReconciliation = await Phase6TestSupport.PostReconciliationAsync(fixture, atmId, snapshot, "operator-one"))
        {
            Assert.Equal(HttpStatusCode.Conflict, secondReconciliation.StatusCode);
            Assert.Equal("reconciliation_approval_required", await ReadProblemCodeAsync(secondReconciliation));
        }
        Assert.Equal(1, await CountReconciliationsAsync(atmId));

        using (var selfApproval = await Phase6TestSupport.PostReconciliationApprovalAsync(
                   fixture, atmId, reconciliationId, " OPERATOR-ONE "))
        {
            Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
            Assert.Equal("reconciliation_self_approval", await ReadProblemCodeAsync(selfApproval));
        }

        var otherAtmId = await Phase6TestSupport.CreateAtmAsync(
            fixture,
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
            },
            resetDatabase: false);
        using (var wrongAtmApproval = await Phase6TestSupport.PostReconciliationApprovalAsync(
                   fixture, otherAtmId, reconciliationId, "operator-two"))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongAtmApproval.StatusCode);
            Assert.Equal("reconciliation_not_found", await ReadProblemCodeAsync(wrongAtmApproval));
        }

        var movementCountBeforeApproval = await CountAdjustmentsAsync(atmId);
        using var approvalResponse = await Phase6TestSupport.PostReconciliationApprovalAsync(
            fixture, atmId, reconciliationId, "  operator-two  ");
        using var approval = await Phase6TestSupport.ReadAsync(approvalResponse);
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        Assert.Equal("APPROVED", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal("operator-two", approval.RootElement.GetProperty("approvedBy").GetString());
        Assert.True(approval.RootElement.GetProperty("approvedAt").GetDateTimeOffset() > DateTimeOffset.MinValue);

        using (var repeatedApprovalResponse = await Phase6TestSupport.PostReconciliationApprovalAsync(
                   fixture, atmId, reconciliationId, "operator-three"))
        using (var repeatedApproval = await Phase6TestSupport.ReadAsync(repeatedApprovalResponse))
        {
            Assert.Equal(HttpStatusCode.OK, repeatedApprovalResponse.StatusCode);
            Assert.Equal("operator-two", repeatedApproval.RootElement.GetProperty("approvedBy").GetString());
            var approvedAtDifference = (approval.RootElement.GetProperty("approvedAt").GetDateTimeOffset() -
                                        repeatedApproval.RootElement.GetProperty("approvedAt").GetDateTimeOffset()).Duration();
            Assert.InRange(approvedAtDifference, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(movementCountBeforeApproval, await CountAdjustmentsAsync(atmId));
        Assert.Equal(3, await GetBalanceAsync(atmId, 1, Currency.TRY, BanknoteDenomination.FiveHundred));
        Assert.Equal(0, await GetBalanceAsync(atmId, 2, Currency.TRY, BanknoteDenomination.OneHundred));
        using var serviceAfterApproval = await SetStateAsync(atmId, AtmLifecycleState.InService);
        Assert.Equal(HttpStatusCode.OK, serviceAfterApproval.StatusCode);
        Assert.Equal("IN_SERVICE", await GetAtmStateAsync(atmId));
        await AssertLedgerConsistentAsync(atmId);
    }

    [Fact]
    public async Task Usd_and_eur_use_decimal_rates_and_existing_snapshot_does_not_change_after_rate_revision()
    {
        var atmId = await CreateMaintenanceAtmAsync(new Dictionary<int, BanknoteLoadRequest[]>
        {
            [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
        });
        var snapshot = Snapshot((1,
        [
            new(Currency.USD, BanknoteDenomination.Twenty, 1),
            new(Currency.EUR, BanknoteDenomination.Twenty, 1)
        ]), (2, [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]));

        using var firstResponse = await Phase6TestSupport.PostReconciliationAsync(fixture, atmId, snapshot, "operator-one");
        Assert.True(firstResponse.IsSuccessStatusCode, await firstResponse.Content.ReadAsStringAsync());
        using var first = await Phase6TestSupport.ReadAsync(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("AWAITING_APPROVAL", first.RootElement.GetProperty("status").GetString());
        var firstId = first.RootElement.GetProperty("reconciliationId").GetGuid();
        Assert.Equal(1460m, first.RootElement.GetProperty("totalAbsoluteDifferenceTry").GetDecimal());
        Assert.Equal(35m, first.RootElement.GetProperty("usdToTryRateSnapshot").GetDecimal());
        Assert.Equal(38m, first.RootElement.GetProperty("eurToTryRateSnapshot").GetDecimal());

        using var approved = await Phase6TestSupport.PostReconciliationApprovalAsync(fixture, atmId, firstId, "operator-two");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await ReviseRateAsync(Currency.USD, 50m);
        await ReviseRateAsync(Currency.EUR, 60m);

        using var secondResponse = await Phase6TestSupport.PostReconciliationAsync(fixture, atmId, snapshot, "operator-one");
        using var second = await Phase6TestSupport.ReadAsync(secondResponse);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("COMPLETED", second.RootElement.GetProperty("status").GetString());
        Assert.Equal(0m, second.RootElement.GetProperty("totalAbsoluteDifferenceTry").GetDecimal());
        Assert.Equal(50m, second.RootElement.GetProperty("usdToTryRateSnapshot").GetDecimal());
        Assert.Equal(60m, second.RootElement.GetProperty("eurToTryRateSnapshot").GetDecimal());

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var original = await db.CashReconciliations.AsNoTracking().SingleAsync(item => item.Id == firstId);
        Assert.Equal(1460m, original.TotalAbsoluteDifferenceTry);
        Assert.Equal(35m, original.UsdToTryRateSnapshot);
        Assert.Equal(38m, original.EurToTryRateSnapshot);
    }

    [Fact]
    public async Task Missing_x_user_and_missing_exchange_rate_are_rejected_without_creating_reconciliation()
    {
        var atmId = await CreateMaintenanceAtmAsync(new Dictionary<int, BanknoteLoadRequest[]>
        {
            [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 1)]
        });
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{atmId}/reconciliations")
               {
                   Content = JsonContent.Create(Snapshot(), options: Phase6TestSupport.JsonOptions)
               })
        using (var noUser = await fixture.Client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noUser.StatusCode);
            Assert.Equal("reconciliation_user_required", await ReadProblemCodeAsync(noUser));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
            var usdRate = await db.ExchangeRates.SingleAsync(rate => rate.BaseCurrency == Currency.USD && rate.QuoteCurrency == Currency.TRY);
            db.ExchangeRates.Remove(usdRate);
            await db.SaveChangesAsync();
        }

        using var missingRate = await Phase6TestSupport.PostReconciliationAsync(
            fixture, atmId, Snapshot(), "operator-one");
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, missingRate.StatusCode);
            Assert.Equal("exchange_rate_missing", await ReadProblemCodeAsync(missingRate));
        }
        finally
        {
            await using var restoreScope = fixture.Services.CreateAsyncScope();
            var db = restoreScope.ServiceProvider.GetRequiredService<AtmDbContext>();
            db.ExchangeRates.Add(new ExchangeRate(Currency.USD, Currency.TRY, 35m));
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, await CountReconciliationsAsync(atmId));
        Assert.Equal(0, await CountAdjustmentsAsync(atmId));
    }

    [Fact]
    public async Task Concurrent_approval_requests_are_safe_and_return_the_persisted_approval()
    {
        var atmId = await CreateMaintenanceAtmAsync(
            new Dictionary<int, BanknoteLoadRequest[]>
            {
                [2] = [new(Currency.TRY, BanknoteDenomination.OneHundred, 11)]
            });
        using var response = await Phase6TestSupport.PostReconciliationAsync(fixture, atmId, Snapshot((2, [])), "operator-one");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var result = await Phase6TestSupport.ReadAsync(response);
        var reconciliationId = result.RootElement.GetProperty("reconciliationId").GetGuid();
        Assert.Equal("AWAITING_APPROVAL", result.RootElement.GetProperty("status").GetString());
        var adjustmentsBefore = await CountAdjustmentsAsync(atmId);

        var approvals = await Phase6TestSupport.StartTogetherAsync(
            new[] { "operator-two", "operator-three" },
            user => Phase6TestSupport.PostReconciliationApprovalAsync(fixture, atmId, reconciliationId, user));
        try
        {
            Assert.All(approvals, item => Assert.Equal(HttpStatusCode.OK, item.StatusCode));
            var results = await Task.WhenAll(approvals.Select(async item =>
            {
                using (item)
                using (var document = await Phase6TestSupport.ReadAsync(item))
                {
                    return document.RootElement.GetProperty("approvedBy").GetString();
                }
            }));
            Assert.Single(results.Distinct());
            Assert.Contains(results[0], new[] { "operator-two", "operator-three" });
        }
        finally
        {
            foreach (var approval in approvals)
            {
                approval.Dispose();
            }
        }

        Assert.Equal(adjustmentsBefore, await CountAdjustmentsAsync(atmId));
        await AssertLedgerConsistentAsync(atmId);
    }

    private async Task<Guid> CreateMaintenanceAtmAsync(IReadOnlyDictionary<int, BanknoteLoadRequest[]> notes)
    {
        var atmId = await Phase6TestSupport.CreateAtmAsync(fixture, notes);
        using var response = await SetStateAsync(atmId, AtmLifecycleState.Maintenance);
        response.EnsureSuccessStatusCode();
        return atmId;
    }

    private static CashReconciliationRequest Snapshot(params (int Number, BanknoteLoadRequest[] Notes)[] overrides)
    {
        var byNumber = overrides.ToDictionary(entry => entry.Number, entry => entry.Notes);
        return new CashReconciliationRequest(Enumerable.Range(1, 5)
            .Select(number => new CassetteCountSnapshotRequest(
                number,
                byNumber.TryGetValue(number, out var notes) ? notes : Array.Empty<BanknoteLoadRequest>()))
            .ToArray());
    }

    private Task<HttpResponseMessage> SetStateAsync(Guid atmId, AtmLifecycleState state) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/state",
            new SetAtmStateRequest(state),
            Phase6TestSupport.JsonOptions);

    private async Task<string> GetAtmStateAsync(Guid atmId)
    {
        using var response = await fixture.Client.GetAsync($"/api/atms/{atmId}/balances");
        response.EnsureSuccessStatusCode();
        using var document = await Phase6TestSupport.ReadAsync(response);
        return document.RootElement.GetProperty("state").GetString()!;
    }

    private async Task<int> CountAdjustmentsAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CassetteMovements.CountAsync(movement =>
            movement.AtmId == atmId && movement.Type == CassetteMovementType.ReconciliationAdjustment);
    }

    private async Task<int> CountReconciliationsAsync(Guid atmId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CashReconciliations.CountAsync(reconciliation => reconciliation.AtmId == atmId);
    }

    private async Task<int> GetBalanceAsync(
        Guid atmId,
        int cassetteNumber,
        Currency currency,
        BanknoteDenomination denomination)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.CassetteBalances
            .Where(balance => balance.Cassette.AtmId == atmId &&
                              balance.Cassette.Number == cassetteNumber &&
                              balance.Currency == currency && balance.Denomination == denomination)
            .Select(balance => balance.PhysicalCount)
            .SingleAsync();
    }

    private async Task ReviseRateAsync(Currency currency, decimal rate)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var exchangeRate = await db.ExchangeRates.SingleAsync(item =>
            item.BaseCurrency == currency && item.QuoteCurrency == Currency.TRY);
        exchangeRate.ReviseRate(rate);
        await db.SaveChangesAsync();
    }

    private async Task AssertLedgerConsistentAsync(Guid atmId)
    {
        using var consistency = await Phase6TestSupport.GetConsistencyAsync(fixture, atmId);
        Assert.True(consistency.RootElement.GetProperty("isConsistent").GetBoolean(),
            string.Join(Environment.NewLine, consistency.RootElement.GetProperty("inconsistencies")
                .EnumerateArray().Select(item => item.GetString())));
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = await Phase6TestSupport.ReadAsync(response);
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
