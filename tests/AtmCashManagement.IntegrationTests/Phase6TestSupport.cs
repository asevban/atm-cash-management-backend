using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtmCashManagement.Api.Application.Contracts;
using AtmCashManagement.Api.Domain.Enums;

namespace AtmCashManagement.IntegrationTests;

internal static class Phase6TestSupport
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static async Task<Guid> CreateAtmAsync(
        AtmApiFixture fixture,
        IReadOnlyDictionary<int, BanknoteLoadRequest[]> notes,
        ConfigureAtmRequest? configuration = null,
        bool resetDatabase = true)
    {
        if (resetDatabase)
        {
            await fixture.ResetDatabaseAsync();
        }

        using var create = await fixture.Client.PostAsJsonAsync(
            "/api/atms",
            new CreateAtmRequest($"PHASE6-{Guid.NewGuid():N}"),
            JsonOptions);
        create.EnsureSuccessStatusCode();
        using var created = await ReadAsync(create);
        var atmId = created.RootElement.GetProperty("id").GetGuid();

        configuration ??= DefaultConfiguration();
        using var configure = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/configure",
            configuration,
            JsonOptions);
        configure.EnsureSuccessStatusCode();

        var cassetteLoads = Enumerable.Range(1, 5)
            .Select(number => new CassetteLoadRequest(
                number,
                notes.TryGetValue(number, out var cassetteNotes) ? cassetteNotes : Array.Empty<BanknoteLoadRequest>()))
            .ToArray();
        using var load = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/load",
            new LoadCashRequest(cassetteLoads),
            JsonOptions);
        load.EnsureSuccessStatusCode();

        using var setState = await fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/state",
            new SetAtmStateRequest(AtmLifecycleState.InService),
            JsonOptions);
        setState.EnsureSuccessStatusCode();
        return atmId;
    }

    internal static ConfigureAtmRequest DefaultConfiguration(bool duplicateTryHundred = false) =>
        new(
        [
            new CassetteDefinitionRequest(1, CassetteType.DepositOnly),
            new CassetteDefinitionRequest(2, CassetteType.Recycling, Currency.TRY, BanknoteDenomination.OneHundred),
            new CassetteDefinitionRequest(3, CassetteType.Recycling, Currency.TRY,
                duplicateTryHundred ? BanknoteDenomination.OneHundred : BanknoteDenomination.Fifty),
            new CassetteDefinitionRequest(4, CassetteType.Recycling, Currency.USD, BanknoteDenomination.Twenty),
            new CassetteDefinitionRequest(5, CassetteType.Recycling, Currency.EUR, BanknoteDenomination.OneHundred)
        ]);

    internal static Task<HttpResponseMessage> PostQuoteAsync(
        AtmApiFixture fixture,
        Guid atmId,
        string cardNumber,
        Currency currency,
        int amount) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/atms/{atmId}/withdrawal-quotes",
            new WithdrawalQuoteRequest(cardNumber, currency, amount),
            JsonOptions);

    internal static async Task<HttpResponseMessage> PostConfirmAsync(
        AtmApiFixture fixture,
        Guid quoteId,
        Guid optionId,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/withdrawal-quotes/{quoteId}/confirm")
        {
            Content = JsonContent.Create(new ConfirmWithdrawalRequest(optionId), options: JsonOptions)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await fixture.Client.SendAsync(request);
    }

    internal static Task<HttpResponseMessage> PostCancelAsync(AtmApiFixture fixture, Guid quoteId) =>
        fixture.Client.PostAsync($"/api/withdrawal-quotes/{quoteId}/cancel", content: null);

    internal static async Task<HttpResponseMessage> PostReconciliationAsync(
        AtmApiFixture fixture,
        Guid atmId,
        CashReconciliationRequest reconciliationRequest,
        string user = "phase6-reconciliation-operator")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/atms/{atmId}/reconciliations")
        {
            Content = JsonContent.Create(reconciliationRequest, options: JsonOptions)
        };
        request.Headers.Add("X-User", user);
        return await fixture.Client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> PostReconciliationApprovalAsync(
        AtmApiFixture fixture,
        Guid atmId,
        Guid reconciliationId,
        string user)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/atms/{atmId}/reconciliations/{reconciliationId}/approve");
        request.Headers.Add("X-User", user);
        return await fixture.Client.SendAsync(request);
    }

    internal static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    internal static async Task<JsonDocument> GetConsistencyAsync(AtmApiFixture fixture, Guid atmId)
    {
        using var response = await fixture.Client.GetAsync($"/api/atms/{atmId}/ledger/consistency");
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    internal static async Task<HttpResponseMessage[]> StartTogetherAsync<T>(
        IReadOnlyList<T> requests,
        Func<T, Task<HttpResponseMessage>> send)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests.Select(async request =>
        {
            await start.Task;
            return await send(request);
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
