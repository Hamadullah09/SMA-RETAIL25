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
    /// An identity for a new cart.
    ///
    /// <para>
    /// Deliberately not a <see cref="SequenceKind"/>. Those are the numbers a shop administers and
    /// sees — invoices, transactions, purchase orders — and each one appears in the Numbering screen
    /// with a start value somebody can set. A cart id is a handle the till uses for a few minutes
    /// and never prints; putting it on that screen would invite an operator to change something with
    /// no business meaning and break every open basket.
    /// </para>
    /// <para>
    /// Drawn from the database rather than invented in the process because two app instances must
    /// never hand out the same one: carts are keyed by it in a shared store, and a collision would
    /// put one till's items into another till's sale.
    /// </para>
    /// </summary>
    Task<long> NextCartIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Repoints a live sequence — what an administrator's edit to a "next number" has to do to take
    /// effect. Saving the row alone would not: the sequence was created from that row the first time
    /// it was used and never looks at it again.
    /// </summary>
    Task RestartAsync(SequenceKind kind, long locationId, long nextNumber, CancellationToken ct = default);
}
