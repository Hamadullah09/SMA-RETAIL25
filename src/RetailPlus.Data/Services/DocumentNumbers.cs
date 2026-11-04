namespace RetailPlus.Data.Services;

/// <summary>Document numbers for services that run at a till and on the server.</summary>
internal static class DocumentNumbers
{
    /// <summary>The station range the server numbers its own documents from; no till has it (tills are 1 to 999).</summary>
    public const int ServerStation = 1000;

    /// <summary>
    /// The next number of <paramref name="sequence"/>: from the till's own range, or on the server (which works as station 0
    /// of a store) from the server's range. The number is taken in <paramref name="db"/>'s own transaction either way.
    /// </summary>
    public static async Task<long> NextAsync(DataContext data, RetailPlusDbContext db, string sequence)
    {
        if (db.Station is { StationNumber: > 0 }) return await db.NextNumberAsync(sequence);
        var kept = db.Station;
        db.Station = new StationIdentity(kept?.StoreId ?? data.Station?.StoreId ?? Guid.Empty, ServerStation);
        try
        {
            return await db.NextNumberAsync(sequence);
        }
        finally
        {
            db.Station = kept;
        }
    }
}
