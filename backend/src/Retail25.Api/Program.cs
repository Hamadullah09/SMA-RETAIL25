using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retail25.Api.Common;
using Retail25.Application;
using Retail25.Infrastructure;
using Retail25.Infrastructure.Caching;
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
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

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

// Where cart state, tag claims and hub tickets live. Read once here because three separate things
// below depend on the answer: the health check, the SignalR backplane, and the store registrations
// in AddInfrastructure.
//
// Redis is the default when nothing is configured, which is why this asks whether the provider is
// *not* one of the others rather than whether it is Redis. Naming SqlServer here matters: it is a
// provider that shares state across instances like Redis does, but has no Redis to probe or to
// carry a backplane, and treating it as "InMemory-like" would skip the backplane it does not need
// while treating it as "Redis-like" would health-check a server that is not there.
var cacheProvider = builder.Configuration["Cache:Provider"];

var usesRedis =
    !string.Equals(cacheProvider, "InMemory", StringComparison.OrdinalIgnoreCase) &&
    !string.Equals(cacheProvider, "SqlServer", StringComparison.OrdinalIgnoreCase);

// --- Health checks ---
var health = builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver",
        tags: ["ready"]);

// Not probed when nothing uses it. A red "redis" check on a bench that deliberately has no Redis
// trains people to ignore the health endpoint, which costs more than the check is worth.
if (usesRedis)
{
    health.AddRedis(
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
        name: "redis",
        tags: ["ready"]);
}

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
//
// There is no equivalent under Cache:Provider=SqlServer, and that is the one guarantee that
// provider does not carry over. The stores work across instances — the tag claim is a primary key,
// the ticket redemption is one statement — but a hub message published on instance A does not reach
// a till connected to instance B, so a second cashier's screen would not update. SqlServer is
// therefore a single-instance deployment. Running more than one means Redis.
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (usesRedis && !string.IsNullOrWhiteSpace(redisConnection))
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

    // Pinned to the origin root, because `__Host-` demands it.
    //
    // The prefix's contract is Secure, no Domain, and Path=/ — a browser that receives a
    // `__Host-` cookie failing any of those discards it silently. ASP.NET Core defaults a cookie's
    // path to the application's PathBase, which is `/backend` where this runs as a sub-application,
    // so the cookie was being thrown away by every browser and nothing said so. It surfaced as
    // sign-in reporting "That form had expired" on a form that was seconds old, because the
    // antiforgery cookie beside this one was discarded for the same reason.
    //
    // The cost is that the front end's own process now also receives these on requests to the
    // origin. They are httpOnly, so no script can read them, and the API authenticates its own
    // endpoints by bearer token — this cookie only ever means "signed in at the interactive page".
    // Worth it to keep the prefix: this account serves several sibling subdomains, and `__Host-` is
    // exactly what stops one of them setting a Domain-wide cookie that shadows this name.
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // Send a browser to the sign-in page, always.
    //
    // The stock handler decides between redirecting and answering 401-with-a-Location based on
    // whether the request looks like a background call. When it picks the second, a browser is
    // handed a status it will not follow and a Location it therefore ignores: /connect/authorize
    // renders as a bare "HTTP ERROR 401" and sign-in cannot start. That is what this deployment
    // did, and the redirect target in the header was correct the whole time.
    //
    // There is nothing to preserve the other branch for. This cookie authenticates exactly one
    // thing — the interactive sign-in page — because every API call arrives as a bearer token on
    // the OpenIddict validation scheme (see the default authorization policy). No caller of this
    // scheme is an XHR that would rather read a 401 than follow a redirect.
    options.Events ??= new CookieAuthenticationEvents();

    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    // Same reasoning: a signed-in user who lacks the rights for a page should see the page that
    // says so, not a status code with no body.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
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

    // Same reason as the identity cookie above: `__Host-` requires Path=/, and the default is the
    // application's PathBase. This is the cookie whose loss produced the visible symptom — the
    // token in the form had nothing to be checked against, so every sign-in was rejected as stale.
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// --- Rate limiting ---
// The endpoints worth guessing at: token exchange, PIN verification and identifier lookup. Without
// a limit, a four-digit PIN on a machine sitting in a shop is guessable in an afternoon.
//
// Every policy is partitioned per caller — by user id once signed in, by client IP before that. A
// single shared window would make the limit a ceiling on the whole shop rather than on the one
// misbehaving caller: at 300 requests a minute shared, thirty tills starve each other long before
// any of them is abusive.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string Caller(HttpContext http)
        => http.User.Identity?.IsAuthenticated == true
            ? http.User.Identity.Name ?? "authenticated"
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static RateLimitPartition<string> PerCaller(HttpContext http, string policy, int permitLimit)
        => RateLimitPartition.GetFixedWindowLimiter(
            $"{policy}:{Caller(http)}",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = permitLimit,
                QueueLimit = 0,
            });

    options.AddPolicy("auth", http => PerCaller(http, "auth", 20));
    options.AddPolicy("pin", http => PerCaller(http, "pin", 10));
    options.AddPolicy("lookup", http => PerCaller(http, "lookup", 300));
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

// Resolved for its side effect: if this build is running without Redis, say so once, loudly, at
// startup. What that costs — no cross-till tag arbitration — is not something to find out later.
app.Services.GetService<InMemoryStoreWarning>();

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

        // The demonstration catalogue is last and self-gating on Demo:SeedCatalogue. It needs the
        // location from the store seed, and it must never run against a shop's own inventory.
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
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

// The read feed. Listen-only for clients: tags enter the system through the agent's channel above,
// never from a browser.
app.MapHub<RfidHub>("/hubs/rfid");

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
