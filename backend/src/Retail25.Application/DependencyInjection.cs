using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Carts.Services;
using Retail25.Application.Receipts;
using Retail25.Application.Receivables;

namespace Retail25.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters and runs outermost to innermost (doc 05). Authorisation precedes
            // validation so a request the actor may not make is refused before its contents are
            // inspected; idempotency precedes the transaction so a replayed key never opens one.
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<PosContextLoader>();
        services.AddScoped<CartPricingService>();
        services.AddScoped<CartWorkflow>();
        services.AddScoped<CartOpener>();
        services.AddScoped<Shoppers.Services.ShopperSessionFactory>();
        services.AddScoped<Trolleys.Services.TrolleyAllocator>();
        services.AddScoped<Rfid.Services.RfidCheckout>();
        services.AddScoped<Rfid.Services.TagObservationRouter>();
        services.AddScoped<IdentifierResolver>();
        services.AddScoped<CartLineFactory>();
        services.AddScoped<ReceiptBuilder>();

        // Resolvable by its own concrete type, not just through ISender/IRequestHandler<> — the
        // nightly late-charge job calls it directly, with no HTTP request or authenticated user
        // behind it for the authorization pipeline behaviour to check (doc: LateChargePolicy,
        // "applied by a nightly Hangfire job").
        services.AddScoped<ReceivablesHandlers>();

        return services;
    }
}
