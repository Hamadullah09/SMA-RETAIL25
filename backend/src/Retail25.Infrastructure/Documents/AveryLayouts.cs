using Retail25.Application.Documents;

namespace Retail25.Infrastructure.Documents;

/// <summary>
/// The geometry of one sheet of label stock, in inches.
/// <para>
/// Every measurement is the vendor's published figure. This is the one place in the system where
/// being a millimetre out costs a whole sheet of labels, so the numbers live together where they can
/// be checked against a physical sheet rather than scattered through a template.
/// </para>
/// </summary>
public sealed record LabelLayout(
    LabelStock Stock,
    string DisplayName,
    int Columns,
    int Rows,
    float LabelWidth,
    float LabelHeight,
    float MarginLeft,
    float MarginTop,
    float GapHorizontal,
    float GapVertical,
    float PageWidth = 8.5f,
    float PageHeight = 11f)
{
    public int PerSheet => Columns * Rows;
}

public static class AveryLayouts
{
    /// <summary>
    /// Avery 5160 — 30 to a sheet. The 0.125" between columns is what stops the text of one label
    /// bleeding onto the next when a printer feeds a fraction crooked.
    /// </summary>
    public static readonly LabelLayout Avery5160 = new(
        LabelStock.Avery5160, "Avery 5160 — 30 per sheet",
        Columns: 3, Rows: 10,
        LabelWidth: 2.625f, LabelHeight: 1f,
        MarginLeft: 0.1875f, MarginTop: 0.5f,
        GapHorizontal: 0.125f, GapVertical: 0f);

    /// <summary>Avery 8160 — the inkjet twin of 5160, identical geometry.</summary>
    public static readonly LabelLayout Avery8160 = Avery5160 with
    {
        Stock = LabelStock.Avery8160,
        DisplayName = "Avery 8160 — 30 per sheet",
    };

    /// <summary>Avery 8163 — 10 to a sheet, big enough for a shelf-edge label.</summary>
    public static readonly LabelLayout Avery8163 = new(
        LabelStock.Avery8163, "Avery 8163 — 10 per sheet",
        Columns: 2, Rows: 5,
        LabelWidth: 4f, LabelHeight: 2f,
        // 5/32", written as the fraction rather than Avery's rounded 0.1563 — at two columns of 4"
        // the rounding is enough to push the right-hand column past the edge of the sheet.
        MarginLeft: 0.15625f, MarginTop: 0.5f,
        GapHorizontal: 0.1875f, GapVertical: 0f);

    /// <summary>
    /// S-644N — 6 to a sheet at 4" × 3", shipping stock.
    /// <para>
    /// The least certain of the four: this is a distributor part number rather than an Avery one, so
    /// print one sheet against a real label before running a batch.
    /// </para>
    /// </summary>
    public static readonly LabelLayout S644N = new(
        LabelStock.S644N, "S-644N — 6 per sheet (verify against stock)",
        Columns: 2, Rows: 3,
        LabelWidth: 4f, LabelHeight: 3f,
        MarginLeft: 0.1875f, MarginTop: 0.5f,
        GapHorizontal: 0.125f, GapVertical: 0f);

    public static LabelLayout For(LabelStock stock) => stock switch
    {
        LabelStock.Avery5160 => Avery5160,
        LabelStock.Avery8160 => Avery8160,
        LabelStock.Avery8163 => Avery8163,
        LabelStock.S644N => S644N,
        _ => Avery5160,
    };

    public static IReadOnlyList<LabelLayout> All { get; } = [Avery5160, Avery8160, Avery8163, S644N];
}
