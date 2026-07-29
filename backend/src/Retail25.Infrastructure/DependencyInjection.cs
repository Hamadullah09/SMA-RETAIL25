using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Infrastructure.Caching;
using Retail25.Infrastructure.Identity;
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

        return services;
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
