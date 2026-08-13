using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Sales.Commands;
using Retail25.Application.Sales.Queries;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// Giving part of a sale back.
/// <para>
/// Before this the till could only ring a negative line, which knows nothing about what was sold —
/// so nothing stopped a customer being handed back three of the two shirts they bought, or the same
/// tagged jacket being returned twice. These assert the rules that make a refund a refund rather
/// than a negative sale: it is counted against the original, it restores stock, and it cannot give
/// back more than went out.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class RefundTests
{
    private readonly CommerceApiFixture _api;

    public RefundTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    /// <summary>Rings a sale of <paramref name="quantity"/> at 100.00 each and returns what it needs.</summary>
    private async Task<(long TransactionId, long SaleLineId, long ProductId, long TenderTypeId)> SellAsync(
        ISender sender,
        ApplicationDbContext db,
        decimal quantity)
    {
        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var stockCode = Unique("RFND");

        var product = await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "Refundable thing", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 100.00m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var cheque = await db.TenderTypes.AsNoTracking().FirstAsync(t => t.Behaviour == TenderBehaviour.Manual);

        var cart = await Ok(sender.Send(new CreateCartCommand(station.Id)));
        await Ok(sender.Send(new AddCartLineByIdentifierCommand(cart.Id, stockCode, Quantity: quantity)));

        var sale = await Ok(sender.Send(new CompleteSaleCommand(
            cart.Id,
            [new TenderRequest(cheque.Id, Amount: 100.00m * quantity, AmountTendered: 100.00m * quantity, Reference: "CHQ")],
            Guid.NewGuid().ToString("N"),
            PrintReceipt: false)));

        var saleLineId = await db.SaleLines.AsNoTracking()
            .Where(l => l.TransactionId == sale.TransactionId)
            .Select(l => l.Id)
            .FirstAsync();

        return (sale.TransactionId, saleLineId, product.Id, cheque.Id);
    }

    [RequiresDockerFact]
    public async Task Part_of_a_sale_comes_back_and_the_rest_stays_refundable()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sold = await SellAsync(sender, db, quantity: 3m);

        var onHandAfterSale = await OnHandAsync(db, sold.ProductId);

        var refund = await Ok(sender.Send(new RefundSaleCommand(
            sold.TransactionId,
            [new RefundLineRequest(sold.SaleLineId, 1m)],
            [new RefundTenderRequest(sold.TenderTypeId, 100.00m, "CHQ-REFUND")],
            Guid.NewGuid().ToString("N"),
            "Customer changed their mind")));

        refund.RefundedTotal.Should().Be(100.00m, "one of three at 100.00");

        // The original is untouched; the refund is its own transaction pointing back at it.
        var original = await db.SalesTransactions.AsNoTracking().FirstAsync(t => t.Id == sold.TransactionId);
        original.GrandTotal.Should().Be(300.00m, "a refund never edits the sale it refunds");
        original.Status.Should().Be(TransactionStatus.Completed);

        var written = await db.SalesTransactions.AsNoTracking().FirstAsync(t => t.Id == refund.RefundTransactionId);
        written.ReversesTransactionId.Should().Be(sold.TransactionId);
        written.GrandTotal.Should().Be(-100.00m);

        // One went back on the shelf.
        (await OnHandAsync(db, sold.ProductId)).Should().Be(onHandAfterSale + 1m);

        // And the screen is told what is left.
        var detail = await Ok(sender.Send(new GetSaleQuery(sold.TransactionId)));
        var line = detail.Lines.Single(l => l.SaleLineId == sold.SaleLineId);
        line.RefundedQuantity.Should().Be(1m);
        line.RefundableQuantity.Should().Be(2m);
    }

    [RequiresDockerFact]
    public async Task More_than_was_sold_cannot_come_back()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sold = await SellAsync(sender, db, quantity: 2m);

        var attempt = await sender.Send(new RefundSaleCommand(
            sold.TransactionId,
            [new RefundLineRequest(sold.SaleLineId, 3m)],
            [new RefundTenderRequest(sold.TenderTypeId, 300.00m)],
            Guid.NewGuid().ToString("N")));

        attempt.IsFailure.Should().BeTrue("only two were sold");
        attempt.Error.Code.Should().Be(RefundSaleHandler.MoreThanWasSold.Code);
    }

    /// <summary>
    /// The one a negative line could never catch: refunding the same thing twice.
    /// </summary>
    [RequiresDockerFact]
    public async Task What_has_already_come_back_cannot_come_back_again()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sold = await SellAsync(sender, db, quantity: 1m);

        await Ok(sender.Send(new RefundSaleCommand(
            sold.TransactionId,
            [new RefundLineRequest(sold.SaleLineId, 1m)],
            [new RefundTenderRequest(sold.TenderTypeId, 100.00m)],
            Guid.NewGuid().ToString("N"))));

        var again = await sender.Send(new RefundSaleCommand(
            sold.TransactionId,
            [new RefundLineRequest(sold.SaleLineId, 1m)],
            [new RefundTenderRequest(sold.TenderTypeId, 100.00m)],
            Guid.NewGuid().ToString("N")));

        again.IsFailure.Should().BeTrue("it has already been given back once");
        again.Error.Code.Should().Be(RefundSaleHandler.MoreThanWasSold.Code);
    }

    [RequiresDockerFact]
    public async Task A_refund_the_money_does_not_add_up_to_is_refused()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sold = await SellAsync(sender, db, quantity: 2m);
        var before = await db.SalesTransactions.CountAsync();

        var attempt = await sender.Send(new RefundSaleCommand(
            sold.TransactionId,
            [new RefundLineRequest(sold.SaleLineId, 1m)],
            [new RefundTenderRequest(sold.TenderTypeId, 5.00m)],
            Guid.NewGuid().ToString("N")));

        attempt.IsFailure.Should().BeTrue("100.00 came back but only 5.00 was handed over");
        attempt.Error.Code.Should().Be(RefundSaleHandler.TenderMismatch.Code);

        (await db.SalesTransactions.CountAsync()).Should()
            .Be(before, "a refused refund leaves no transaction behind");
    }

    /// <summary>A refund is money leaving the till, so a retried request must not pay twice.</summary>
    [RequiresDockerFact]
    public async Task Refunding_twice_with_one_key_pays_once()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sold = await SellAsync(sender, db, quantity: 2m);

        var key = Guid.NewGuid().ToString("N");
        var lines = new[] { new RefundLineRequest(sold.SaleLineId, 1m) };
        var tenders = new[] { new RefundTenderRequest(sold.TenderTypeId, 100.00m) };

        var first = await Ok(sender.Send(new RefundSaleCommand(sold.TransactionId, lines, tenders, key)));
        var before = await db.SalesTransactions.CountAsync();

        var second = await sender.Send(new RefundSaleCommand(sold.TransactionId, lines, tenders, key));

        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Code : string.Empty);
        second.Value.RefundTransactionId.Should().Be(first.RefundTransactionId);
        (await db.SalesTransactions.CountAsync()).Should().Be(before, "the customer is paid once");
    }

    private static async Task<decimal> OnHandAsync(ApplicationDbContext db, long productId)
        => await db.StockLevels.AsNoTracking()
            .Where(s => s.ProductId == productId)
            .Select(s => s.OnHand)
            .FirstOrDefaultAsync();

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}
