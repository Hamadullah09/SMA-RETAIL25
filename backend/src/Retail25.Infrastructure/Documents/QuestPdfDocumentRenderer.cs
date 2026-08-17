using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Retail25.Application.Documents;

namespace Retail25.Infrastructure.Documents;

/// <summary>Envelopes and the price list (guide App. L).</summary>
public sealed class QuestPdfDocumentRenderer : IDocumentRenderer
{
    private const float PointsPerInch = 72f;

    static QuestPdfDocumentRenderer() => QuestPdfLicence.Accept();

    /// <summary>A #10 envelope is 9.5" × 4.125", fed long edge first.</summary>
    private const float EnvelopeWidth = 9.5f;
    private const float EnvelopeHeight = 4.125f;

    /// <summary>
    /// Where the recipient block starts, measured from the top-left of the envelope.
    /// <para>
    /// A standard #10 window is 4.5" × 1.125", set 7/8" in from the left and 1/2" up from the bottom
    /// — so it spans 2.5" to 3.625" down the page. These are chosen to sit inside that with room for
    /// the longest block we emit (company, name, two address lines, city line) and still clear the
    /// window's edges. Envelope stock varies; print one and hold it up to the light before a run.
    /// </para>
    /// </summary>
    private const float RecipientLeft = 0.875f;
    private const float RecipientTop = 2.6f;

    private const float ReturnLeft = 0.4f;
    private const float ReturnTop = 0.35f;

    public byte[] RenderCom10Envelope(EnvelopeRequest request) => BuildEnvelope(request).GeneratePdf();

    public byte[] RenderReceipt(Retail25.Contracts.Terminals.ReceiptDocument document)
        => QuestPdfReceiptRenderer.Render(document);

    /// <summary>
    /// The composed envelope, before it becomes bytes. Exposed so it can also be rendered as an
    /// image — the address block's position relative to the window is not something a byte count
    /// can tell you anything about.
    /// </summary>
    public static IDocument BuildEnvelope(EnvelopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(EnvelopeWidth * PointsPerInch, EnvelopeHeight * PointsPerInch, Unit.Point);
                page.Margin(0);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial).FontSize(10));

                // Two layers rather than a stacked column, so each block's position is measured from
                // the top of the envelope and nothing else. Stacked, the recipient block's offset
                // depended on how many lines the return address happened to have — a store with no
                // street line on file shifted every address up out of the window.
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer()
                        .PaddingLeft(ReturnLeft * PointsPerInch)
                        .PaddingTop(ReturnTop * PointsPerInch)
                        .Column(from =>
                        {
                            from.Item().Text(request.StoreName).FontSize(9).SemiBold();

                            if (!string.IsNullOrWhiteSpace(request.StoreLine1))
                            {
                                from.Item().Text(request.StoreLine1).FontSize(8);
                            }

                            var storeCityLine = Join(request.StoreCity, request.StorePostcode);

                            if (!string.IsNullOrWhiteSpace(storeCityLine))
                            {
                                from.Item().Text(storeCityLine).FontSize(8);
                            }
                        });

                    layers.Layer()
                        .PaddingLeft(RecipientLeft * PointsPerInch)
                        .PaddingTop(RecipientTop * PointsPerInch)
                        .Column(to =>
                        {
                            to.Item().Text(request.Company ?? request.RecipientName).SemiBold();

                            if (!string.IsNullOrWhiteSpace(request.Company))
                            {
                                to.Item().Text(request.RecipientName);
                            }

                            if (!string.IsNullOrWhiteSpace(request.Line1))
                            {
                                to.Item().Text(request.Line1);
                            }

                            if (!string.IsNullOrWhiteSpace(request.Line2))
                            {
                                to.Item().Text(request.Line2);
                            }

                            var cityLine = Join(request.City, request.State, request.Postcode);

                            if (!string.IsNullOrWhiteSpace(cityLine))
                            {
                                to.Item().Text(cityLine);
                            }
                        });
                });
            });
        });
    }

    public byte[] RenderCatalogue(CatalogueRequest request) => BuildCatalogue(request).GeneratePdf();

    /// <summary>The composed price list, before it becomes bytes.</summary>
    public static IDocument BuildCatalogue(CatalogueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var byDepartment = request.Items
            .GroupBy(item => item.DepartmentName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.6f, Unit.Inch);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.Arial).FontSize(9));

                page.Header().Column(header =>
                {
                    header.Item().Text(request.StoreName).FontSize(16).Bold();
                    header.Item().Text($"Price list — {request.PrintedOn:d MMMM yyyy}").FontSize(9).Light();
                    header.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(8).Column(column =>
                {
                    column.Spacing(12);

                    foreach (var department in byDepartment)
                    {
                        column.Item().Column(section =>
                        {
                            section.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(90);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(70);
                                });

                                // The department name lives inside the header rather than above the
                                // table so QuestPDF repeats it when a long department runs onto the
                                // next page — otherwise page two is a list of prices for nothing in
                                // particular.
                                table.Header(head =>
                                {
                                    head.Cell().ColumnSpan(3).PaddingBottom(3)
                                        .Text(department.Key).FontSize(11).SemiBold();

                                    head.Cell().Element(HeaderCell).Text("Code");
                                    head.Cell().Element(HeaderCell).Text("Item");
                                    head.Cell().Element(HeaderCell).AlignRight().Text("Price");
                                });

                                foreach (var item in department.OrderBy(i => i.StockCode, StringComparer.OrdinalIgnoreCase))
                                {
                                    table.Cell().Element(BodyCell).Text(item.StockCode);

                                    table.Cell().Element(BodyCell).Column(nameCell =>
                                    {
                                        nameCell.Item().Text(item.Name);

                                        if (!string.IsNullOrWhiteSpace(item.Description))
                                        {
                                            nameCell.Item().Text(item.Description).FontSize(7).Light();
                                        }
                                    });

                                    table.Cell().Element(BodyCell).AlignRight()
                                        .Text(item.Price.ToString("C", System.Globalization.CultureInfo.CurrentCulture));
                                }
                            });
                        });
                    }

                    if (byDepartment.Count == 0)
                    {
                        column.Item().Text("No items match this selection.").Light();
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer container)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(3).DefaultTextStyle(t => t.SemiBold().FontSize(8));

    private static IContainer BodyCell(IContainer container)
        => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(2);

    private static string Join(params string?[] parts)
        => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
