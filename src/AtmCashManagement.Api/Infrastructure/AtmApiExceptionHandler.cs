using AtmCashManagement.Api.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AtmCashManagement.Api.Infrastructure;

public sealed class AtmApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AtmApiException apiException)
        {
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = apiException.StatusCode,
            Title = apiException.Title,
            Detail = apiException.Message,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["errorCode"] = apiException.ErrorCode;
        httpContext.Response.StatusCode = apiException.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
