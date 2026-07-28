using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Carts;
using Retail25.Infrastructure.Identity;
using Retail25.Infrastructure.Persistence;
using Retail25.Infrastructure.Persistence.Seeding;
using Retail25.Infrastructure.Services;
using StackExchange.Redis;

namespace Retail25.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsql.EnableRetryOnFailure(3);
                })
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 10;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IDateTime, SystemDateTime>();

        AddCartStore(services, configuration);

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<ReferenceDataSeeder>();

        return services;
    }

    /// <summary>
    /// Chooses where carts live.
    /// <para>
    /// Redis is the deployment default: it is what lets a suspended sale be recalled at a different
    /// till, and what lets more than one API instance serve the same store. When no Redis connection
    /// is configured the store falls back to process memory so a developer can press F5 and sell
    /// something with only a database running — at the cost of the multi-station behaviour, which is
    /// why the fallback is a deliberate, visible choice rather than a silent default.
    /// </para>
    /// </summary>
    private static void AddCartStore(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<ICartStore, InMemoryCartStore>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);

            // The till must start even if Redis is briefly unavailable; the multiplexer reconnects.
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        var idleMinutes = configuration.GetValue("Carts:IdleExpiryMinutes", 720);

        services.AddSingleton<ICartStore>(provider => new RedisCartStore(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            TimeSpan.FromMinutes(idleMinutes)));
    }
}
