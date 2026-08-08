using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;

namespace Retail25.Application.Documents;

/// <summary>How many labels to print for one item.</summary>
public sealed record LabelRequestLine(long ProductId, int Copies = 1);

/// <summary>
/// A sheet of price tags for the chosen items (guide App. L).
/// <para>
/// Gated on catalogue read rather than write: printing a shelf tag is looking at a price, not
/// changing one.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record PrintPriceTagsQuery(
    long LocationId,
    IReadOnlyList<LabelRequestLine> Lines,
    LabelStock Stock = LabelStock.Avery5160,
    bool BarcodeFirst = false,
    bool ShowBarcode = true,
    int SkipLabels = 0) : IRequest<Result<byte[]>>;

/// <summary>A statement envelope for one customer, laid out for a #10 window.</summary>
[RequiresPermission(PermissionKeys.Ar.Read)]
public sealed record PrintStatementEnvelopeQuery(long CustomerId) : IRequest<Result<byte[]>>;

/// <summary>The price list, filtered the same way the catalogue browse is.</summary>
[RequiresPermission(PermissionKeys.Catalog.Read)]
public sealed record PrintCatalogueQuery(
    long LocationId,
    long? DepartmentId = null,
    long? CategoryId = null,
    string? Search = null) : IRequest<Result<byte[]>>;

public sealed class DocumentHandlers
    : IRequestHandler<PrintPriceTagsQuery, Result<byte[]>>,
      IRequestHandler<PrintStatementEnvelopeQuery, Result<byte[]>>,
      IRequestHandler<PrintCatalogueQuery, Result<byte[]>>
{
    public static readonly Error NothingToPrint = new("documents.nothing_to_print", "There is nothing to print.");
    public static readonly Error CustomerNotFound = new("documents.customer_not_found", "No such customer.");

    private readonly IApplicationDbContext _db;
    private readonly ILabelRenderer _labels;
    private readonly IDocumentRenderer _documents;
    private readonly IDateTime _clock;

    public DocumentHandlers(
        IApplicationDbContext db,
        ILabelRenderer labels,
        IDocumentRenderer documents,
        IDateTime clock)
    {
        _db = db;
        _labels = labels;
        _documents = documents;
        _clock = clock;
    }

    public async Task<Result<byte[]>> Handle(PrintPriceTagsQuery request, CancellationToken ct)
    {
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();

        if (productIds.Count == 0)
        {
            return Result.Failure<byte[]>(NothingToPrint);
        }

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId && productIds.Contains(p.Id) && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        // An EPC only exists for a serialized item that already has a tag. Carried onto the print
        // job for a printer that can encode; nothing here writes to a tag.
        var epcs = await _db.SerializedUnits.AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId) && u.Epc != null)
            .GroupBy(u => u.ProductId)
            .Select(g => new { ProductId = g.Key, Epc = g.First().Epc })
            .ToDictionaryAsync(x => x.ProductId, x => x.Epc, ct);

        var lines = new List<LabelLine>();

        foreach (var line in request.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            lines.Add(new LabelLine(
                new PriceTag(
                    product.StockCode,
                    product.Name,
                    product.RegularPrice,
                    product.Upc,
                    product.BinLocation,
                    epcs.GetValueOrDefault(product.Id)),
                Math.Clamp(line.Copies, 1, 500)));
        }

        if (lines.Count == 0)
        {
            return Result.Failure<byte[]>(NothingToPrint);
        }

        var sheet = new LabelSheetRequest(request.Stock, lines, request.ShowBarcode, request.SkipLabels);

        return Result.Success(request.BarcodeFirst
            ? _labels.RenderBarcodeLabels(sheet)
            : _labels.RenderPriceTags(sheet));
    }

    public async Task<Result<byte[]>> Handle(PrintStatementEnvelopeQuery request, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);

        if (customer is null)
        {
            return Result.Failure<byte[]>(CustomerNotFound);
        }

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == customer.LocationId, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.LocationId == customer.LocationId, ct);

        return Result.Success(_documents.RenderCom10Envelope(new EnvelopeRequest(
            customer.FullName,
            customer.Company,
            customer.BillingAddress.Line1,
            customer.BillingAddress.Line2,
            customer.BillingAddress.City,
            customer.BillingAddress.StateOrProvince,
            customer.BillingAddress.PostalCode,
            business?.BusinessName ?? location?.Name ?? "Store",
            business?.Address.Line1,
            business?.Address.City,
            business?.Address.PostalCode)));
    }

    public async Task<Result<byte[]>> Handle(PrintCatalogueQuery request, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking()
            .Where(p => p.LocationId == request.LocationId && !p.IsDeleted);

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(p => p.DepartmentId == departmentId);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.StockCode.Contains(term) || p.Name.Contains(term));
        }

        var products = await query.OrderBy(p => p.StockCode).ToListAsync(ct);
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.LocationId, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.LocationId == request.LocationId, ct);

        var items = products.Select(p => new CatalogueItem(
            p.StockCode,
            p.Name,
            p.Description,
            p.RegularPrice,
            p.DepartmentId is { } id && departments.TryGetValue(id, out var name) ? name : "Unfiled",
            p.Upc)).ToList();

        return Result.Success(_documents.RenderCatalogue(new CatalogueRequest(
            business?.BusinessName ?? location?.Name ?? "Store",
            _clock.Today(),
            items)));
    }
}
