using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AtmCashManagement.Api.Controllers;

[ApiController]
public sealed class WithdrawalQuotesController(WithdrawalQuoteService service) : ControllerBase
{
    [HttpPost("api/atms/{id:guid}/withdrawal-quotes")]
    [ProducesResponseType<WithdrawalQuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WithdrawalQuoteResponse>> Create(
        Guid id,
        [FromBody] WithdrawalQuoteRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateQuoteAsync(id, request, cancellationToken));

    [HttpPost("api/withdrawal-quotes/{quoteId:guid}/confirm")]
    [ProducesResponseType<WithdrawalConfirmationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WithdrawalConfirmationResponse>> Confirm(
        Guid quoteId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] ConfirmWithdrawalRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.ConfirmAsync(quoteId, request, idempotencyKey, cancellationToken));

    [HttpPost("api/withdrawal-quotes/{quoteId:guid}/cancel")]
    [ProducesResponseType<WithdrawalQuoteStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WithdrawalQuoteStatusResponse>> Cancel(
        Guid quoteId,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(quoteId, cancellationToken));
}
