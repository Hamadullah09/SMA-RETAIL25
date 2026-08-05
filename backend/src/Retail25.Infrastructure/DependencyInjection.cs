using Hangfire;
using Hangfire.SqlServer;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Accounting;
using Retail25.Infrastructure.Accounting;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Retail25.Application.Maintenance;
using Retail25.Application.Migration;
using Retail25.Application.Rfid;
using Retail25.Infrastructure.LegacyData;
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
        services.AddSingleton<IDatabaseBackupService, Retail25.Infrastructure.Maintenance.SqlServerDatabaseBackupService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditingInterceptor>();

        // snake_case is kept on SQL Server, where it is not the house style. The column names are a
        // published interface by now — the schema reference, the reporting views a store's
        // accountant writes, the external system that wanted numeric ids — and renaming ninety
        // tables' worth of columns to earn a convention nobody outside the database can see is a
        // migration with all of the risk and none of the benefit.
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
            options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sqlServer =>
                    {
                        sqlServer.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                        sqlServer.EnableRetryOnFailure(3);

                        // The SQL Server provider batches 42 statements by default; Npgsql batched
                        // 1000. Nothing about the application changed in the move between them, but
                        // a twenty-thousand-row legacy import went from seconds to over the command
                        // timeout — twenty-four times the round trips, on the one operation in the
                        // system that is all round trips.
                        //
                        // 200 rather than higher because SQL Server caps a command at 2100
                        // parameters and EF splits a batch that would exceed it, so past a point
                        // this number stops meaning anything for wide rows while still applying to
                        // narrow ones. Staging rows are narrow, and they are the ones that come in
                        // twenty thousand at a time.
                        sqlServer.MaxBatchSize(200);
                    })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>()));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();

        // The keyring behind every cookie, antiforgery token and OpenIddict token this system issues.
        //
        // Both calls matter and for different reasons. PersistKeysToDbContext puts the keys somewhere
        // every replica can read and every restart can find; the default is a folder under the
        // starting user's profile, which a container does not have and two replicas do not share, so
        // keys are effectively regenerated on each start and everything issued before it stops
        // decrypting. SetApplicationName fixes the isolation discriminator, which otherwise derives
        // from the content-root path — so the same deployment unpacked to a different directory, or
        // run from the SDK rather than a published folder, silently cannot read its own keys.
        //
        // The symptom of either is identical and misleading: sessions end at every restart, and a
        // login page fetched moments earlier comes back "that form had expired".
        services
            .AddDataProtection()
            .SetApplicationName("Retail25")
            .PersistKeysToDbContext<ApplicationDbContext>();

        // Identity, OpenIddict and the permission resolver. Registered here rather than in the API
        // so a background job or an integration test gets the same authorisation model.
        services.AddIdentityAndOpenIddict(configuration);
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IAccountNotifier, SmtpAccountNotifier>();

        // Innermost behaviour: the transaction wraps the handler and nothing else.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        AddRedis(services, configuration);

        services.AddScoped<IPosNotifier, PosNotifier>();
        services.AddScoped<ITerminalNotifier, TerminalNotifier>();
        services.AddScoped<IRfidNotifier, RfidNotifier>();

        // The registry is a singleton and the publisher is not, on purpose: the debounce window and
        // the EPC cache have to outlive a request, while the publisher needs the request's DbContext.
        services.AddSingleton<TagStreamRegistry>();
        services.AddScoped<TagObservationPublisher>();

        // Q1: the simulator ships first, and the real processor is a registration change here.
        services.AddScoped<IPaymentGateway, SimulatorPaymentGateway>();

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<LateChargeAccrualJob>();

        // The CSV adapter is the one that is always available. A provider integration (QuickBooks,
        // Xero) would be registered here instead per deployment — the port is what the application
        // layer depends on, so swapping it is a registration change and nothing else (doc 09 §1).
        services.AddScoped<IAccountingConnector, CsvExportConnector>();
        services.AddScoped<PostPosRevenueToAccountingJob>();

        // The legacy migration toolchain. The reader is stateless over bytes; the importer needs a
        // unit of work, so it is scoped like everything else that writes.
        services.AddSingleton<ILegacySourceReader, LegacySourceReader>();
        services.AddScoped<ILegacyImporter, LegacyImporter>();

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
    /// nightly Hangfire job"). Same SQL Server database as everything else — a second datastore for one
    /// job's bookkeeping would be a second thing that can be down.
    /// </summary>
    private static void AddHangfire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(configuration.GetConnectionString("DefaultConnection")));

        services.AddHangfireServer();
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        // An explicit opt-out, never an automatic failover.
        //
        // Falling back on a failed connection would be the dangerous design: a shop whose Redis
        // blipped for ten seconds would silently lose cross-till tag arbitration and could sell the
        // same garment twice, with nothing on any screen to say so. Losing that protection has to be
        // something someone chose, in a config file, on purpose.
        if (string.Equals(configuration["Cache:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            AddInMemoryStores(services, configuration);
            return;
        }

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

    /// <summary>
    /// Holds cart state, tag claims, idempotency and hub tickets in this process instead of Redis.
    /// <para>
    /// Singletons, not scoped: the whole point of these is to outlive a request. A scoped in-memory
    /// cart store would forget the cart between two calls of the same sale, which is a subtler and
    /// far more confusing failure than having no store at all.
    /// </para>
    /// <para>
    /// Refused in Production. The trade this makes — no arbitration between tills — is invisible
    /// until a stock count weeks later says an item sold twice, and that is not a discovery anyone
    /// should make from a config line they inherited.
    /// </para>
    /// </summary>
    private static void AddInMemoryStores(IServiceCollection services, IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cache:Provider is InMemory, which is not permitted in Production. "
                + InMemoryStoreNotes.Caveat
                + " Configure ConnectionStrings:Redis instead.");
        }

        services.AddSingleton<ICartStore, InMemoryCartStore>();
        services.AddSingleton<ITagDebouncer, InMemoryTagDebouncer>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IHubTicketStore, InMemoryHubTicketStore>();

        // Resolved at startup purely so the warning is logged once, where an operator will see it.
        services.AddSingleton<InMemoryStoreWarning>();
    }
}
