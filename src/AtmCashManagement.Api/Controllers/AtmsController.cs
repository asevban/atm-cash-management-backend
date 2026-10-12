using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AtmCashManagement.Api.Controllers;

[ApiController]
[Route("api/atms")]
public sealed class AtmsController(
    AtmManagementService service,
    CassetteLedgerConsistencyService ledgerConsistencyService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<AtmResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AtmResponse>> Create(
        [FromBody] CreateAtmRequest request,
        CancellationToken cancellationToken)
    {
        var atm = await service.CreateAtmAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = atm.Id }, atm);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AtmResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AtmResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await service.GetAtmsAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AtmResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AtmResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAtmAsync(id, cancellationToken));

    [HttpPost("{id:guid}/configure")]
    [ProducesResponseType<AtmResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AtmResponse>> Configure(
        Guid id,
        [FromBody] ConfigureAtmRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ConfigureAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/load")]
    [ProducesResponseType<AtmResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AtmResponse>> Load(
        Guid id,
        [FromBody] LoadCashRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.LoadCashAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deposits")]
    [ProducesResponseType<CashDepositResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashDepositResponse>> Deposit(
        Guid id,
        [FromBody] CashDepositRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.DepositAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/reconciliations")]
    [ProducesResponseType<CashReconciliationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashReconciliationResponse>> Reconcile(
        Guid id,
        [FromBody] CashReconciliationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReconcileAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/state")]
    [ProducesResponseType<AtmResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AtmResponse>> SetState(
        Guid id,
        [FromBody] SetAtmStateRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.SetStateAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/balances")]
    [ProducesResponseType<AtmBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AtmBalanceResponse>> GetBalances(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetBalancesAsync(id, cancellationToken));

    [HttpGet("{id:guid}/ledger/consistency")]
    [ProducesResponseType<LedgerConsistencyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LedgerConsistencyResponse>> GetLedgerConsistency(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await ledgerConsistencyService.CheckAsync(id, cancellationToken));
}
