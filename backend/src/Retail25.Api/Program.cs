using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retail25.Api;
using Retail25.Api.Realtime;
using Retail25.Api.Startup;
using Retail25.Application;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure;
using Retail25.Infrastructure.Persistence.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Logging -------------------------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

builder.Host.UseSerilog();

// --- Application services ------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IdentitySeeder>();

// SignalR broadcasting lives in this project because the hubs do. Application only ever sees
// IPosNotifier, so it never learns that SignalR exists.
builder.Services.AddScoped<IPosNotifier, SignalRPosNotifier>();

// --- Authentication ------------------------------------------------------------------------
// The browser session is an httpOnly cookie: nothing a script can read, so an injected script has
// no token to steal. Machine-to-machine clients get OpenIddict with PKCE in a later phase.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "retail25.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;

        // An API answers with a status code. Redirecting to a login page would hand the caller
        // an HTML document where it expected JSON.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// --- Realtime ------------------------------------------------------------------------------
var signalR = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();

    // A bulk RFID read can deliver hundreds of tags at once.
    options.MaximumReceiveMessageSize = 512 * 1024;
});

// With more than one API instance, hubs need a backplane or a message reaches only the clients
// connected to the instance that raised it.
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection);
}

// --- Observability -------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Retail25.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Retail25.Api")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

// --- Health --------------------------------------------------------------------------------
// "Live" answers "is the process up"; "ready" answers "can it serve traffic". Conflating them
// makes an orchestrator restart a healthy process whose database is briefly unreachable.
var health = builder.Services.AddHealthChecks();

var postgres = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrWhiteSpace(postgres))
{
    health.AddNpgSql(postgres, name: "postgresql", tags: ["ready"]);
}

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    health.AddRedis(redisConnection, name: "redis", tags: ["ready"]);
}

// --- API -----------------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Retail25 API", Version = "v1" }));

builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddResponseCompression();

var app = builder.Build();

// --- Database ------------------------------------------------------------------------------
await DatabaseInitializer.InitializeAsync(app);

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
}

// --- Pipeline ------------------------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new()
{
    // Liveness must not touch a dependency, or a slow database looks like a dead process.
    Predicate = _ => false,
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapControllers();
app.MapRetail25Hubs();

await app.RunAsync();

/// <summary>Exposed so the integration tests can host the API with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
