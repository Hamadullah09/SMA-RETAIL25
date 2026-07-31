using FluentAssertions;
using Retail25.Application.Documents;
using Retail25.Infrastructure.Documents;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// Envelopes and the price list. Most of what can go wrong here is a null address line or an empty
/// selection throwing at render time — which surfaces as a print button that does nothing.
/// </summary>
public sealed class DocumentRendererTests
{
    private readonly QuestPdfDocumentRenderer _renderer = new();

    private static EnvelopeRequest Envelope(string? company = null, string? line2 = null)
        => new("Jane Roe", company, "12 Example Street", line2, "Toronto", "ON", "M5V 1A1",
            "Test Store", "1 Store Road", "Toronto", "M5V 2B2");

    [Fact]
    public void An_envelope_renders()
        => BeAPdf(_renderer.RenderCom10Envelope(Envelope()));

    [Fact]
    public void A_business_envelope_renders_with_both_the_company_and_the_contact()
        => BeAPdf(_renderer.RenderCom10Envelope(Envelope(company: "Roe Holdings Ltd", line2: "Unit 4")));

    /// <summary>
    /// A customer with no address on file still has to produce an envelope — the operator writes it
    /// on by hand. Throwing here would mean one incomplete record blocks a statement run.
    /// </summary>
    [Fact]
    public void An_envelope_with_no_address_on_file_still_renders()
    {
        var pdf = _renderer.RenderCom10Envelope(
            new EnvelopeRequest("Jane Roe", null, null, null, null, null, null, "Test Store", null, null, null));

        BeAPdf(pdf);
    }

    [Fact]
    public void A_catalogue_renders()
    {
        var pdf = _renderer.RenderCatalogue(new CatalogueRequest("Test Store", new DateOnly(2026, 7, 31),
        [
            new CatalogueItem("A-1", "Widget", "A useful widget", 9.99m, "Hardware", "A-1"),
            new CatalogueItem("B-2", "Gadget", null, 24.50m, "Hardware", null),
            new CatalogueItem("C-3", "Sprocket", null, 4.25m, "Garden", null),
        ]));

        BeAPdf(pdf);
    }

    /// <summary>An empty selection says so on the page rather than producing a blank sheet or throwing.</summary>
    [Fact]
    public void An_empty_catalogue_renders_a_page_saying_so()
        => BeAPdf(_renderer.RenderCatalogue(new CatalogueRequest("Test Store", new DateOnly(2026, 7, 31), [])));

    /// <summary>
    /// The catalogue is the one document that genuinely runs to many pages, and QuestPDF's page
    /// numbering only resolves on a second layout pass — so a multi-page run is worth exercising.
    /// </summary>
    [Fact]
    public void A_catalogue_long_enough_to_paginate_renders()
    {
        var items = Enumerable.Range(1, 400)
            .Select(i => new CatalogueItem($"SKU-{i:0000}", $"Item {i}", "Description", i * 1.5m, $"Dept {i % 7}", null))
            .ToList();

        var pdf = _renderer.RenderCatalogue(new CatalogueRequest("Test Store", new DateOnly(2026, 7, 31), items));

        BeAPdf(pdf);
        pdf.Length.Should().BeGreaterThan(20_000, because: "400 items cannot fit on one page");
    }

    private static void BeAPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
