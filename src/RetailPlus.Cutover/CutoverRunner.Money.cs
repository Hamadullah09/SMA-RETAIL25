using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>The store a company-wide thing (a gift card, a client's points) is recorded at: the client's, or the first carried store.</summary>
    private CarriedStore HomeStore(long? customerId) =>
        StoreOf(_src["customers"].Find(customerId)?.Long("location_id")) ?? _stores.Values.First();

    /// <summary>
    /// Gift cards and gift certificates, each with its remaining value as one opening transaction (Retail25 keeps only the
    /// value left, not a history); and every client's reward points as one opening loyalty transaction.
    /// </summary>
    private async Task GiftCardsAndLoyaltyAsync(CancellationToken ct)
    {
        Log("Gift cards and loyalty points");
        var cardArea = _report.Area("Gift cards", "gift_cards, gift_certificates");
        var openingArea = _report.Area("Gift card balances", "gift_cards.remaining_value, gift_certificates.remaining_value");
        var cards = new List<GiftCard>();
        var openings = new List<GiftCardTransaction>();
        var possibleOpenings = new HashSet<Guid>();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, kind, prefix) in new[] { ("gift_cards", GiftCardKind.Card, "C"), ("gift_certificates", GiftCardKind.Certificate, "GC") })
            foreach (var row in _src[table].Rows.OrderBy(r => r.Id))
            {
                cardArea.Read++;
                openingArea.Read++;
                var client = ClientOf(row.LongOrNull("issued_to_customer_id"));
                if (_report.StoreCode is not null && client is null && row.LongOrNull("issued_to_customer_id") is not null)
                {
                    cardArea.Skip("client not carried over (--store-code)");
                    openingArea.Skip("client not carried over (--store-code)");
                    continue;
                }
                var number = GiftCardService.Normalize(row.Text("serial_number") ?? prefix + Number(row.Id));
                if (number.Length > 30) number = number[..30];
                if (!numbers.Add(number))
                {
                    var taken = number;
                    for (int n = 1; !numbers.Add(number); n++) number = Cut(prefix + (n == 1 ? "" : Number(n)) + "-" + taken, 30);
                    Note($"Retail25 {(kind == GiftCardKind.Card ? "gift card" : "gift certificate")} {taken} is number {number} here: a gift card or certificate already has that number.");
                }
                var store = HomeStore(row.LongOrNull("issued_to_customer_id"));
                decimal original = Money(row.Dec("original_value"), "GiftCard.OriginalValue"), left = Money(row.Dec("remaining_value"), "GiftCardTransaction.Amount");
                var issued = row.Date("issued_on") ?? DateOnly.FromDateTime(Local(store, row.Instant("created_at") ?? DateTimeOffset.UtcNow));
                var card = From(new GiftCard
                {
                    Id = StableIds.For(table, row.Id), StoreId = store.StoreId, Number = number, Kind = kind, IssuedAt = issued.ToDateTime(TimeOnly.MinValue),
                    ExpiresOn = row.Date("expires_on"), IsActive = row.Bool("is_active"), ClientId = client?.Id,
                    Note = $"Carried over from Retail25: issued for {original:0.00}",
                    CreatedAtUtc = Utc(row.Instant("created_at")),
                }, row);
                cards.Add(card);
                var openingId = StableIds.For(table + "-balance", row.Id);
                possibleOpenings.Add(openingId);
                if (left == 0) { openingArea.Skip("nothing left on it"); continue; }
                openings.Add(From(new GiftCardTransaction
                {
                    Id = openingId, StoreId = store.StoreId, GiftCardId = card.Id, Kind = GiftCardTransactionKind.Issue, Amount = left, At = card.IssuedAt,
                    Note = left == original ? "Carried over from Retail25" : $"Carried over from Retail25: {left:0.00} left of {original:0.00}",
                }, row));
            }
        await _w.UpsertAsync(cardArea, cards, ct);
        await _w.UpsertAsync(openingArea, openings, ct);
        await _w.RemoveMissingAsync<GiftCardTransaction>(openingArea, db => db.GiftCardTransactions.Where(t => possibleOpenings.Contains(t.Id)), openings.Select(t => t.Id).ToHashSet(), ct);

        var loyaltyArea = _report.Area("Loyalty points", "customer_pricing_profiles.reward_points");
        var points = new List<LoyaltyTransaction>();
        foreach (var (customerId, client) in _clients)
        {
            loyaltyArea.Read++;
            if (client.Client.Points == 0) { loyaltyArea.Skip("no points"); continue; }
            var store = HomeStore(customerId);
            points.Add(From(new LoyaltyTransaction
            {
                Id = StableIds.For("loyalty-opening", customerId), StoreId = store.StoreId, ClientId = client.Id, Kind = LoyaltyTransactionKind.Opening,
                Points = client.Client.Points, At = Local(store, DateTimeOffset.UtcNow), Note = "Points carried over from Retail25",
            }, client.Row));
        }
        await _w.UpsertAsync(loyaltyArea, points, ct, merge: (stored, mapped) => mapped.At = stored.At);
        var clientIds = _clients.Values.Select(c => c.Id).ToHashSet();
        await _w.RemoveMissingAsync<LoyaltyTransaction>(loyaltyArea,
            db => db.LoyaltyTransactions.Where(t => t.Kind == LoyaltyTransactionKind.Opening && clientIds.Contains(t.ClientId)), points.Select(p => p.Id).ToHashSet(), ct);
        int history = _src["loyalty_ledger_entries"].Count;
        if (history > 0)
            _report.NotCarriedOver.Add(new NotCarried("Loyalty ledger history (loyalty_ledger_entries)", history,
                "Each client's balance is carried over as one opening transaction; the earn and redeem history stays in Retail25 (each sale keeps its points earned and redeemed)."));
    }
}
