namespace AtmCashManagement.Api.Application;

public sealed class AtmApiException(
    int statusCode,
    string errorCode,
    string title,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public string Title { get; } = title;

    public static AtmApiException Validation(string errorCode, string message) =>
        new(StatusCodes.Status400BadRequest, errorCode, "Invalid request", message);

    public static AtmApiException NotFound(Guid id) =>
        new(StatusCodes.Status404NotFound, "atm_not_found", "ATM not found", $"ATM '{id}' was not found.");

    public static AtmApiException Conflict(string errorCode, string message) =>
        new(StatusCodes.Status409Conflict, errorCode, "Operation conflict", message);
}
