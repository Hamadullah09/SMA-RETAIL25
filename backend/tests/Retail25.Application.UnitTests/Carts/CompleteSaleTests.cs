using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Drawer;
using Retail25.Contracts.Terminals;
using Retail25.Application.Receipts;
using Retail25.Application.Sales.Commands;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Carts;

/// <summary>
/// The Phase 3 exit criteria, exercised end to end: a cash sale, a split tender, a return, a void,
/// and a drawer that closes with a correct variance.
/// </summary>
public sealed class CompleteSaleTests
{
    [Fact]
    public async Task A_cash_sale_writes_the_transaction_its_ledgers_and_the_drawer_entry()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        var product = await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");

        var result = await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 60.00m)]);

        result.IsSuccess.Should().BeTrue();
        result.Value.GrandTotal.Should().Be(55.99m);
        result.Value.ChangeGiven.Should().Be(4.01m);

        var transaction = await harness.Db.SalesTransactions.SingleAsync();
        transaction.TransactionNumber.Should().BeGreaterThan(0);
        transaction.Subtotal.Should().Be(49.99m);
        transaction.Tax1Total.Should().Be(2.50m);
        transaction.Tax2Total.Should().Be(3.50m);
        transaction.BusinessDate.Should().Be(new DateOnly(2026, 7, 28));

        // The tax configuration is frozen onto the sale, which is what makes a later reprint honest.
        var snapshot = await harness.Db.SaleTaxSnapshots.SingleAsync();
        snapshot.Tax1Name.Should().Be("GST");
        snapshot.Tax1Rate.Should().Be(5m);

        // Stock left the shelf and the movement is on the ledger, not only on the derived level.
        var movement = await harness.Db.StockLedgerEntries.SingleAsync();
        movement.Quantity.Should().Be(-1m);
        movement.MovementType.Should().Be(Domain.Inventory.MovementType.Sale);

        var level = await harness.Db.StockLevels.SingleAsync(s => s.ProductId == product.Id);
        level.OnHand.Should().Be(-1m);

        // Cash landed in the drawer.
        var drawerEntries = await harness.Db.DrawerLedgerEntries.ToListAsync();
        drawerEntries.Should().Contain(e => e.EntryType == DrawerEntryType.Sale && e.Amount == 55.99m);

        // The till is free for the next customer.
        (await harness.CartStore.GetByStationAsync(harness.Station.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_split_tender_settles_across_card_and_cash()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("BOOT01", "Work boots", 100.00m);
        var cart = await fixture.RingAsync("BOOT01");

        // 100.00 + 5.00 + 7.00 = 112.00, split 60 on card and 52 in cash.
        var result = await fixture.CompleteAsync(cart.Id, [fixture.Card(60.00m), fixture.Cash(52.00m, 52.00m)]);

        result.IsSuccess.Should().BeTrue();
        result.Value.GrandTotal.Should().Be(112.00m);
        result.Value.ChangeGiven.Should().Be(0m);

        var tenders = await harness.Db.SaleTenders.ToListAsync();
        tenders.Should().HaveCount(2);
        tenders.Sum(t => t.Amount).Should().Be(112.00m);
        tenders.Should().Contain(t => t.Behaviour == TenderBehaviour.Card && t.AuthCode != null);
    }

    /// <summary>Stage 4: a gift-card tender spends the card's stored value, mirroring how a gift certificate already does.</summary>
    [Fact]
    public async Task A_gift_card_tender_redeems_the_cards_stored_value()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");

        var card = Domain.Receivables.GiftCard.Issue(
            "TESTCARD1", 100m, DateOnly.FromDateTime(harness.Clock.Now.DateTime), null, null).Value;
        card.CreatedAt = harness.Clock.Now;
        harness.Db.GiftCards.Add(card);
        await harness.Db.SaveChangesAsync();

        var result = await fixture.CompleteAsync(cart.Id, [fixture.GiftCard(55.99m, "TESTCARD1")]);

        result.IsSuccess.Should().BeTrue();

        var refreshed = await harness.Db.GiftCards.SingleAsync();
        refreshed.RemainingValue.Should().Be(44.01m);
        refreshed.IsActive.Should().BeTrue();
    }

    /// <summary>A gift card spent down to zero deactivates — the same rule <c>GiftCard.Redeem</c> enforces standalone.</summary>
    [Fact]
    public async Task A_gift_card_spent_to_zero_becomes_inactive()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");

        var card = Domain.Receivables.GiftCard.Issue(
            "TESTCARD2", 55.99m, DateOnly.FromDateTime(harness.Clock.Now.DateTime), null, null).Value;
        card.CreatedAt = harness.Clock.Now;
        harness.Db.GiftCards.Add(card);
        await harness.Db.SaveChangesAsync();

        await fixture.CompleteAsync(cart.Id, [fixture.GiftCard(55.99m, "TESTCARD2")]);

        var refreshed = await harness.Db.GiftCards.SingleAsync();
        refreshed.RemainingValue.Should().Be(0m);
        refreshed.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_short_payment_is_refused_rather_than_half_completing_the_sale()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("BOOT01", "Work boots", 100.00m);
        var cart = await fixture.RingAsync("BOOT01");

        var result = await fixture.CompleteAsync(cart.Id, [fixture.Cash(50.00m, 50.00m)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tender.mismatch");

        harness.Db.SalesTransactions.Should().BeEmpty();
        (await harness.CartStore.GetAsync(cart.Id)).Should().NotBeNull("a refused payment must not close the sale");
    }

    [Fact]
    public async Task A_return_pays_the_customer_and_puts_the_stock_back()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("SHIRT01", "Cotton shirt", 20.00m);
        var cart = await fixture.RingAsync("SHIRT01", LineType.Return);

        var result = await fixture.CompleteAsync(cart.Id, [fixture.Cash(-22.40m, -22.40m)]);

        result.IsSuccess.Should().BeTrue();
        result.Value.GrandTotal.Should().Be(-22.40m);

        var movement = await harness.Db.StockLedgerEntries.SingleAsync();
        movement.Quantity.Should().Be(1m, "the goods came back");

        var drawerEntry = await harness.Db.DrawerLedgerEntries
            .SingleAsync(e => e.EntryType == DrawerEntryType.Refund);
        drawerEntry.Amount.Should().Be(-22.40m);
    }

    [Fact]
    public async Task A_void_reverses_every_ledger_without_editing_the_original()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");
        var sale = await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 55.99m)]);

        var voidHandler = new VoidSaleHandler(
            harness.Db, fixture.Sequences, harness.Notifier, harness.CurrentUser, harness.Clock);

        var voided = await voidHandler.Handle(
            new VoidSaleCommand(sale.Value.TransactionId, Guid.NewGuid().ToString(), "Customer changed their mind"),
            default);

        voided.IsSuccess.Should().BeTrue();

        var original = await harness.Db.SalesTransactions.SingleAsync(t => t.Id == sale.Value.TransactionId);
        original.Status.Should().Be(TransactionStatus.Voided);
        original.GrandTotal.Should().Be(55.99m, "history is never rewritten");
        original.VoidedByTransactionId.Should().Be(voided.Value.ReversalTransactionId);

        var reversal = await harness.Db.SalesTransactions.SingleAsync(t => t.Id == voided.Value.ReversalTransactionId);
        reversal.GrandTotal.Should().Be(-55.99m);
        reversal.ReversesTransactionId.Should().Be(original.Id);

        // Stock is back and the ledger explains why.
        var movements = await harness.Db.StockLedgerEntries.ToListAsync();
        movements.Sum(m => m.Quantity).Should().Be(0m);
        movements.Should().Contain(m => m.Reason == "Void");
    }

    [Fact]
    public async Task A_drawer_closes_with_a_variance_the_supervisor_can_reconcile()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness, openingFloat: 200.00m);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");
        await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 55.99m)]);

        var drawer = new DrawerHandlers(
            harness.Db, harness.ContextLoader, harness.Notifier, harness.TerminalNotifier, harness.CurrentUser, harness.Clock);

        await drawer.Handle(new PayOutCommand(harness.Station.Id, 20.00m, "Petty cash"), default);

        // 200.00 float + 55.99 cash sale − 20.00 pay-out = 235.99 expected. The till counted 235.00.
        var closed = await drawer.Handle(new CloseDrawerSessionCommand(harness.Station.Id, 235.00m), default);

        closed.IsSuccess.Should().BeTrue();
        closed.Value.ExpectedCash.Should().Be(235.99m);
        closed.Value.CountedCash.Should().Be(235.00m);
        closed.Value.Variance.Should().Be(-0.99m);
        closed.Value.CashSales.Should().Be(55.99m);
        closed.Value.PayOuts.Should().Be(-20.00m);
        closed.Value.Status.Should().Be(DrawerSessionStatus.Closed);
        closed.Value.TenderTotals.Should().ContainSingle(t => t.TenderName == "Cash");
    }

    [Fact]
    public async Task Cash_cannot_be_taken_without_an_open_drawer()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness, openDrawer: false);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");

        var result = await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 55.99m)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("drawer.not_open");
    }

    /// <summary>
    /// A reprint is rebuilt from the sale's own snapshot rows, so it survives a later rate change —
    /// the guarantee at guide p.56.
    /// </summary>
    [Fact]
    public async Task A_reprint_shows_the_taxes_that_were_in_force_at_the_time_of_the_sale()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");
        var sale = await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 55.99m)]);

        // The store puts up its taxes the next day.
        var current = await harness.Db.TaxConfigurations.SingleAsync();
        current.Supersede(new DateOnly(2026, 7, 29));
        harness.Db.TaxConfigurations.Add(TaxConfiguration.Create(
            harness.Location.Id,
            new DateOnly(2026, 7, 29),
            true, "GST", new Domain.ValueObjects.Percentage(9m),
            true, "PST", new Domain.ValueObjects.Percentage(11m),
            false,
            false, "Service", Domain.ValueObjects.Percentage.Zero, false,
            TaxationType.Exclusive,
            null).Value);
        await harness.Db.SaveChangesAsync();

        var receipts = new ReceiptBuilder(harness.Db);
        var document = await receipts.BuildAsync(sale.Value.TransactionId, ReceiptFormat.Slip40, isReprint: true, default);

        document.Should().NotBeNull();
        document!.Tax1Total.Should().Be(2.50m, "the sale was rung at 5%, not at today's 9%");
        document.GrandTotal.Should().Be(55.99m);
        document.IsReprint.Should().BeTrue();
    }

    /// <summary>A packing slip carries no money (guide p.12).</summary>
    [Fact]
    public async Task A_packing_slip_omits_prices()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var fixture = await SaleFixture.CreateAsync(harness);

        await harness.AddProductAsync("POLO01", "Columbia polo", 49.99m);
        var cart = await fixture.RingAsync("POLO01");
        var sale = await fixture.CompleteAsync(cart.Id, [fixture.Cash(55.99m, 55.99m)]);

        var receipts = new ReceiptBuilder(harness.Db);
        var document = await receipts.BuildAsync(sale.Value.TransactionId, ReceiptFormat.PackingSlip, false, default);

        document.Should().NotBeNull();
        document!.GrandTotal.Should().Be(0m);
        document.Lines.Should().OnlyContain(l => l.UnitPrice == 0m);
        document.Lines.Single().Quantity.Should().Be(1m);
    }

    /// <summary>
    /// Everything a completion test needs: tenders, an open drawer, a sequence generator that does
    /// not need Postgres, and the two handlers under test.
    /// </summary>
    private sealed class SaleFixture
    {
        private readonly PosTestHarness _harness;

        private SaleFixture(PosTestHarness harness) => _harness = harness;

        public TenderType CashTender { get; private set; } = null!;

        public TenderType CardTender { get; private set; } = null!;

        public TenderType GiftCardTender { get; private set; } = null!;

        public ISequenceGenerator Sequences { get; private set; } = null!;

        public CompleteSaleHandler Complete { get; private set; } = null!;

        public AddCartLineByIdentifierHandler Add { get; private set; } = null!;

        public static async Task<SaleFixture> CreateAsync(
            PosTestHarness harness,
            decimal openingFloat = 100.00m,
            bool openDrawer = true)
        {
            var fixture = new SaleFixture(harness)
            {
                CashTender = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash),
                CardTender = await harness.AddTenderAsync("CREDIT", "Credit", TenderBehaviour.Card),
                GiftCardTender = await harness.AddTenderAsync("GIFTCARD", "Gift Card", TenderBehaviour.GiftCard),
                Sequences = new CountingSequenceGenerator(),
            };

            fixture.Add = new AddCartLineByIdentifierHandler(harness.Workflow, harness.Resolver, harness.LineFactory);

            fixture.Complete = new CompleteSaleHandler(
                harness.CartStore,
                harness.Db,
                harness.ContextLoader,
                harness.Pricing,
                fixture.Sequences,
                new Infrastructure.Services.SimulatorPaymentGateway(),
                harness.Notifier,
                harness.TerminalNotifier,
                harness.Debouncer,
                new ReceiptBuilder(harness.Db),
                harness.CurrentUser,
                harness.Clock);

            if (openDrawer)
            {
                var drawer = new DrawerHandlers(
                    harness.Db, harness.ContextLoader, harness.Notifier, harness.TerminalNotifier, harness.CurrentUser, harness.Clock);

                await drawer.Handle(new OpenDrawerSessionCommand(harness.Station.Id, openingFloat), default);
            }

            return fixture;
        }

        public async Task<Cart> RingAsync(string identifier, LineType lineType = LineType.Sale)
        {
            var cart = await _harness.OpenCartAsync();
            var added = await Add.Handle(
                new AddCartLineByIdentifierCommand(cart.Id, identifier, LineType: lineType),
                default);

            added.IsSuccess.Should().BeTrue(added.IsFailure ? added.Error.Code : string.Empty);
            return cart;
        }

        public Task<Domain.Common.Result<CompleteSaleResult>> CompleteAsync(Guid cartId, IReadOnlyList<TenderRequest> tenders)
            => Complete.Handle(
                new CompleteSaleCommand(cartId, tenders, Guid.NewGuid().ToString(), PrintReceipt: false),
                default);

        public TenderRequest Cash(decimal amount, decimal tendered) => new(CashTender.Id, amount, tendered);

        public TenderRequest Card(decimal amount) => new(CardTender.Id, amount, amount, CardToken: "4111111111114242");

        public TenderRequest GiftCard(decimal amount, string serial) => new(GiftCardTender.Id, amount, amount, Reference: serial);
    }

}
