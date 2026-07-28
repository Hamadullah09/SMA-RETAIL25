using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Services;

/// <summary>
/// The real clock. Always UTC — the business date is derived per location from its own time zone
/// and day-start, so nothing downstream should ever need a local server time.
/// </summary>
public sealed class SystemDateTime : IDateTime
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
