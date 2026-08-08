using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Retail25.Application.Documents;

namespace Retail25.Infrastructure.Documents;

/// <summary>
/// Price tags and barcode labels, laid out on real label stock (guide App. L).
/// </summary>
public sealed class QuestPdfLabelRenderer : ILabelRenderer
{
    /// <summary>PDF points per inch. Label stock is specified in inches; PDFs are drawn in points.</summary>
    private const float PointsPerInch = 72f;

    static QuestPdfLabelRenderer() => QuestPdfLicence.Accept();

    public byte[] RenderPriceTags(LabelSheetRequest request)
        => Render(request, barcodeFirst: false);

    public byte[] RenderBarcodeLabels(LabelSheetRequest request)
        => Render(request, barcodeFirst: true);

    private static byte[] Render(LabelSheetRequest request, bool barcodeFirst)
        => BuildSheet(request, barcodeFirst).GeneratePdf();

    /// <summary>
    /// The composed document, before it becomes bytes. Exposed so the sheet can also be rendered as
    /// images — which is the only way to actually look at a label layout rather than assert on the
    /// size of the file it produced.
    /// </summary>
    public static IDocument BuildSheet(LabelSheetRequest request, bool barcodeFirst)
    {
        ArgumentNullException.ThrowIfNull(request);

        var layout = AveryLayouts.For(request.Stock);
        var cells = BuildCells(request, layout);

        return Document.Create(container =>
        {
            foreach (var sheet in cells.Chunk(layout.PerSheet))
            {
                container.Page(page =>
                {
                    page.Size(layout.PageWidth * PointsPerInch, layout.PageHeight * PointsPerInch, Unit.Point);
                    page.MarginLeft(layout.MarginLeft * PointsPerInch);
                    page.MarginTop(layout.MarginTop * PointsPerInch);
                    page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial));

                    page.Content().Column(column =>
                    {
                        column.Spacing(layout.GapVertical * PointsPerInch);

                        foreach (var row in sheet.Chunk(layout.Columns))
                        {
                            column.Item().Row(rowContainer =>
                            {
                                rowContainer.Spacing(layout.GapHorizontal * PointsPerInch);

                                for (var index = 0; index < layout.Columns; index++)
                                {
                                    var tag = index < row.Length ? row[index] : null;

                                    rowContainer
                                        .ConstantItem(layout.LabelWidth * PointsPerInch)
                                        .Height(layout.LabelHeight * PointsPerInch)
                                        .Padding(4)
                                        .Element(cell =>
                                        {
                                            if (tag is null)
                                            {
                                                // A blank keeps the grid aligned on a part-filled row.
                                                cell.Text(string.Empty);
                                                return;
                                            }

                                            DrawLabel(cell, tag, layout, request.ShowBarcode, barcodeFirst);
                                        });
                                }
                            });
                        }
                    });
                });
            }
        });
    }

    /// <summary>
    /// Expands copies into individual cells, and pads the front of the run when the operator is
    /// re-using a part-used sheet — printing over labels that have already been peeled off is the
    /// fastest way to waste the rest of the sheet.
    /// </summary>
    public static List<PriceTag?> BuildCells(LabelSheetRequest request, LabelLayout layout)
    {
        var cells = new List<PriceTag?>();

        for (var skipped = 0; skipped < Math.Max(0, request.SkipLabels); skipped++)
        {
            cells.Add(null);
        }

        foreach (var line in request.Lines)
        {
            for (var copy = 0; copy < Math.Max(1, line.Copies); copy++)
            {
                cells.Add(line.Tag);
            }
        }

        // Pad the last sheet so the final row still lays out as a grid.
        var remainder = cells.Count % layout.PerSheet;

        if (remainder != 0)
        {
            for (var pad = remainder; pad < layout.PerSheet; pad++)
            {
                cells.Add(null);
            }
        }

        return cells;
    }

    private static void DrawLabel(IContainer cell, PriceTag tag, LabelLayout layout, bool showBarcode, bool barcodeFirst)
    {
        var big = layout.LabelHeight >= 2f;

        // An unencodable code prints as a tag without a barcode rather than as a barcode that will
        // not scan — a shelf tag with no barcode is a minor inconvenience, a bad one is a queue.
        BarcodePattern? barcode = null;

        if (showBarcode && Code39Renderer.TryEncode(tag.Barcode ?? tag.StockCode, out var pattern))
        {
            barcode = pattern;
        }

        cell.Column(column =>
        {
            column.Spacing(1);

            if (barcodeFirst && barcode is not null)
            {
                // Taller bars on the big stock: those are bin and shelf-edge labels, read at arm's
                // length or further, where bar height is what gives the scanner something to aim at.
                column.Item().Height(big ? 70 : 22).Element(c => DrawBars(c, barcode));
                column.Item().Text(barcode.Text).FontSize(big ? 9 : 6).AlignCenter();
            }

            column.Item().Text(tag.Name)
                .FontSize(big ? 13 : 8)
                .SemiBold()
                .ClampLines(big ? 2 : 1);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text(tag.StockCode).FontSize(big ? 9 : 6).Light();

                row.AutoItem().Text(tag.Price.ToString("C", System.Globalization.CultureInfo.CurrentCulture))
                    .FontSize(big ? 18 : 11)
                    .Bold();
            });

            if (!string.IsNullOrWhiteSpace(tag.BinLocation))
            {
                column.Item().Text($"Bin {tag.BinLocation}").FontSize(big ? 8 : 5).Light();
            }

            if (!barcodeFirst && barcode is not null)
            {
                column.Item().Height(big ? 56 : 16).Element(c => DrawBars(c, barcode));
            }
        });
    }

    /// <summary>
    /// Draws the bar pattern as vector rectangles. Runs of identical modules are merged into one
    /// rectangle so a long bar is a single shape rather than a stack of hairlines that a renderer
    /// can leave seams between.
    /// </summary>
    private static void DrawBars(IContainer container, BarcodePattern pattern)
    {
        container.Row(row =>
        {
            var index = 0;

            while (index < pattern.Width)
            {
                var isBar = pattern.Modules[index];
                var run = 1;

                while (index + run < pattern.Width && pattern.Modules[index + run] == isBar)
                {
                    run++;
                }

                var segment = row.RelativeItem(run);

                if (isBar)
                {
                    segment.Background(Colors.Black);
                }

                index += run;
            }
        });
    }
}
