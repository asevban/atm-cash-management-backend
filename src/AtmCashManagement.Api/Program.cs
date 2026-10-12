using System.Text.Json.Serialization;
using AtmCashManagement.Api.Infrastructure;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid request",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        problem.Extensions["errorCode"] = "bad_request";
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "ATM Cash Management API", Version = "v1" }));
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions.TryAdd("errorCode", context.ProblemDetails.Status switch
        {
            400 => "bad_request",
            404 => "not_found",
            >= 500 => "internal_error",
            _ => $"http_{context.ProblemDetails.Status ?? 500}"
        });
    };
});
builder.Services.AddExceptionHandler<AtmApiExceptionHandler>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.ApplyDatabaseMigrationsAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// TLS is expected to terminate at the local development server or deployment proxy.
// The container listens on HTTP port 8080 and intentionally does not redirect to HTTPS.
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
