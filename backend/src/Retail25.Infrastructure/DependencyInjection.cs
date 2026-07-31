using Hangfire;
using Hangfire.PostgreSql;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Accounting;
using Retail25.Infrastructure.Accounting;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Retail25.Infrastructure.Caching;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Jobs;
using Retail25.Infrastructure.Persistence;
using Retail25.Infrastructure.Realtime;
using Retail25.Infrastructure.Services;
using StackExchange.Redis;

namespace Retail25.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();
        services.AddSingleton<IDateTime, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql =>
                    {
                        npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                        npgsql.EnableRetryOnFailure(3);
                    })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>()));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();

        // Identity, OpenIddict and the permission resolver. Registered here rather than in the API
        // so a background job or an integration test gets the same authorisation model.
        services.AddIdentityAndOpenIddict(configuration);
        services.AddScoped<IdentitySeeder>();

        // Innermost behaviour: the transaction wraps the handler and nothing else.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        AddRedis(services, configuration);

        services.AddScoped<IPosNotifier, PosNotifier>();
        services.AddScoped<ITerminalNotifier, TerminalNotifier>();

        // Q1: the simulator ships first, and the real processor is a registration change here.
        services.AddScoped<IPaymentGateway, SimulatorPaymentGateway>();

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<LateChargeAccrualJob>();

        // The CSV adapter is the one that is always available. A provider integration (QuickBooks,
        // Xero) would be registered here instead per deployment — the port is what the application
        // layer depends on, so swapping it is a registration change and nothing else (doc 09 §1).
        services.AddScoped<IAccountingConnector, CsvExportConnector>();
        services.AddScoped<PostPosRevenueToAccountingJob>();

        // The QuestPDF licence is accepted by the renderers themselves (QuestPdfLicence) so any path
        // that builds one — including a test — gets a working renderer, not one that throws on the
        // first PDF.
        services.AddSingleton<ILabelRenderer, QuestPdfLabelRenderer>();
        services.AddSingleton<IDocumentRenderer, QuestPdfDocumentRenderer>();

        AddHangfire(services, configuration);

        return services;
    }

    /// <summary>
    /// The store for the late-charge recurring job (doc: <c>LateChargePolicy</c>, "applied by a
    /// nightly Hangfire job"). Same Postgres database as everything else — a second datastore for one
    /// job's bookkeeping would be a second thing that can be down.
    /// </summary>
    private static void AddHangfire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection"))));

        services.AddHangfireServer();
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);

            // A till must start even if Redis is briefly away; the multiplexer reconnects on its own,
            // and failing fast here would take the whole API down with it.
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddScoped<ICartStore, RedisCartStore>();
        services.AddScoped<ITagDebouncer, RedisTagDebouncer>();
        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddScoped<IHubTicketStore, RedisHubTicketStore>();
    }
}
