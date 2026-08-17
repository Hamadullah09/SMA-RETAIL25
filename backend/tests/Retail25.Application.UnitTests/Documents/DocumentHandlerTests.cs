using FluentAssertions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Documents;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Xunit;

namespace Retail25.Application.UnitTests.Documents;

/// <summary>
/// What the handlers hand the renderer. The renderers are exercised separately; what matters here is
/// that the right items, the right number of copies and the right store details reach them.
/// </summary>
public sealed class DocumentHandlerTests
{
    private readonly ILabelRenderer _labels = Substitute.For<ILabelRenderer>();
    private readonly IDocumentRenderer _documents = Substitute.For<IDocumentRenderer>();
    private readonly IDateTime _clock = Substitute.For<IDateTime>();

    public DocumentHandlerTests()
    {
        _labels.RenderPriceTags(Arg.Any<LabelSheetRequest>()).Returns([1, 2, 3]);
        _labels.RenderBarcodeLabels(Arg.Any<LabelSheetRequest>()).Returns([4, 5, 6]);
        _documents.RenderCom10Envelope(Arg.Any<EnvelopeRequest>()).Returns([7]);
        _documents.RenderCatalogue(Arg.Any<CatalogueRequest>()).Returns([8]);
        _clock.Today().Returns(new DateOnly(2026, 7, 31));
    }

    private DocumentHandlers Handlers(MastersTestHarness harness)
        => new(harness.Db, _labels, _documents, _clock, new Retail25.Application.Receipts.ReceiptBuilder(harness.Db));

    /// <summary>
    /// Asking for a receipt that does not exist says so, rather than handing back a PDF of nothing.
    /// <para>
    /// A blank slip is the worst possible answer here: it prints, it looks like a receipt, and it
    /// tells whoever is holding it that the sale had no lines rather than that it was never found.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_receipt_for_a_sale_that_does_not_exist_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness).Handle(new PrintReceiptQuery(404_404), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("documents.sale_not_found");
        _documents.DidNotReceive().RenderReceipt(Arg.Any<Retail25.Contracts.Terminals.ReceiptDocument>());
    }

    [Fact]
    public async Task A_price_tag_run_carries_the_item_details_through()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget", price: 12.50m);
        product.UpdateDetails("Widget", null, "0123456789012", "B12", null);
        await harness.Db.SaveChangesAsync();

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(product.Id, 3)]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var sheet = _labels.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0].As<LabelSheetRequest>();
        var line = sheet.Lines.Should().ContainSingle().Subject;

        line.Copies.Should().Be(3);
        line.Tag.StockCode.Should().Be("A-1");
        line.Tag.Name.Should().Be("Widget");
        line.Tag.Price.Should().Be(12.50m);
        line.Tag.Barcode.Should().Be("0123456789012");
        line.Tag.BinLocation.Should().Be("B12");
    }

    /// <summary>
    /// An operator typing 5000 into the copies box should not tie up the printer for an hour — and a
    /// zero should still print the one tag they asked for.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(700, 500)]
    public async Task Copies_are_clamped_to_something_a_printer_can_survive(int asked, int expected)
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget");

        await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(product.Id, asked)]),
            CancellationToken.None);

        var sheet = _labels.ReceivedCalls().Single().GetArguments()[0].As<LabelSheetRequest>();
        sheet.Lines.Single().Copies.Should().Be(expected);
    }

    [Fact]
    public async Task Barcode_first_goes_to_the_other_renderer()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget");

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(product.Id)], BarcodeFirst: true),
            CancellationToken.None);

        result.Value.Should().Equal([4, 5, 6]);
        _labels.DidNotReceive().RenderPriceTags(Arg.Any<LabelSheetRequest>());
    }

    /// <summary>
    /// The EPC is carried onto the print job for a printer that can encode. Nothing here writes to a
    /// tag — this only proves the value reaches the job rather than being dropped on the way.
    /// </summary>
    [Fact]
    public async Task A_serialised_item_carries_its_epc_onto_the_print_job()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget");

        harness.Db.SerializedUnits.Add(SerializedUnit.Create(
            product.Id, harness.Location.Id, "SN-1", "300833B2DDD9014000000001", DateTimeOffset.UtcNow).Value);
        await harness.Db.SaveChangesAsync();

        await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(product.Id)]),
            CancellationToken.None);

        var sheet = _labels.ReceivedCalls().Single().GetArguments()[0].As<LabelSheetRequest>();
        sheet.Lines.Single().Tag.EpcToEncode.Should().Be("300833B2DDD9014000000001");
    }

    [Fact]
    public async Task An_item_that_is_not_at_this_location_is_skipped_rather_than_printed_blank()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var here = await harness.AddProductAsync("A-1", "Widget");

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id,
                [new LabelRequestLine(here.Id), new LabelRequestLine(TestIds.Next())]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var sheet = _labels.ReceivedCalls().Single().GetArguments()[0].As<LabelSheetRequest>();
        sheet.Lines.Should().ContainSingle().Which.Tag.StockCode.Should().Be("A-1");
    }

    [Fact]
    public async Task A_run_with_no_lines_fails_rather_than_producing_a_blank_sheet()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, []), CancellationToken.None);

        result.Error.Should().Be(DocumentHandlers.NothingToPrint);
        _labels.DidNotReceiveWithAnyArgs().RenderPriceTags(default!);
    }

    [Fact]
    public async Task A_run_where_nothing_resolves_fails_rather_than_producing_a_blank_sheet()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(TestIds.Next())]),
            CancellationToken.None);

        result.Error.Should().Be(DocumentHandlers.NothingToPrint);
    }

    [Fact]
    public async Task A_deleted_item_does_not_print()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var product = await harness.AddProductAsync("A-1", "Widget");

        harness.Db.Products.Remove(product);
        await harness.Db.SaveChangesAsync();

        var result = await Handlers(harness).Handle(
            new PrintPriceTagsQuery(harness.Location.Id, [new LabelRequestLine(product.Id)]),
            CancellationToken.None);

        result.Error.Should().Be(DocumentHandlers.NothingToPrint);
    }

    [Fact]
    public async Task An_envelope_uses_the_registered_business_name_over_the_location_name()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Jane", "Roe");

        var profile = BusinessProfile.Create(harness.Location.Id, "Roe Retail Ltd");
        harness.Db.BusinessProfiles.Add(profile);
        await harness.Db.SaveChangesAsync();

        var result = await Handlers(harness).Handle(
            new PrintStatementEnvelopeQuery(customer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var envelope = _documents.ReceivedCalls().Single().GetArguments()[0].As<EnvelopeRequest>();
        envelope.StoreName.Should().Be("Roe Retail Ltd");
        envelope.RecipientName.Should().Contain("Roe");
    }

    /// <summary>With no business profile filled in, the location's own name is the honest fallback.</summary>
    [Fact]
    public async Task An_envelope_falls_back_to_the_location_name()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Jane", "Roe");

        await Handlers(harness).Handle(new PrintStatementEnvelopeQuery(customer.Id), CancellationToken.None);

        var envelope = _documents.ReceivedCalls().Single().GetArguments()[0].As<EnvelopeRequest>();
        envelope.StoreName.Should().Be(harness.Location.Name);
    }

    [Fact]
    public async Task An_envelope_for_an_unknown_customer_fails()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await Handlers(harness).Handle(
            new PrintStatementEnvelopeQuery(TestIds.Next()), CancellationToken.None);

        result.Error.Should().Be(DocumentHandlers.CustomerNotFound);
    }

    [Fact]
    public async Task The_catalogue_names_each_item_department_and_files_the_rest_as_unfiled()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var hardware = await harness.AddDepartmentAsync("Hardware");
        await harness.AddProductAsync("A-1", "Widget", departmentId: hardware.Id);
        await harness.AddProductAsync("B-2", "Loose item");

        await Handlers(harness).Handle(new PrintCatalogueQuery(harness.Location.Id), CancellationToken.None);

        var catalogue = _documents.ReceivedCalls().Single().GetArguments()[0].As<CatalogueRequest>();

        catalogue.Items.Should().HaveCount(2);
        catalogue.Items.Single(i => i.StockCode == "A-1").DepartmentName.Should().Be("Hardware");
        catalogue.Items.Single(i => i.StockCode == "B-2").DepartmentName.Should().Be("Unfiled");
        catalogue.PrintedOn.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task The_catalogue_honours_the_department_filter()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var hardware = await harness.AddDepartmentAsync("Hardware");
        var garden = await harness.AddDepartmentAsync("Garden");
        await harness.AddProductAsync("A-1", "Widget", departmentId: hardware.Id);
        await harness.AddProductAsync("C-3", "Spade", departmentId: garden.Id);

        await Handlers(harness).Handle(
            new PrintCatalogueQuery(harness.Location.Id, DepartmentId: garden.Id), CancellationToken.None);

        var catalogue = _documents.ReceivedCalls().Single().GetArguments()[0].As<CatalogueRequest>();
        catalogue.Items.Should().ContainSingle().Which.StockCode.Should().Be("C-3");
    }

    [Fact]
    public async Task The_catalogue_search_matches_a_code_or_a_name()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddProductAsync("A-1", "Widget");
        await harness.AddProductAsync("B-2", "Gadget");

        await Handlers(harness).Handle(
            new PrintCatalogueQuery(harness.Location.Id, Search: " Widget "), CancellationToken.None);

        var catalogue = _documents.ReceivedCalls().Single().GetArguments()[0].As<CatalogueRequest>();
        catalogue.Items.Should().ContainSingle().Which.StockCode.Should().Be("A-1");
    }
}
