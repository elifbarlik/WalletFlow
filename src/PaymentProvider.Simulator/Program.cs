using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

// Fictional external payment provider used to demonstrate resilient integration.
// Behaviour: fails randomly (Simulator:FailureRate), declines the token "tok_declined", always errors on "tok_error",
// and deduplicates by Idempotency-Key exactly like a real provider would.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var apiKey = builder.Configuration["Simulator:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 16)
{
    throw new InvalidOperationException("Simulator:ApiKey must be configured (min 16 characters).");
}

var failureRate = builder.Configuration.GetValue("Simulator:FailureRate", 0.1);
var app = builder.Build();
var charges = new ConcurrentDictionary<string, (int Status, object Body)>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/v1/charges", async (HttpContext http, ChargeRequest request) =>
{
    var provided = http.Request.Headers["X-Api-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey)))
    {
        return Results.Json(new { error = "invalid_api_key" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
    {
        return Results.Json(new { error = "idempotency_key_required" }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (request.Amount <= 0 || request.Currency?.Length != 3 || string.IsNullOrWhiteSpace(request.Source))
    {
        return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (charges.TryGetValue(idempotencyKey, out var previous))
    {
        return Results.Json(previous.Body, statusCode: previous.Status);
    }

    await Task.Delay(Random.Shared.Next(40, 250));

    if (request.Source == "tok_error" || Random.Shared.NextDouble() < failureRate)
    {
        return Results.Json(new { error = "provider_error" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    (int Status, object Body) result = request.Source == "tok_declined"
        ? (StatusCodes.Status402PaymentRequired, new { error = "card_declined" })
        : (StatusCodes.Status201Created, new { id = $"ch_{Guid.NewGuid():N}", status = "succeeded" });

    charges[idempotencyKey] = result;
    return Results.Json(result.Body, statusCode: result.Status);
});

app.Run();

internal sealed record ChargeRequest(decimal Amount, string? Currency, string? Source, string? Description);
