using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Services;
using RetailPlus.Server;

// The SMA RETAIL server: the central database (SQL Server, the same model as the tills), the tills' sync packages,
// sign-in for the web back office, stores and tills.
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
builder.Services.Configure<ServerOptions>(configuration.GetSection("Server"));
builder.Services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
builder.Services.Configure<BootstrapOptions>(configuration.GetSection("Bootstrap"));

// The server works for no station: every row it adds is given its store explicitly.
builder.Services.AddSingleton(provider => new DataContext(DatabaseConnection.SqlServer(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("RetailPlus") is { Length: > 0 } connection
        ? connection : RetailPlusDb.LocalDbConnectionString("RetailPlus_Server"))));
builder.Services.AddSingleton<ServerFolders>();
builder.Services.AddSingleton<SigningKey>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<StationKeys>();
builder.Services.AddSingleton<ServerSync>();
builder.Services.AddSingleton<ServerStartup>();
// Finance (phase E1): the jobs that post business days, recurring expenses and the FBR queue. FBR is off unless configured
// (Fbr:Enabled, PosId, Token, Url); Fbr:UseFake records what would be sent and sends nothing.
builder.Services.Configure<FinanceOptions>(configuration.GetSection("Finance"));
builder.Services.Configure<FbrOptions>(configuration.GetSection("Fbr"));
builder.Services.AddHttpClient(nameof(FbrInvoiceSender));
builder.Services.AddSingleton<IFiscalInvoiceSender>(provider =>
{
    var fbr = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FbrOptions>>().Value;
    return fbr.UseFake ? new FakeFiscalInvoiceSender { Enabled = fbr.Enabled, Stores = fbr.Stores }
        : new FbrInvoiceSender(provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FbrOptions>>());
});
builder.Services.AddSingleton<FinanceJobs>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<FinanceJobs>());
// WhatsApp: Meta's Cloud API when switched on (WhatsApp:Enabled), else a stand-in that records; and the campaign sender.
builder.Services.AddWhatsApp(configuration);
// What one web request works on (its user, stores and services); see Api.cs.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(provider => new ApiRequest(
    provider.GetRequiredService<IHttpContextAccessor>().HttpContext ?? throw new InvalidOperationException("An API request needs an HTTP request."),
    provider.GetRequiredService<DataContext>()));
// The supplier bills the three-way match reads (Endpoints.Supply.cs): the payables module's posted bills (phase E1).
builder.Services.AddScoped<IBillLineSource>(provider => new SupplierBillLineSource(provider.GetRequiredService<DataContext>()));

// Station keys are encrypted with data-protection keys kept in the data folder (encrypted with DPAPI on Windows).
builder.Services.AddDataProtection().SetApplicationName("SMA RETAIL Server");
builder.Services.AddOptions<KeyManagementOptions>().Configure<ServerFolders, ILoggerFactory>((options, folders, logging) =>
{
    options.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(Path.Combine(folders.Data, "keys")), logging);
    if (OperatingSystem.IsWindows()) options.XmlEncryptor = new DpapiXmlEncryptor(protectToLocalMachine: true, logging);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<TokenService>((options, tokens) =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = tokens.Validation;
    options.Events = new JwtBearerEvents
    {
        // The web app keeps no token: it is signed in by the rp_session cookie, read when there is no Authorization header.
        OnMessageReceived = context =>
        {
            if (string.IsNullOrEmpty(context.Request.Headers.Authorization) && context.Request.Cookies.TryGetValue(Session.Cookie, out var token)
                && !string.IsNullOrEmpty(token))
                context.Token = token;
            return Task.CompletedTask;
        },
        OnTokenValidated = TokenService.RefreshAsync,
    };
});
// One policy per permission key: an endpoint names the permission it needs.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(ServerClaims.Policy(permission.Key), policy => policy.RequireAuthenticatedUser().RequireClaim(ServerClaims.Permission, permission.Key));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(configuration.GetSection("Cors:Origins").Get<string[]>() is { Length: > 0 } origins ? origins : ["http://localhost:3000"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { UtcJson.MarkUtc } };
});
builder.Services.AddOpenApi();

var app = builder.Build();
await app.Services.GetRequiredService<ServerStartup>().RunAsync();

app.UseMiddleware<ApiErrors>();
// The web back office's files come before routing, so a page is never taken for an endpoint.
app.UseWebApp();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CsrfGuard>();
app.UseAuthorization();
app.MapOpenApi();

app.MapRetailPlusApi();
await app.RunAsync();

/// <summary>The server program (public for the integration tests).</summary>
public partial class Program;
