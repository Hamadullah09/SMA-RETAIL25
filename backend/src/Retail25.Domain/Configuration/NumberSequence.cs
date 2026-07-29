using Retail25.Domain.Common;

namespace Retail25.Domain.Configuration;

/// <summary>The documents and records that carry a human-facing number.</summary>
public enum SequenceKind
{
    Customer = 0,
    Supplier = 1,
    Product = 2,
    Invoice = 3,
    PurchaseOrder = 4,
    Transaction = 5,
    StockCount = 6,
    Transfer = 7,
}

/// <summary>
/// The legacy "next number" settings (guide p.76), kept as administrable rows.
/// <para>
/// Two things make this worth its own entity. A migrated store must continue its existing numbering —
/// customer 4,182 has to be followed by 4,183, not by 1, because staff and paper records refer to
/// those numbers. And the numbering shape is a business preference: prefixes, padding and starting
/// points differ per store, and none of that belongs in code.
/// </para>
/// <para>
/// This is <b>not</b> how transaction numbers are allocated at the till. Those come from a Postgres
/// sequence, because two stations completing a sale in the same millisecond must not collide — which
/// is exactly what the legacy per-workstation counter did. This row records the <i>starting point</i>
/// and the display format; the sequence enforces uniqueness.
/// </para>
/// </summary>
public sealed class NumberSequence : Entity, IAuditable
{
    public static readonly Error NextNumberInvalid = new("sequence.next_invalid", "The next number cannot be negative.");
    public static readonly Error WouldGoBackwards = new("sequence.would_go_backwards", "The next number cannot be set below a number already issued.");

    public NumberSequence()
    {
    }

    public Guid LocationId { get; set; }

    public SequenceKind Kind { get; set; }

    /// <summary>Printed before the number, e.g. <c>INV-</c>. Empty for a bare number.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Zero-padding width. Six gives <c>000042</c>, which keeps printed lists aligned.</summary>
    public int PadWidth { get; set; }

    /// <summary>The next value to hand out. Set from the legacy system's own counter at migration.</summary>
    public long NextNumber { get; set; } = 1;

    /// <summary>The highest number actually issued. Guards an administrator against reusing one.</summary>
    public long HighWaterMark { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }

    public static NumberSequence Create(Guid locationId, SequenceKind kind, long nextNumber = 1, string prefix = "", int padWidth = 0)
        => new()
        {
            LocationId = locationId,
            Kind = kind,
            NextNumber = nextNumber,
            Prefix = prefix,
            PadWidth = padWidth,
        };

    /// <summary>Hands out the next number and advances. Callers persist inside their own transaction.</summary>
    public long Take()
    {
        var value = NextNumber;
        NextNumber = value + 1;

        if (value > HighWaterMark)
        {
            HighWaterMark = value;
        }

        return value;
    }

    /// <summary>Renders a number the way this store expects to see it printed.</summary>
    public string Format(long number)
        => Prefix + (PadWidth > 0
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(PadWidth, '0')
            : number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Repoints the counter — the operation a migration performs, and the one an administrator
    /// occasionally needs. Moving it below a number already issued is refused, because duplicate
    /// invoice numbers are the kind of mistake that surfaces months later at an audit.
    /// </summary>
    public Result SetNext(long nextNumber)
    {
        if (nextNumber < 0)
        {
            return Result.Failure(NextNumberInvalid.With("value", nextNumber));
        }

        if (nextNumber <= HighWaterMark)
        {
            return Result.Failure(WouldGoBackwards.With("value", nextNumber).With("issued", HighWaterMark));
        }

        NextNumber = nextNumber;
        return Result.Success();
    }

    public void SetFormat(string prefix, int padWidth)
    {
        Prefix = prefix?.Trim() ?? string.Empty;
        PadWidth = Math.Clamp(padWidth, 0, 12);
    }

    /// <summary>Every kind a store needs on day one, all starting at 1.</summary>
    public static IReadOnlyList<NumberSequence> SeedDefaults(Guid locationId)
        => Enum.GetValues<SequenceKind>()
            .Select(kind => Create(locationId, kind, 1, DefaultPrefix(kind), DefaultPad(kind)))
            .ToList();

    private static string DefaultPrefix(SequenceKind kind) => kind switch
    {
        SequenceKind.Invoice => "INV-",
        SequenceKind.PurchaseOrder => "PO-",
        SequenceKind.StockCount => "SC-",
        SequenceKind.Transfer => "TR-",
        _ => string.Empty,
    };

    private static int DefaultPad(SequenceKind kind) => kind switch
    {
        SequenceKind.Customer or SequenceKind.Supplier => 0,
        _ => 6,
    };
}
