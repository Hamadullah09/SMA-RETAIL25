using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retail25.Api.Common;
using Retail25.Application;
using Retail25.Infrastructure;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Jobs;
using Retail25.Infrastructure.Persistence;
using Retail25.Infrastructure.Realtime;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog ---
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();

builder.Host.UseSerilog();

// --- Services ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- OpenTelemetry ---
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

// --- Health checks ---
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis",
        tags: ["ready"]);

// --- API ---
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums cross the wire as names. A till that receives PriceOrigin "Break" can badge the line
        // without a lookup table that would drift from the server's numbering.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Retail25 API", Version = "v1" }));

// --- Realtime ---
var signalR = builder.Services
    .AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment())
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The Redis backplane is configured from day one so scaling out is a deployment change rather than
// a rewrite of how carts are broadcast.
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(
        redisConnection,
        options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("retail25"));
}

// --- CORS ---
// Exactly one origin: the BFF. Every browser call goes through it, so nothing else has any reason
// to reach the API cross-origin (doc 07 §Hardening).
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// --- Authentication cookie ---
// Only the sign-in page uses it; the API itself is bearer-only. It is httpOnly and SameSite=Lax so
// it survives the redirect back from the authorization endpoint without being sendable cross-site.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    // __Host- requires Secure unconditionally — a browser silently drops the cookie if the name
    // carries the prefix but SecurePolicy resolves to non-Secure, which SameAsRequest does over
    // this project's own documented plain-HTTP dev flow. Without this split, sign-in looked like
    // it succeeded (PasswordSignInAsync 302) but the cookie never landed, so /connect/authorize
    // never saw the user as signed in and bounced straight back to the login page.
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "r25.identity" : "__Host-r25.identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(options =>
{
    // __Host- requires Secure, and ASP.NET Core's antiforgery middleware throws outright if
    // SecurePolicy=Always is set on a non-HTTPS request — it has no localhost exception the way
    // browsers do. So the prefix itself, not just the policy, has to follow environment: this
    // project's own documented dev flow runs the API on plain http://localhost (OpenIddict:
    // AllowInsecureHttp above), where __Host- can never be satisfied. Same split the Identity
    // cookie above uses, applied to the cookie name as well as SecurePolicy.
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "r25.antiforgery" : "__Host-r25.antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// --- Rate limiting ---
// The endpoints worth guessing at: token exchange, PIN verification and identifier lookup. Without
// a limit, a four-digit PIN on a machine sitting in a shop is guessable in an afternoon.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 20;
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("pin", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("lookup", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 300;
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddResponseCompression();

// Model-binding failures answer with the same problem shape as domain errors, so a client has one
// error contract to handle rather than two.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "validation.failed",
        };

        problem.Extensions["code"] = "validation.failed";
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});

var app = builder.Build();

// --- Database bootstrap (development and staging only) ---
if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Migrate, never EnsureCreated. EnsureCreated builds the schema from the model but writes no
    // __EFMigrationsHistory row, so a database created that way can never be migrated afterwards —
    // the only route to a later schema change would be dropping it and losing the data. That is not
    // a trade a shop can make, and the failure only shows up at the first upgrade.
    await db.Database.MigrateAsync();

    if (builder.Configuration.GetValue<bool>("Database:Seed"))
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

        // Identity seeding follows the store seed: the administrator's staff profile needs a
        // location to belong to.
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    }
}

// --- Middleware pipeline ---
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

// Redeems a hub ticket into a principal before authentication runs, and only for hub paths.
app.UseMiddleware<HubTicketMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapControllers().RequireRateLimiting("lookup");

app.MapHub<PosHub>("/hubs/pos");
app.MapHub<InventoryHub>("/hubs/inventory");
app.MapHub<TerminalHub>("/hubs/terminal");

// Nightly late-charge accrual (LateChargePolicy: "applied by a nightly Hangfire job"). 2am local —
// after the day's trading has closed everywhere this deployment plausibly serves, before the next.
// Resolved from DI rather than the static RecurringJob helper: AddHangfire only wires JobStorage
// into the container, it never sets the static JobStorage.Current the static API depends on.
using (var scope = app.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    recurring.AddOrUpdate<LateChargeAccrualJob>(
        "late-charge-accrual",
        job => job.RunAsync(CancellationToken.None),
        "0 2 * * *");

    // An hour after the late charges, so a day's books are settled before its takings post.
    recurring.AddOrUpdate<PostPosRevenueToAccountingJob>(
        "post-pos-revenue",
        job => job.RunAsync(CancellationToken.None),
        "0 3 * * *");
}

await app.RunAsync();

/// <summary>Exposed so <c>WebApplicationFactory</c> can boot the API in integration tests.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
