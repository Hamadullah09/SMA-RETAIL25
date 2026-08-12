using Retail25.Domain.Configuration;

namespace Retail25.Application.Abstractions;

/// <summary>
/// Gap-free-enough document numbers drawn from database sequences.
/// <para>
/// Two stations completing a sale in the same millisecond must not produce the same transaction
/// number, and a number must never be derived from a clock — the legacy system's "next number"
/// settings were a per-workstation counter and did collide. A Postgres sequence per location is the
/// smallest thing that is actually correct here.
/// </para>
/// <para>
/// The administered <see cref="NumberSequence"/> row supplies each sequence's <i>starting point</i>
/// and its printed shape; the sequence itself enforces uniqueness. That split is what lets a migrated
/// store carry on from customer 4,182 without two clerks both being handed 4,183.
/// </para>
/// </summary>
public interface ISequenceGenerator
{
    /// <summary>Next sale number for a location. Sequence name: <c>seq_transaction_{locationId}</c>.</summary>
    Task<long> NextTransactionNumberAsync(long locationId, CancellationToken ct = default);

    Task<long> NextInvoiceNumberAsync(long locationId, CancellationToken ct = default);

    /// <summary>Next number of any administered kind, seeded from that kind's configured start.</summary>
    Task<long> NextAsync(SequenceKind kind, long locationId, CancellationToken ct = default);


    /// <summary>
    /// Repoints a live sequence — what an administrator's edit to a "next number" has to do to take
    /// effect. Saving the row alone would not: the sequence was created from that row the first time
    /// it was used and never looks at it again.
    /// </summary>
    Task RestartAsync(SequenceKind kind, long locationId, long nextNumber, CancellationToken ct = default);
}
