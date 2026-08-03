using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Inventory;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Writes a demonstration catalogue: departments, categories, a few hundred products across three
/// pricing tiers, and the RFID tags that sit on them.
/// <para>
/// This is deliberately separate from <see cref="DatabaseSeeder"/>. That one writes configuration a
/// real shop cannot run without and always runs; this one writes invented merchandise and only runs
/// when <c>Demo:SeedCatalogue</c> is set. Nobody wants three hundred fictional products turning up
/// in their live inventory count.
/// </para>
/// <para>
/// Everything here is deterministic — a fixed-seed generator, not <see cref="Guid.NewGuid"/> or the
/// clock — so two developers seeding the same database get the same catalogue and a failing test can
/// be reproduced from the stock code alone.
/// </para>
/// </summary>
public sealed class DemoDataSeeder
{
    /// <summary>Marks every row this seeder owns, so re-running it is a no-op and cleanup is one delete.</summary>
    public const string StockCodePrefix = "DEMO-";

    /// <summary>Retail, Trade, Wholesale. Level 1 is <see cref="Product.RegularPrice"/> itself.</summary>
    private const int TradeLevel = 2;
    private const int WholesaleLevel = 3;

    /// <summary>Trade takes 12% off retail, wholesale 22%. Both are rounded to the cent.</summary>
    private const decimal TradeDiscount = 0.12m;
    private const decimal WholesaleDiscount = 0.22m;

    /// <summary>Tagged lines get RFID; the rest are barcode-only, which is how a real shop rolls it out.</summary>
    private const int UnitsPerTaggedProduct = 24;

    private readonly ApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        ApplicationDbContext db,
        IDateTime clock,
        IConfiguration configuration,
        ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_configuration.GetValue("Demo:SeedCatalogue", false))
        {
            return;
        }

        var locationId = await _db.Locations
            .AsNoTracking()
            .OrderBy(l => l.LegacyCode)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (locationId is null)
        {
            _logger.LogWarning("Demo catalogue skipped: no location exists yet.");
            return;
        }

        if (await _db.Products.AnyAsync(p => p.StockCode.StartsWith(StockCodePrefix), ct))
        {
            // Pictures arrived after the demo catalogue did, so a bench seeded before that has items
            // and no images. Top them up rather than making people drop their database — the grid
            // looks broken without them, and "delete everything and start again" is a poor answer to
            // a feature being added.
            await BackfillDemoImagesAsync(ct);

            _logger.LogInformation("Demo catalogue already present.");
            return;
        }

        var departments = await SeedDepartmentsAsync(locationId.Value, ct);
        var categories = await SeedCategoriesAsync(locationId.Value, ct);
        var products = SeedProducts(locationId.Value, departments, categories);

        await _db.SaveChangesAsync(ct);

        var units = SeedSerializedUnits(locationId.Value, products);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Demo catalogue written: {Products} products, {Units} RFID tags",
            products.Count,
            units);
    }

    /// <summary>
    /// Gives demo items their pictures when they were seeded before pictures existed.
    /// <para>
    /// Does nothing once any demo item has one, so it costs a single <c>EXISTS</c> on every later
    /// start rather than a scan of the catalogue.
    /// </para>
    /// </summary>
    private async Task BackfillDemoImagesAsync(CancellationToken ct)
    {
        if (await _db.Products.AnyAsync(p => p.StockCode.StartsWith(StockCodePrefix) && p.HasImage, ct))
        {
            return;
        }

        var products = await _db.Products
            .Where(p => p.StockCode.StartsWith(StockCodePrefix) && !p.IsDeleted)
            .OrderBy(p => p.StockCode)
            .ToListAsync(ct);

        var added = 0;

        for (var index = 0; index < products.Count; index++)
        {
            // The same two-in-three spread the fresh seed uses, so a topped-up bench and a new one
            // show the same mix of photographed and unphotographed items.
            if ((index + 1) % 3 == 0)
            {
                continue;
            }

            var product = products[index];

            var image = ProductImage.Create(
                product.Id, DemoImageFactory.Create(product.StockCode), DemoImageFactory.ContentType);

            if (image.IsFailure)
            {
                continue;
            }

            _db.ProductImages.Add(image.Value);
            product.SetHasImage(true);
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Demo catalogue topped up with {Count} pictures.", added);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Structure
    // ---------------------------------------------------------------------------------------------

    /// <summary>The trading departments, with the margin each one is normally run at.</summary>
    private static readonly (string Code, string Name, decimal Margin)[] DepartmentPlan =
    [
        ("GROC", "Grocery", 0.24m),
        ("PROD", "Fresh produce", 0.32m),
        ("BEVG", "Beverages", 0.28m),
        ("HHLD", "Household", 0.35m),
        ("HLTH", "Health and beauty", 0.42m),
        ("ELEC", "Electronics", 0.18m),
        ("APRL", "Apparel", 0.52m),
        ("HDWR", "Hardware", 0.38m),
    ];

    private static readonly (string Department, string Code, string Name)[] CategoryPlan =
    [
        ("GROC", "GROC-DRY", "Dry goods"),
        ("GROC", "GROC-CAN", "Canned and jarred"),
        ("GROC", "GROC-SNK", "Snacks"),
        ("PROD", "PROD-FRT", "Fruit"),
        ("PROD", "PROD-VEG", "Vegetables"),
        ("BEVG", "BEVG-HOT", "Tea and coffee"),
        ("BEVG", "BEVG-SFT", "Soft drinks"),
        ("HHLD", "HHLD-CLN", "Cleaning"),
        ("HHLD", "HHLD-PPR", "Paper goods"),
        ("HLTH", "HLTH-PRS", "Personal care"),
        ("HLTH", "HLTH-OTC", "Over the counter"),
        ("ELEC", "ELEC-ACC", "Accessories"),
        ("ELEC", "ELEC-AUD", "Audio"),
        ("APRL", "APRL-TOP", "Tops"),
        ("APRL", "APRL-OUT", "Outerwear"),
        ("HDWR", "HDWR-TOL", "Hand tools"),
        ("HDWR", "HDWR-FIX", "Fixings"),
    ];

    private async Task<Dictionary<string, Guid>> SeedDepartmentsAsync(Guid locationId, CancellationToken ct)
    {
        var existing = await _db.Departments
            .Where(d => d.LocationId == locationId)
            .ToDictionaryAsync(d => d.Code ?? d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase, ct);

        var order = 0;

        foreach (var (code, name, _) in DepartmentPlan)
        {
            order += 10;

            if (existing.ContainsKey(code))
            {
                continue;
            }

            var created = Department.Create(locationId, name, code, order);
            if (created.IsFailure)
            {
                _logger.LogWarning("Demo department {Code} rejected: {Error}", code, created.Error.Code);
                continue;
            }

            _db.Departments.Add(created.Value);
            existing[code] = created.Value.Id;
        }

        return existing;
    }

    private async Task<Dictionary<string, Guid>> SeedCategoriesAsync(Guid locationId, CancellationToken ct)
    {
        var existing = await _db.Categories
            .Where(c => c.LocationId == locationId)
            .ToDictionaryAsync(c => c.Code ?? c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);

        var order = 0;

        foreach (var (_, code, name) in CategoryPlan)
        {
            order += 10;

            if (existing.ContainsKey(code))
            {
                continue;
            }

            var created = Category.Create(locationId, name, code, order);
            if (created.IsFailure)
            {
                _logger.LogWarning("Demo category {Code} rejected: {Error}", code, created.Error.Code);
                continue;
            }

            _db.Categories.Add(created.Value);
            existing[code] = created.Value.Id;
        }

        return existing;
    }

    // ---------------------------------------------------------------------------------------------
    // Merchandise
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Names are assembled from a modifier and a noun per category rather than listed one by one.
    /// Eight items per category over seventeen categories is 136 products from thirty-odd words, and
    /// the result still reads like a shelf rather than "Product 47".
    /// </summary>
    private static readonly Dictionary<string, (string[] Modifiers, string[] Nouns)> Vocabulary = new(StringComparer.Ordinal)
    {
        ["GROC-DRY"] = (["Long grain", "Basmati", "Wholemeal", "Plain", "Self-raising", "Pearl", "Red split", "Rolled"], ["rice 1kg", "flour 1.5kg", "barley 500g", "lentils 500g", "oats 1kg", "pasta 500g", "couscous 400g", "quinoa 500g"]),
        ["GROC-CAN"] = (["Chopped", "Whole", "Baked", "Garden", "Tuna in", "Sliced", "Cream of", "New"], ["tomatoes 400g", "beans 415g", "peas 300g", "peaches 410g", "spring water 185g", "mushrooms 400g", "chicken soup 400g", "potatoes 540g"]),
        ["GROC-SNK"] = (["Salted", "Ready salted", "Milk", "Dark", "Roasted", "Honey", "Sea salt", "Salt and vinegar"], ["peanuts 200g", "crisps 150g", "chocolate 100g", "chocolate 90g", "cashews 150g", "oat bar 6pk", "pretzels 175g", "crisps 150g"]),
        ["PROD-FRT"] = (["Royal Gala", "Pink Lady", "Cavendish", "Navel", "Seedless", "Hass", "Kent", "Ruby"], ["apples 1kg", "apples 1kg", "bananas 1kg", "oranges 2kg", "grapes 500g", "avocado each", "mango each", "grapefruit each"]),
        ["PROD-VEG"] = (["Brushed", "Sweet", "Dutch", "Spanish", "Baby", "Iceberg", "Continental", "Truss"], ["potatoes 2kg", "potatoes 1kg", "carrots 1kg", "onions 1kg", "spinach 120g", "lettuce each", "cucumber each", "tomatoes 500g"]),
        ["BEVG-HOT"] = (["Ground", "Instant", "Whole bean", "English breakfast", "Earl Grey", "Green", "Decaf", "Peppermint"], ["coffee 250g", "coffee 100g", "coffee 1kg", "tea 80pk", "tea 50pk", "tea 40pk", "coffee 200g", "tea 30pk"]),
        ["BEVG-SFT"] = (["Sparkling", "Still", "Cloudy", "Diet", "Zero sugar", "Original", "Ginger", "Cranberry"], ["water 1.5L", "water 1.5L", "apple juice 1L", "cola 2L", "cola 1.25L", "lemonade 2L", "beer 4pk", "juice 1L"]),
        ["HHLD-CLN"] = (["Concentrated", "Antibacterial", "Lemon", "Original", "Multi-surface", "Heavy duty", "Non-bio", "Fabric"], ["dish liquid 500ml", "spray 750ml", "floor cleaner 1L", "bleach 1L", "wipes 80pk", "scourers 6pk", "laundry liquid 2L", "softener 1L"]),
        ["HHLD-PPR"] = (["3-ply", "2-ply", "Extra long", "Kitchen", "Recycled", "Quilted", "Facial", "Heavy"], ["toilet tissue 9pk", "toilet tissue 12pk", "toilet tissue 6pk", "towel 4pk", "towel 2pk", "tissue 4pk", "tissues 100pk", "bin liners 30pk"]),
        ["HLTH-PRS"] = (["Sensitive", "Whitening", "Daily", "Moisturising", "Anti-dandruff", "2-in-1", "Fresh", "Aloe"], ["toothpaste 110g", "toothpaste 110g", "shampoo 400ml", "body wash 500ml", "shampoo 350ml", "conditioner 400ml", "deodorant 150ml", "hand cream 75ml"]),
        ["HLTH-OTC"] = (["Soluble", "Fast acting", "Non-drowsy", "Extra strength", "Children's", "Chesty", "Effervescent", "High strength"], ["paracetamol 24pk", "ibuprofen 24pk", "antihistamine 30pk", "paracetamol 16pk", "syrup 100ml", "cough syrup 200ml", "vitamin C 20pk", "vitamin D 60pk"]),
        ["ELEC-ACC"] = (["Braided", "Fast charge", "Universal", "Compact", "Anti-glare", "Silicone", "Magnetic", "Retractable"], ["USB-C cable 2m", "wall charger 30W", "adaptor kit", "power bank 10000mAh", "screen protector", "phone case", "car mount", "cable 1m"]),
        ["ELEC-AUD"] = (["Wireless", "Noise cancelling", "In-ear", "Over-ear", "Bluetooth", "Portable", "Studio", "Sports"], ["earbuds", "headphones", "earphones", "headphones", "speaker", "speaker", "monitors", "earbuds"]),
        ["APRL-TOP"] = (["Cotton", "Long sleeve", "Striped", "Plain", "Oversized", "Fitted", "Linen", "Merino"], ["t-shirt S", "t-shirt M", "t-shirt L", "polo M", "sweatshirt L", "shirt M", "shirt L", "jumper M"]),
        ["APRL-OUT"] = (["Waterproof", "Padded", "Lightweight", "Fleece-lined", "Hooded", "Quilted", "Softshell", "Insulated"], ["jacket M", "jacket L", "gilet M", "parka L", "raincoat M", "jacket S", "jacket XL", "coat L"]),
        ["HDWR-TOL"] = (["Claw", "Adjustable", "Long nose", "Ratcheting", "Retractable", "Cross-head", "Insulated", "Folding"], ["hammer 16oz", "spanner 250mm", "pliers 160mm", "screwdriver set", "knife", "screwdriver PH2", "pliers 180mm", "saw 300mm"]),
        ["HDWR-FIX"] = (["Zinc plated", "Stainless", "Galvanised", "Hex head", "Wood", "Masonry", "Self-tapping", "Countersunk"], ["screws 4x40 100pk", "screws 4x30 100pk", "nails 50mm 500g", "bolts M8 20pk", "screws 5x60 50pk", "plugs 7mm 100pk", "screws 3.5x25 200pk", "screws 4x50 100pk"]),
    };

    private List<Product> SeedProducts(
        Guid locationId,
        Dictionary<string, Guid> departments,
        Dictionary<string, Guid> categories)
    {
        // Fixed seed. The catalogue has to be the same on every machine, or a bug reported against
        // DEMO-0042 cannot be reproduced.
        var random = new Random(25_2025);
        var products = new List<Product>();
        var sequence = 0;

        foreach (var (departmentCode, categoryCode, _) in CategoryPlan)
        {
            if (!departments.TryGetValue(departmentCode, out var departmentId) ||
                !categories.TryGetValue(categoryCode, out var categoryId) ||
                !Vocabulary.TryGetValue(categoryCode, out var words))
            {
                continue;
            }

            var margin = DepartmentPlan.First(d => d.Code == departmentCode).Margin;

            for (var i = 0; i < words.Nouns.Length; i++)
            {
                sequence++;

                var stockCode = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{StockCodePrefix}{sequence:D4}");

                var name = $"{words.Modifiers[i]} {words.Nouns[i]}";

                // Cost first, then price from the department margin — the direction a buyer actually
                // works in. Prices land on a .49/.95/.99 ending because a catalogue full of £4.37 is
                // an obvious tell that nobody set these.
                var cost = Math.Round((decimal)(random.NextDouble() * 18.0 + 0.6), 2);
                var raw = cost / (1m - margin);
                var price = Charm(raw);

                // Tagged lines are Serialized: they carry per-unit EPCs, and the till has to treat
                // them as individually identified rather than as a countable quantity.
                var type = TaggedDepartments.Contains(departmentCode, StringComparer.Ordinal)
                    ? ProductType.Serialized
                    : ProductType.Standard;

                var created = Product.Create(locationId, stockCode, name, type, price);
                if (created.IsFailure)
                {
                    _logger.LogWarning("Demo product {Code} rejected: {Error}", stockCode, created.Error.Code);
                    continue;
                }

                var product = created.Value;

                product.SetDepartment(departmentId);
                product.SetCategory(categoryId);
                product.UpdatePricing(price, cost, cost);
                product.UpdateDetails(name, null, Upc(sequence), Bin(departmentCode, sequence), null);

                var reorderPoint = random.Next(4, 20);
                product.UpdateOrdering(
                    baseStock: reorderPoint * 3,
                    reorderPoint: reorderPoint,
                    reorderQty: reorderPoint * 2,
                    caseQty: 1m,
                    shipWeight: Math.Round((decimal)(random.NextDouble() * 2.5 + 0.05), 3));

                // A spread that gives the reports something to find: most lines healthy, roughly one
                // in eight below its reorder point, and the occasional stock-out.
                var onHand = random.Next(10) switch
                {
                    0 => 0m,
                    1 => reorderPoint - random.Next(1, 4),
                    _ => reorderPoint + random.Next(2, 60),
                };

                onHand = Math.Max(0m, onHand);
                product.UpdateStockLevels(onHand, onOrder: 0m);

                _db.Products.Add(product);

                // The stock ledger's own row. Product.OnHand and StockLevel.OnHand are written in
                // lockstep everywhere else in the system; seeding is not an exception.
                var level = StockLevel.Create(product.Id, null, locationId);
                level.OnHand = onHand;
                _db.StockLevels.Add(level);

                AddTieredPrices(product, price);

                // Roughly two items in three get a picture. Not all of them, on purpose: a real shop
                // photographs its catalogue over months, and the till's grid has to look right when
                // some tiles have a photograph and the rest fall back to a monogram.
                if (sequence % 3 != 0)
                {
                    var image = ProductImage.Create(
                        product.Id, DemoImageFactory.Create(stockCode), DemoImageFactory.ContentType);

                    if (image.IsSuccess)
                    {
                        _db.ProductImages.Add(image.Value);
                        product.SetHasImage(true);
                    }
                }

                products.Add(product);
            }
        }

        return products;
    }

    /// <summary>
    /// Trade and wholesale rows. Retail is <see cref="Product.RegularPrice"/> and is not duplicated
    /// here — two places holding the shelf price is two places to disagree.
    /// </summary>
    private void AddTieredPrices(Product product, decimal retail)
    {
        foreach (var (level, discount) in new[] { (TradeLevel, TradeDiscount), (WholesaleLevel, WholesaleDiscount) })
        {
            var tier = ProductPrice.Create(
                product.Id,
                level,
                Math.Round(retail * (1m - discount), 2, MidpointRounding.AwayFromZero));

            if (tier.IsSuccess)
            {
                _db.ProductPrices.Add(tier.Value);
            }
        }
    }

    /// <summary>Rounds up to the nearest psychological price point rather than to a bare cent.</summary>
    private static decimal Charm(decimal raw)
    {
        var whole = Math.Floor(raw);
        var fraction = raw - whole;

        var ending = fraction switch
        {
            < 0.50m => 0.49m,
            < 0.96m => 0.95m,
            _ => 0.99m,
        };

        return Math.Max(0.49m, whole + ending);
    }

    /// <summary>A check-digit-correct EAN-13, so the barcode scanner path can be tested for real.</summary>
    private static string Upc(int sequence)
    {
        var body = string.Create(CultureInfo.InvariantCulture, $"250{sequence:D9}");

        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        var check = (10 - (sum % 10)) % 10;
        return body + check.ToString(CultureInfo.InvariantCulture);
    }

    private static string Bin(string departmentCode, int sequence)
        => string.Create(CultureInfo.InvariantCulture, $"{departmentCode[..2]}-{sequence / 10 + 1:D2}-{sequence % 10 + 1:D2}");

    // ---------------------------------------------------------------------------------------------
    // RFID
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Tags for the lines a shop would actually tag — apparel and electronics, where the unit value
    /// justifies the label — plus hardware, which is the awkward case worth having in test data
    /// because metal detunes the antenna.
    /// </summary>
    private static readonly string[] TaggedDepartments = ["APRL", "ELEC", "HDWR"];

    private int SeedSerializedUnits(Guid locationId, List<Product> products)
    {
        var taggedCategories = CategoryPlan
            .Where(c => TaggedDepartments.Contains(c.Department, StringComparer.Ordinal))
            .Select(c => c.Code)
            .ToHashSet(StringComparer.Ordinal);

        var categoryIds = _db.Categories.Local
            .Where(c => taggedCategories.Contains(c.Code ?? string.Empty))
            .Select(c => c.Id)
            .ToHashSet();

        var receivedOn = _clock.Now.AddDays(-30);
        var written = 0;
        var serial = 0;

        foreach (var product in products.Where(p => p.CategoryId is not null && categoryIds.Contains(p.CategoryId!.Value)))
        {
            for (var i = 0; i < UnitsPerTaggedProduct; i++)
            {
                serial++;

                var created = SerializedUnit.Create(
                    product.Id,
                    locationId,
                    string.Create(CultureInfo.InvariantCulture, $"SN{serial:D8}"),
                    Sgtin96(serial),
                    receivedOn);

                if (created.IsFailure)
                {
                    _logger.LogWarning("Demo EPC {Serial} rejected: {Error}", serial, created.Error.Code);
                    continue;
                }

                var unit = created.Value;

                // Provisioned tags are invisible to the till. Commissioning them is what a receiving
                // clerk does, and a demo database where nothing scans is not a demo.
                unit.Commission();

                _db.SerializedUnits.Add(unit);
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// A 96-bit SGTIN as 24 hex characters, which is what an ISO 18000-6C tag actually carries.
    /// <para>
    /// Header 0x30, filter 3 (single trade item), partition 5, then a company prefix, an item
    /// reference and a serial. The layout is right rather than merely 24 characters of hex, so
    /// anything downstream that decodes an EPC sees a well-formed one.
    /// </para>
    /// </summary>
    private static string Sgtin96(int serial)
    {
        const ulong Header = 0x30;
        const ulong Filter = 3;
        const ulong Partition = 5;
        const ulong CompanyPrefix = 9_521_234;  // 7 digits, per partition 5
        const ulong ItemReference = 250;        // 5 digits, per partition 5

        // High 64 bits: header(8) filter(3) partition(3) company(24) item(20) then the top 6 bits of
        // the 38-bit serial.
        var high = (Header << 56)
            | (Filter << 53)
            | (Partition << 50)
            | (CompanyPrefix << 26)
            | (ItemReference << 6)
            | ((ulong)serial >> 32);

        var low = (uint)serial;

        return high.ToString("X16", CultureInfo.InvariantCulture)
            + low.ToString("X8", CultureInfo.InvariantCulture);
    }
}
