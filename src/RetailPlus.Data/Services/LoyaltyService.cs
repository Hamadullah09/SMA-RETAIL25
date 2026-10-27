using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>
/// Bonus points as a history: every point earned, redeemed or corrected is a <see cref="LoyaltyTransaction"/>, and
/// the client's <see cref="Client.Points"/> is kept equal to their sum.
/// </summary>
public sealed class LoyaltyService(DataContext data)
{
    /// <summary>The client's points history, newest first.</summary>
    public async Task<List<LoyaltyTransaction>> HistoryAsync(Guid clientId)
    {
        await using var db = data.Create();
        return await db.LoyaltyTransactions.AsNoTracking().Where(t => t.ClientId == clientId).OrderByDescending(t => t.At).ThenByDescending(t => t.CreatedAtUtc).ToListAsync();
    }

    /// <summary>The client's points: the sum of the history.</summary>
    public async Task<decimal> TotalAsync(Guid clientId)
    {
        await using var db = data.Create();
        return (await db.LoyaltyTransactions.Where(t => t.ClientId == clientId).Select(t => t.Points).ToListAsync()).Sum();
    }

    /// <summary>A correction at the client screen: adds (or with a negative number takes off) points, with the reason.</summary>
    public async Task AdjustAsync(Guid clientId, decimal points, string? note, string? user)
    {
        if (points == 0) return;
        await using var db = data.Create();
        var client = await db.Clients.SingleAsync(c => c.Id == clientId);
        if (client.Points + points < 0) throw new BusinessRuleException($"The client has only {client.Points:0.##} points.");
        Record(db, client, LoyaltyTransactionKind.Adjust, points, null, user, note);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This client was changed at another station. Try again.");
        }
    }

    /// <summary>
    /// Adds a points row for a client tracked by <paramref name="db"/> and keeps the client's total in step (saved with
    /// the caller's next SaveChanges).
    /// </summary>
    internal static void Record(RetailPlusDbContext db, Client client, LoyaltyTransactionKind kind, decimal points, Sale? sale, string? user, string? note = null, DateTime? at = null)
    {
        if (points == 0) return;
        client.Points += points;
        db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            ClientId = client.Id, Kind = kind, Points = points, At = at ?? sale?.SoldAt ?? DateTime.Now, SaleId = sale?.Id, InvoiceNumber = sale?.InvoiceNumber,
            User = user is { Length: > 50 } ? user[..50] : user, Note = note is { Length: > 200 } ? note[..200] : note,
        });
    }
}
