using AtmCashManagement.Api.Application;
using AtmCashManagement.Api.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AtmCashManagement.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(OperationsReportService service) : ControllerBase
{
    [HttpGet("operations")]
    [ProducesResponseType<OperationsReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OperationsReportResponse>> GetOperations(
        [FromQuery] Guid? atmId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(atmId, cancellationToken));
}
