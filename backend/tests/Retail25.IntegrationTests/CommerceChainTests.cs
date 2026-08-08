using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Customers;
using Retail25.Application.Purchasing;
using Retail25.Application.Receivables;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Purchasing;
using Retail25.Domain.ValueObjects;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Phase 5's exit criterion, run as one scenario.
/// <para>
/// <em>Raise a PO, receive it with freight, sell on account, take a partial payment, accrue a late
/// charge, print a statement.</em> Every step of that chain already had unit tests. What none of them
/// could show is whether the chain <b>reconciles</b> — whether the money that leaves the purchase
/// order arrives in the stock valuation, and the money that leaves the till arrives on the customer's
/// account at the same figure.
/// </para>
/// <para>
/// So this asserts on totals at every hand-off, against a real PostgreSQL, through the real handlers.
/// It is deliberately one test rather than six: a chain broken at the fourth link is not discovered
/// by six tests that each set up their own third link.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class CommerceChainTests
{
    private readonly CommerceApiFixture _api;

    public CommerceChainTests(CommerceApiFixture api) => _api = api;

    /// <summary>Unique per run, so a shared database cannot make an assertion pass on old rows.</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    [RequiresDockerFact]
    public async Task A_purchase_order_becomes_stock_becomes_a_sale_becomes_a_settled_account()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        // ------------------------------------------------------------------------------------
        // 1 · An item, and a supplier to buy it from
        // ------------------------------------------------------------------------------------
        var stockCode = Unique("CHAIN");

        var product = await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "Chain test widget", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 100.00m,

            // Tax off. This scenario is about whether money moves intact from one ledger to the
            // next; a tax rate in the middle only obscures which figure went wrong.
            Tax1Applies: false,
            Tax2Applies: false)));

        var supplier = await Ok(sender.Send(new CreateSupplierCommand(
            location.Id,
            new SupplierSection("Chain Test Supplies", null, null, null, new Address(), new ContactDetails()))));

        // ------------------------------------------------------------------------------------
        // 2 · Raise the PO and post it
        // ------------------------------------------------------------------------------------
        var order = await Ok(sender.Send(new GeneratePurchaseOrderCommand(
            location.Id, supplier.Id, OrderQuantityStrategy.Blank)));

        order = await Ok(sender.Send(new AddPurchaseOrderLineCommand(
            order.Id, product.Id, OrderQty: 10m, CostEach: 40.00m, CaseQty: 1m)));

        order.Lines.Should().ContainSingle();
        order.Lines[0].OrderQty.Should().Be(10m);

        order = await Ok(sender.Send(new PostPurchaseOrderCommand(order.Id)));

        // Posting reserves the goods as on-order. Nothing is on hand yet.
        var afterPost = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        afterPost.OnHand.Should().Be(0m, "posting an order does not deliver it");
        afterPost.OnOrder.Should().Be(10m);

        // ------------------------------------------------------------------------------------
        // 3 · Receive it, with freight
        // ------------------------------------------------------------------------------------
        // £400 of goods plus £50 carriage over 10 units = £45 landed each. Freight that vanishes
        // between the supplier's invoice and the stock valuation is the single most common way a
        // shop's margin quietly becomes fiction.
        order = await Ok(sender.Send(new ReceivePurchaseOrderCommand(
            order.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            FreightTotal: 50.00m,
            [new ReceivePurchaseOrderLine(order.Lines[0].Id, QtyReceived: 10m)])));

        var received = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);

        received.OnHand.Should().Be(10m);
        received.OnOrder.Should().Be(0m, "the order is fulfilled");
        received.AvgCost.Should().Be(45.00m, "£400 goods + £50 freight over 10 units");

        // The stock ledger has to agree with the product row, or the valuation report and the item
        // screen will tell a manager two different numbers.
        var ledgerIn = await db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.ProductId == product.Id)
            .SumAsync(e => e.Quantity);

        ledgerIn.Should().Be(10m);

        // ------------------------------------------------------------------------------------
        // 4 · A customer with an account
        // ------------------------------------------------------------------------------------
        var customer = await Ok(sender.Send(new CreateCustomerCommand(
            location.Id,
            new CustomerIdentitySection("Chain", "Tester", "Chain Test Ltd", null, null, null, null),
            Addresses: null,
            Account: new CustomerAccountSection(
                CreditLimit: 1_000m,
                UsualDiscountPct: 0m,
                PriceLevel: 1,
                ExemptTax1: true,
                ExemptTax2: true))));

        // ------------------------------------------------------------------------------------
        // 5 · Sell two of them, on account
        // ------------------------------------------------------------------------------------
        var onAccount = await db.TenderTypes.AsNoTracking()
            .FirstAsync(t => t.Behaviour == TenderBehaviour.OnAccount);

        var cart = await Ok(sender.Send(new CreateCartCommand(station.Id)));
        await Ok(sender.Send(new AssignCartCustomerCommand(cart.Id, customer.Id)));
        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, stockCode, Quantity: 2m)));

        var sale = await Ok(sender.Send(new CompleteSaleCommand(
            cart.Id,
            [new TenderRequest(onAccount.Id, Amount: 200.00m)],
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            PrintReceipt: false)));

        sale.GrandTotal.Should().Be(200.00m, "two at the £100 shelf price, no tax");

        // Stock left the building.
        var afterSale = await db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        afterSale.OnHand.Should().Be(8m);

        // ------------------------------------------------------------------------------------
        // 6 · The sale became an invoice on the account
        // ------------------------------------------------------------------------------------
        var statement = await Ok(sender.Send(new GetCustomerStatementQuery(customer.Id)));

        statement.BalanceDue.Should().Be(200.00m, "the on-account tender is money owed, not money taken");
        statement.Invoices.Should().ContainSingle();

        var invoice = statement.Invoices[0];
        invoice.InvoiceTotal.Should().Be(200.00m);
        invoice.BalanceDue.Should().Be(200.00m);

        // ------------------------------------------------------------------------------------
        // 7 · A partial payment
        // ------------------------------------------------------------------------------------
        var cash = await db.TenderTypes.AsNoTracking().FirstAsync(t => t.Behaviour == TenderBehaviour.Cash);

        await Ok(sender.Send(new TakeInvoicePaymentCommand(customer.Id, Amount: 75.00m, cash.Id, "Cheque 1001")));

        statement = await Ok(sender.Send(new GetCustomerStatementQuery(customer.Id)));

        statement.BalanceDue.Should().Be(125.00m, "£200 owed less £75 paid");
        statement.Invoices[0].BalanceDue.Should().Be(125.00m, "the payment landed on the invoice, not merely on the account");

        // The ledger is the audit trail behind that balance; a balance without a matching journal is
        // a number nobody can defend to an accountant.
        statement.Ledger.Should().NotBeEmpty();

        // ------------------------------------------------------------------------------------
        // 8 · Age the invoice, then accrue late charges
        // ------------------------------------------------------------------------------------
        // Reached into the database on purpose. The alternative is waiting forty-five days, and the
        // behaviour under test is the accrual, not the calendar.
        //
        // Both dates move, not just the due date. The accrual measures the interval since the
        // invoice was last charged and falls back to `IssuedOn` when it never has been — so an
        // invoice issued today but due forty-five days ago is not "overdue", it is incoherent, and
        // the handler correctly declines to charge it. Getting that wrong is what made the first run
        // of this scenario accrue nothing.
        var overdue = await db.Invoices.FirstAsync(i => i.Id == invoice.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        overdue.IssuedOn = today.AddDays(-60);
        overdue.DueOn = today.AddDays(-45);

        await db.SaveChangesAsync();

        // A shop that has not set its terms charges nothing, which is the right default and the
        // reason the first run of this scenario accrued zero: `AccrueLateChargesCommand` finds no
        // enabled policy and returns without touching an invoice. So the scenario configures the
        // terms the way a shop would before expecting a charge.
        if (!await db.LateChargePolicies.AnyAsync(p => p.LocationId == location.Id))
        {
            db.LateChargePolicies.Add(new Retail25.Domain.Receivables.LateChargePolicy
            {
                LocationId = location.Id,
                MonthlyRate = 1.5m,
                GracePeriodDays = 30,
                IsEnabled = true,
            });

            await db.SaveChangesAsync();
        }

        var accrued = await Ok(sender.Send(new AccrueLateChargesCommand(location.Id)));
        accrued.Should().BeGreaterThan(0, "an invoice 45 days overdue is past a 30-day grace period");

        statement = await Ok(sender.Send(new GetCustomerStatementQuery(customer.Id)));

        statement.Invoices[0].PenaltyAccrued.Should().BeGreaterThan(0m);
        statement.BalanceDue.Should().BeGreaterThan(125.00m, "the charge is added to what is owed");

        // ------------------------------------------------------------------------------------
        // 9 · The aging report agrees with the statement
        // ------------------------------------------------------------------------------------
        // Two screens, two queries, one truth. A manager who chases a debt off the aging report and
        // then reads a different figure on the statement stops trusting both.
        var aging = await sender.Send(new GetReceivablesAgingQuery(location.Id));
        var row = aging.Single(a => a.CustomerId == customer.Id);

        row.Total.Should().Be(statement.BalanceDue);

        // The buckets count days past *due*, and each is an upper bound: ≤0 current, ≤30, ≤60, then
        // 90+. Forty-five days therefore lands in Days60, not Days30 — the column headed "30" holds
        // debt between one and thirty days late, not debt thirty days late or more.
        row.Days60.Should().BeGreaterThan(0m, "45 days past due falls in the 31–60 bucket");
        row.Days30.Should().Be(0m);
        row.Current.Should().Be(0m);
    }

    /// <summary>
    /// The credit limit is a real control, not a display field.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_sale_that_would_breach_the_credit_limit_is_refused()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var stockCode = Unique("LIMIT");

        await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "Expensive thing", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 500.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var customer = await Ok(sender.Send(new CreateCustomerCommand(
            location.Id,
            new CustomerIdentitySection("Tight", "Limit", null, null, null, null, null),
            Addresses: null,
            Account: new CustomerAccountSection(CreditLimit: 100m, 0m, 1, true, true))));

        var onAccount = await db.TenderTypes.AsNoTracking()
            .FirstAsync(t => t.Behaviour == TenderBehaviour.OnAccount);

        var cart = await Ok(sender.Send(new CreateCartCommand(station.Id)));
        await Ok(sender.Send(new AssignCartCustomerCommand(cart.Id, customer.Id)));
        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, stockCode, Quantity: 1m)));

        var attempt = await sender.Send(new CompleteSaleCommand(
            cart.Id,
            [new TenderRequest(onAccount.Id, Amount: 500.00m)],
            Guid.NewGuid().ToString("N"),
            PrintReceipt: false));

        attempt.IsFailure.Should().BeTrue("£500 against a £100 limit");
        attempt.Error.Code.Should().Be(CompleteSaleHandler.CreditLimitExceeded.Code);
    }

    /// <summary>Unwraps a <c>Result&lt;T&gt;</c>, failing with the error code rather than a null reference.</summary>
    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;

        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
