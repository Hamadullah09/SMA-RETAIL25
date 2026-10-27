using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the Gift cards list.</summary>
public sealed record GiftCardRow(Guid Id, string Number, GiftCardKind Kind, DateTime IssuedAt, DateOnly? ExpiresOn, bool IsActive, string? Client,
    decimal Balance, DateTime? LastUsed);

/// <summary>A gift card with its balance and history, newest first.</summary>
public sealed record GiftCardDetail(GiftCard Card, decimal Balance, IReadOnlyList<GiftCardTransaction> History);

/// <summary>
/// Gift cards and gift certificates: selling (issue and reload) and spending them happen inside the sale
/// (<see cref="SalesService"/>); this service finds cards, checks balances and makes corrections.
/// <para>
/// A till works on its own database: it knows the cards it issued itself and those the last download from the server
/// brought. A card sold at another till since then is unknown here, and its balance here does not show what was spent
/// elsewhere since the last download.
/// </para>
/// </summary>
public sealed class GiftCardService(DataContext data)
{
    /// <summary>The help text the screens show about cards issued or spent at other tills.</summary>
    public const string OfflineNote =
        "This till knows the cards it sold itself and those the last download from the server brought. A card sold at another till "
        + "since then is not known here yet, and a balance does not show what was spent at other tills since the last download.";

    /// <summary>Card numbers are kept without spaces or dashes, in capitals.</summary>
    public static string Normalize(string number) =>
        string.Concat(number.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();

    public async Task<List<GiftCardRow>> SearchAsync(string? text, bool activeOnly = false, int take = 1000)
    {
        await using var db = data.Create();
        var q = db.GiftCards.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = Normalize(text);
            q = q.Where(c => c.Number.StartsWith(t) || (c.Client != null && ((c.Client.LastName ?? "").StartsWith(text.Trim()) || (c.Client.Company ?? "").StartsWith(text.Trim()))));
        }
        var cards = await q.OrderByDescending(c => c.IssuedAt).Take(take)
            .Select(c => new
            {
                c.Id, c.Number, c.Kind, c.IssuedAt, c.ExpiresOn, c.IsActive,
                Client = c.Client == null ? null : c.Client.Company ?? (c.Client.FirstName + " " + c.Client.LastName),
            }).ToListAsync();
        var ids = cards.Select(c => c.Id).ToList();
        // Added up in memory: SQLite cannot sum decimals.
        var moves = (await db.GiftCardTransactions.AsNoTracking().Where(t => ids.Contains(t.GiftCardId)).Select(t => new { t.GiftCardId, t.Amount, t.At }).ToListAsync())
            .GroupBy(t => t.GiftCardId).ToDictionary(g => g.Key, g => (Balance: g.Sum(t => t.Amount), Last: g.Max(t => t.At)));
        return cards.Select(c =>
        {
            var m = moves.GetValueOrDefault(c.Id);
            return new GiftCardRow(c.Id, c.Number, c.Kind, c.IssuedAt, c.ExpiresOn, c.IsActive, c.Client?.Trim(), m.Balance, moves.ContainsKey(c.Id) ? m.Last : null);
        }).ToList();
    }

    /// <summary>The card with this number, its balance and history; null when this database does not know it.</summary>
    public async Task<GiftCardDetail?> FindAsync(string number)
    {
        var n = Normalize(number);
        await using var db = data.Create();
        var card = await db.GiftCards.AsNoTracking().Include(c => c.Client).SingleOrDefaultAsync(c => c.Number == n);
        if (card is null) return null;
        var history = await db.GiftCardTransactions.AsNoTracking().Where(t => t.GiftCardId == card.Id).OrderByDescending(t => t.At).ThenByDescending(t => t.CreatedAtUtc).ToListAsync();
        return new GiftCardDetail(card, history.Sum(t => t.Amount), history);
    }

    /// <summary>
    /// Checks that a card can pay <paramref name="amount"/> (it is known here, active, not expired, and has enough on it)
    /// and returns its balance. Throws with a message for the cashier otherwise.
    /// </summary>
    public async Task<decimal> CheckPaymentAsync(string number, decimal amount)
    {
        await using var db = data.Create();
        var card = await KnownCardAsync(db, number);
        decimal balance = await BalanceAsync(db, card.Id);
        CheckSpendable(card, balance, amount, DateOnly.FromDateTime(DateTime.Today));
        return balance;
    }

    /// <summary>Checks a card can be sold (a new number) or reloaded with <paramref name="amount"/>; returns the card when it exists.</summary>
    public async Task<GiftCardDetail?> CheckSaleAsync(string number, decimal amount)
    {
        var settings = await StoreSettingsAsync();
        await using var db = data.Create();
        var n = Normalize(number);
        if (n.Length == 0) throw new BusinessRuleException("Scan or type the card number.");
        var card = await db.GiftCards.AsNoTracking().SingleOrDefaultAsync(c => c.Number == n);
        decimal balance = card is null ? 0 : await BalanceAsync(db, card.Id);
        if (card is not null) CheckReloadable(card);
        CheckMaximum(settings, n, balance + amount);
        return await FindAsync(n);
    }

    /// <summary>A number for a new gift certificate (from this station's range).</summary>
    public async Task<string> NewNumberAsync()
    {
        await using var db = data.Create();
        return (await db.NextNumberAsync(RetailPlusDbContext.Sequences.GiftCardNumbers)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A correction by a supervisor: adds (or with a negative amount takes off) money, with the reason.</summary>
    public async Task AdjustAsync(Guid cardId, decimal amount, string? note, string? user)
    {
        if (amount == 0) return;
        var settings = await StoreSettingsAsync();
        await using var db = data.Create();
        var card = await db.GiftCards.SingleAsync(c => c.Id == cardId);
        decimal balance = await BalanceAsync(db, cardId) + amount;
        if (balance < 0) throw new BusinessRuleException($"Card {card.Number} would go below zero.");
        if (amount > 0) CheckMaximum(settings, card.Number, balance);
        db.GiftCardTransactions.Add(new GiftCardTransaction
        {
            GiftCardId = cardId, Kind = GiftCardTransactionKind.Adjust, Amount = amount, At = DateTime.Now, User = Trim(user, 50), Note = Trim(note, 200),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Deactivates a card (lost, stolen) or makes it active again.</summary>
    public async Task SetActiveAsync(Guid cardId, bool active)
    {
        await using var db = data.Create();
        var card = await db.GiftCards.SingleAsync(c => c.Id == cardId);
        card.IsActive = active;
        await db.SaveChangesAsync();
    }

    public async Task SetExpiryAsync(Guid cardId, DateOnly? expires)
    {
        await using var db = data.Create();
        var card = await db.GiftCards.SingleAsync(c => c.Id == cardId);
        card.ExpiresOn = expires;
        await db.SaveChangesAsync();
    }

    private async Task<StoreSettings> StoreSettingsAsync()
    {
        await using var db = data.Create();
        return await db.CurrentStoreSettings.AsNoTracking().FirstOrDefaultAsync() ?? new StoreSettings();
    }

    // Inside a sale's transaction ----------------------------------------------------------------------------------

    /// <summary>
    /// Sells a card: a new number is issued with the amount, a known card is reloaded. Certificates are not reloaded,
    /// deactivated or expired cards cannot be, and no card may hold more than the store's gift card maximum.
    /// </summary>
    internal static async Task IssueOrReloadAsync(RetailPlusDbContext db, StoreSettings settings, string number, GiftCardKind kind, decimal amount, Sale sale)
    {
        var n = Normalize(number);
        if (n.Length == 0) throw new BusinessRuleException("A gift card line has no card number.");
        if (amount <= 0) throw new BusinessRuleException($"Gift card {n}: the amount must be above zero.");
        var card = db.GiftCards.Local.FirstOrDefault(c => c.Number == n) ?? await db.GiftCards.SingleOrDefaultAsync(c => c.Number == n);
        decimal balance = 0;
        var transactionKind = GiftCardTransactionKind.Reload;
        if (card is null)
        {
            card = new GiftCard { Number = n, Kind = kind, IssuedAt = sale.SoldAt, ClientId = sale.ClientId, IsActive = true };
            db.GiftCards.Add(card);
            transactionKind = GiftCardTransactionKind.Issue;
        }
        else
        {
            CheckReloadable(card);
            balance = await BalanceAsync(db, card.Id);
        }
        CheckMaximum(settings, n, balance + amount);
        db.GiftCardTransactions.Add(new GiftCardTransaction
        {
            GiftCardId = card.Id, Kind = transactionKind, Amount = amount, At = sale.SoldAt, SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, User = Trim(sale.Staff, 50),
        });
    }

    /// <summary>A payment by the card (redeem), refused when the card cannot pay it all.</summary>
    internal static async Task RedeemAsync(RetailPlusDbContext db, string number, decimal amount, Sale sale)
    {
        var card = await KnownCardAsync(db, number);
        decimal balance = await BalanceAsync(db, card.Id);
        CheckSpendable(card, balance, amount, DateOnly.FromDateTime(sale.SoldAt));
        db.GiftCardTransactions.Add(new GiftCardTransaction
        {
            GiftCardId = card.Id, Kind = GiftCardTransactionKind.Redeem, Amount = -amount, At = sale.SoldAt, SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, User = Trim(sale.Staff, 50),
        });
    }

    /// <summary>Money put back on a card: a refund paid to the card, or a returned card line taken off it (negative).</summary>
    internal static async Task RefundAsync(RetailPlusDbContext db, StoreSettings settings, string number, decimal amount, Sale sale, GiftCardTransactionKind kind, string? note = null)
    {
        var card = await KnownCardAsync(db, number);
        decimal balance = await BalanceAsync(db, card.Id) + amount;
        if (balance < 0) throw new BusinessRuleException($"Gift card {card.Number} has only {balance - amount:N2} on it.");
        if (amount > 0) CheckMaximum(settings, card.Number, balance);
        db.GiftCardTransactions.Add(new GiftCardTransaction
        {
            GiftCardId = card.Id, Kind = kind, Amount = amount, At = sale.SoldAt, SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, User = Trim(sale.Staff, 50), Note = note,
        });
    }

    /// <summary>
    /// Undoes what a voided sale did to gift cards. Refused when a card it loaded has been spent since, because the
    /// money is no longer on it. A card the sale issued is deactivated.
    /// </summary>
    internal static async Task ReverseSaleAsync(RetailPlusDbContext db, Sale sale, string? user)
    {
        var done = await db.GiftCardTransactions.AsNoTracking().Where(t => t.SaleId == sale.Id).ToListAsync();
        foreach (var group in done.GroupBy(t => t.GiftCardId))
        {
            decimal amount = -group.Sum(t => t.Amount);
            if (amount == 0) continue;
            var card = await db.GiftCards.SingleAsync(c => c.Id == group.Key);
            decimal balance = await BalanceAsync(db, card.Id);
            if (balance + amount < 0)
                throw new BusinessRuleException($"Gift card {card.Number} has been spent since this sale ({balance:N2} left), so the sale cannot be voided.");
            db.GiftCardTransactions.Add(new GiftCardTransaction
            {
                GiftCardId = card.Id, Kind = GiftCardTransactionKind.Adjust, Amount = amount, At = DateTime.Now, SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber,
                User = Trim(user, 50), Note = $"Invoice {sale.InvoiceNumber} voided",
            });
            if (group.Any(t => t.Kind == GiftCardTransactionKind.Issue)) card.IsActive = false;
        }
    }

    private static async Task<GiftCard> KnownCardAsync(RetailPlusDbContext db, string number)
    {
        var n = Normalize(number);
        if (n.Length == 0) throw new BusinessRuleException("Scan or type the gift card number.");
        return db.GiftCards.Local.FirstOrDefault(c => c.Number == n) ?? await db.GiftCards.SingleOrDefaultAsync(c => c.Number == n)
               ?? throw new BusinessRuleException($"Gift card {n} is not known at this till. A card sold at another till is known here after the next download from the server.");
    }

    /// <summary>The balance: the sum of the card's transactions (added up in memory, as SQLite cannot sum decimals).</summary>
    internal static async Task<decimal> BalanceAsync(RetailPlusDbContext db, Guid cardId)
    {
        var saved = (await db.GiftCardTransactions.AsNoTracking().Where(t => t.GiftCardId == cardId).Select(t => t.Amount).ToListAsync()).Sum();
        // Transactions added in this context and not saved yet count too (two lines of one sale on the same card).
        return saved + db.GiftCardTransactions.Local.Where(t => t.GiftCardId == cardId && db.Entry(t).State == EntityState.Added).Sum(t => t.Amount);
    }

    private static void CheckSpendable(GiftCard card, decimal balance, decimal amount, DateOnly today)
    {
        if (!card.IsActive) throw new BusinessRuleException($"Gift card {card.Number} is deactivated and cannot be used.");
        if (card.ExpiresOn is DateOnly expires && expires < today) throw new BusinessRuleException($"Gift card {card.Number} expired on {expires:d}.");
        if (amount > balance) throw new BusinessRuleException($"Gift card {card.Number} has only {balance:N2} on it; {amount:N2} cannot be paid with it.");
    }

    private static void CheckReloadable(GiftCard card)
    {
        if (!card.IsActive) throw new BusinessRuleException($"Gift card {card.Number} is deactivated.");
        if (card.Kind == GiftCardKind.Certificate) throw new BusinessRuleException($"{card.Number} is a gift certificate; a certificate is not reloaded. Sell a new one.");
        if (card.ExpiresOn is DateOnly expires && expires < DateOnly.FromDateTime(DateTime.Today)) throw new BusinessRuleException($"Gift card {card.Number} expired on {expires:d}.");
    }

    private static void CheckMaximum(StoreSettings settings, string number, decimal balance)
    {
        if (settings.GiftCardMaximum > 0 && balance > settings.GiftCardMaximum)
            throw new BusinessRuleException($"A gift card can hold at most {settings.GiftCardMaximum:N2}; card {number} would hold {balance:N2}.");
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
