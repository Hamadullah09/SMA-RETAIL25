using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Retail25.Application")]
[assembly: InternalsVisibleTo("Retail25.Infrastructure")]
[assembly: InternalsVisibleTo("Retail25.Api")]
[assembly: InternalsVisibleTo("Retail25.Domain.UnitTests")]
[assembly: InternalsVisibleTo("Retail25.ArchitectureTests")]

// Entity.Id has an internal setter, so a test that needs an entity with a *known* id has to be
// inside the fence. The cache-store suite does: it stores carts by id and then asks for them back,
// which proves nothing if every cart it builds has id 0.
[assembly: InternalsVisibleTo("Retail25.IntegrationTests")]
