namespace Retail25.Application.Abstractions;

/// <summary>
/// Abstraction over the current time. Production uses DateTimeOffset.UtcNow; tests can
/// substitute a fixed clock for deterministic pricing/tax calculations.
/// </summary>
public interface IDateTime
{
    DateTimeOffset Now { get; }

    DateOnly Today() => DateOnly.FromDateTime(Now.DateTime);
}
