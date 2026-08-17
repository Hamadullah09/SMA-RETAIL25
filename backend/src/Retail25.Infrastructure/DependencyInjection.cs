using Hangfire;
using Hangfire.SqlServer;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Retail25.Infrastructure.Rfid;
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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddHttpContextAccessor();
        services.AddSingleton<IDateTime, SystemClock>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        // Which kind of backup this deployment can actually take.
        //
        // `Native` is BACKUP DATABASE, which is the better tool when the database is on a machine
        // you control: it captures the whole database, indexes and all. It cannot work when the
        // database is somewhere else — the file lands on the SQL Server's disk, where the
        // application cannot see it, list it or offer it for download — which is the shape of every
        // shared-hosting plan, including this one.
        //
        // `Portable` therefore is the default: it reads the data through the connection the
        // application already has, so it works anywhere the app runs at all. Configuration decides,
        // because which one is right is a fact about the deployment and not about the code.
        if (string.Equals(configuration["Backup:Mode"], "Native", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDatabaseBackupService, Retail25.Infrastructure.Maintenance.SqlServerDatabaseBackupService>();
        }
        else
        {
            services.AddScoped<IDatabaseBackupService, Retail25.Infrastructure.Maintenance.PortableDatabaseBackupService>();
            services.AddScoped<Retail25.Infrastructure.Maintenance.PortableDatabaseBackupService>();
        }
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
        services.AddIdentityAndOpenIddict(configuration, environment);
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IAccountNotifier, SmtpAccountNotifier>();

        // Innermost behaviour: the transaction wraps the handler and nothing else.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        AddCacheStores(services, configuration, environment);

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
        AddServerReaders(services, configuration);

        return services;
    }

    /// <summary>
    /// Lets this process hold the RFID reader connections instead of a per-till agent.
    /// <para>
    /// Registered always, enabled by configuration, and off by default. The host does nothing at all
    /// when disabled, so a deployment that uses terminal agents pays nothing for this being present.
    /// </para>
    /// </summary>
    private static void AddServerReaders(IServiceCollection services, IConfiguration configuration)
    {
        // Which counters the phone app may connect to, and whether it may bring new ones into service.
        services.Configure<Application.Trolleys.TrolleyOptions>(
            configuration.GetSection(Application.Trolleys.TrolleyOptions.Section));

        services.Configure<ServerReaderOptions>(configuration.GetSection(ServerReaderOptions.Section));

        services.AddSingleton<ServerReaderStatus>();
        services.AddSingleton<IReaderConnectionStatus>(sp => sp.GetRequiredService<ServerReaderStatus>());

        services.AddHostedService<ServerReaderHost>();
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

        // Whether this process also *works* the queue, as opposed to only scheduling onto it.
        //
        // Storage is always registered: the schedule lives in SQL Server, so any instance can enqueue
        // and any instance can read what ran. The worker is separate because where it belongs depends
        // on where this is deployed, and that is not a code decision.
        //
        // On shared IIS hosting the worker is close to useless and quietly so. The pool is recycled on
        // a schedule and unloaded when idle, and a background loop dies with it — so a nightly 2am
        // accrual runs only if somebody happens to be using the site at 2am. Turning the worker off
        // there and driving the job from something that is actually awake (the host's scheduler, or a
        // pinger against the trigger endpoint) is honest; leaving it on is a job that appears
        // scheduled, reports no error, and does not run.
        if (configuration.GetValue("Jobs:RunServer", defaultValue: true))
        {
            services.AddHangfireServer();
        }
    }

    /// <summary>
    /// Where cart state, tag claims, idempotency records and hub tickets live.
    /// <para>
    /// Three providers, and the choice is really about what infrastructure the deployment has.
    /// <c>Redis</c> is the default and the right answer when there is a Redis to point at.
    /// <c>SqlServer</c> is for hosting that offers a database and nothing else — shared IIS, in
    /// particular — and keeps every guarantee the Redis version makes, at the cost of a round trip
    /// to the database on paths that used to hit memory. <c>InMemory</c> is for development and is
    /// refused in Production.
    /// </para>
    /// </summary>
    private static void AddCacheStores(IServiceCollection services, IConfiguration configuration, IHostEnvironment host)
    {
        var provider = configuration["Cache:Provider"];

        // An explicit opt-out, never an automatic failover.
        //
        // Falling back on a failed connection would be the dangerous design: a shop whose Redis
        // blipped for ten seconds would silently lose cross-till tag arbitration and could sell the
        // same garment twice, with nothing on any screen to say so. Losing that protection has to be
        // something someone chose, in a config file, on purpose.
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            AddInMemoryStores(services, configuration, host);
            return;
        }

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            AddSqlServerStores(services);
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
    /// Holds cart state, tag claims, idempotency and hub tickets in the application's own database.
    /// <para>
    /// Scoped, like the Redis stores and unlike the in-memory ones: these borrow
    /// <see cref="ApplicationDbContext"/>'s connection so that clearing a cart and writing the sale
    /// that emptied it land in the same transaction. State outlives the request because it is in a
    /// table, so nothing here needs to be a singleton.
    /// </para>
    /// <para>
    /// The sweeper is the exception. It holds the timestamp of the last expiry pass, which is
    /// per-process bookkeeping rather than per-request, and a scoped one would sweep on every
    /// single write.
    /// </para>
    /// <para>
    /// Permitted in Production, unlike the in-memory stores, and for the reason that matters: two
    /// instances reading the same tables arbitrate against each other correctly. The tag claim is a
    /// primary key and the hub ticket redemption is a single statement, so scaling out changes
    /// throughput and not behaviour.
    /// </para>
    /// </summary>
    private static void AddSqlServerStores(IServiceCollection services)
    {
        services.AddSingleton<CacheSweeper>();

        services.AddScoped<ICartStore, SqlCartStore>();
        services.AddScoped<ITagDebouncer, SqlTagDebouncer>();
        services.AddScoped<IIdempotencyStore, SqlIdempotencyStore>();
        services.AddScoped<IHubTicketStore, SqlHubTicketStore>();
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
    private static void AddInMemoryStores(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment host)
    {
        // Asked of the host rather than read from a configuration key.
        //
        // The key is only populated when the environment arrives as ASPNETCORE_ENVIRONMENT. Set it
        // any of the other supported ways — DOTNET_ENVIRONMENT, --environment on the command line,
        // a launch profile — and this check silently found nothing and allowed the in-memory stores
        // through. In a single-process shop that is survivable. Behind a load balancer it is not:
        // each instance holds its own carts and its own tag claims, so two tills can be told the
        // same garment is theirs and both sell it, with nothing on any screen to say so.
        if (host.IsProduction())
        {
            throw new InvalidOperationException(
                "Cache:Provider is InMemory, which is not permitted in Production. "
                + InMemoryStoreNotes.Caveat
                + " Set Cache:Provider to Redis with ConnectionStrings:Redis, or to SqlServer to "
                + "use the application's own database — which is the answer on hosting that offers "
                + "no Redis.");
        }

        services.AddSingleton<ICartStore, InMemoryCartStore>();
        services.AddSingleton<ITagDebouncer, InMemoryTagDebouncer>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IHubTicketStore, InMemoryHubTicketStore>();

        // Resolved at startup purely so the warning is logged once, where an operator will see it.
        services.AddSingleton<InMemoryStoreWarning>();
    }
}
