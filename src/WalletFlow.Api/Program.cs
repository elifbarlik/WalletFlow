using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog.Formatting.Compact;
using WalletFlow.Api.Controllers;
using WalletFlow.Api.Infrastructure;
using WalletFlow.Application;
using WalletFlow.Application.Abstractions;
using WalletFlow.Infrastructure;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Logging: structured, JSON in production, correlation id + user id enrichment -------------------------------
builder.Host.UseSerilog((context, services, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext();
    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        logger.WriteTo.Console(new CompactJsonFormatter());
    }

    if (context.Configuration["Seq:Url"] is { Length: > 0 } seq)
    {
        logger.WriteTo.Seq(seq);
    }
});

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 64 * 1024;
});

// ---- Application & infrastructure ------------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---- OpenTelemetry: opt-in, enabled when Otel:Endpoint is set (OTLP/HTTP base URL, e.g. http://jaeger:4318) ---------
if (config["Otel:Endpoint"] is { Length: > 0 } otlp)
{
    var baseUrl = otlp.TrimEnd('/');
    var otel = builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("walletflow-api"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql")
            .AddOtlpExporter(o => { o.Endpoint = new Uri($"{baseUrl}/v1/traces"); o.Protocol = OtlpExportProtocol.HttpProtobuf; }));

    // Jaeger accepts traces only; enable metrics export when pointing at a full OpenTelemetry Collector.
    if (config.GetValue<bool>("Otel:ExportMetrics"))
    {
        otel.WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(o => { o.Endpoint = new Uri($"{baseUrl}/v1/metrics"); o.Protocol = OtlpExportProtocol.HttpProtobuf; }));
    }
}
// ---- Web API -------------------------------------------------------------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    ctx.ProblemDetails.Instance = null;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = false;
    o.ReportApiVersions = true;
}).AddMvc().AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});
builder.Services.AddApiRateLimiting(config);

// ---- Authentication / authorization: secure by default (everything needs a token unless [AllowAnonymous]) ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role",
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthorizationPolicies.AdminOnly, p => p.RequireRole("Admin"))
    .AddPolicy(AuthorizationPolicies.AuditorRead, p => p.RequireRole("Admin", "Auditor"));

// ---- CORS: deny by default, allow only configured origins -------------------------------------------------------------
var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (origins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).WithMethods("GET", "POST", "PATCH").WithHeaders("Authorization", "Content-Type", "Idempotency-Key")));
}

// ---- Reverse-proxy support (opt-in, so the client IP used for rate limiting cannot be spoofed) ----------------------
if (config.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// ---- Health checks -------------------------------------------------------------------------------------------------------
builder.Services.AddHttpClient("payment-health");
builder.Services.AddHealthChecks()
    .AddNpgSql(config.GetConnectionString("Postgres")!, name: "postgres", tags: ["ready"])
    .AddCheck<PaymentProviderHealthCheck>("payment-provider", tags: ["ready"]);
if (!string.IsNullOrWhiteSpace(config.GetConnectionString("Redis")))
{
    builder.Services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
}

// ---- OpenAPI -----------------------------------------------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "WalletFlow API",
        Version = "v1",
        Description = "Digital wallet and money-transfer API (fictional data). Authenticate via /auth/login, then use the Authorize button.",
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "WalletFlow.Api.xml");
    if (File.Exists(xml))
    {
        o.IncludeXmlComments(xml);
    }

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the access token only; the Bearer prefix is added automatically.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
});

var app = builder.Build();

if (config.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();
// Request logging wraps the exception handler so it records the final status (e.g. 402), not a raw exception.
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diagnostics, http) =>
    diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value ?? "anonymous"));
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (config.GetValue<bool>("Security:UseHttps"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}


app.UseStatusCodePages();

if (config.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "WalletFlow API v1"));
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status200OK },
}).AllowAnonymous().DisableRateLimiting();

if (config.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().MigrateWithLockAsync();
}

await DbSeeder.SeedAsync(app.Services);
await app.RunAsync();

/// <summary>Entry point marker so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;







