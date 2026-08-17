using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Services;

/// <summary>
/// Puts a shopper on a self-checkout station and opens the basket there.
/// <para>
/// There are two ways in and they must not be two implementations. A shopper can name the station
/// they are standing at, or — the ordinary path now — simply sign in and be given one. Both end at
/// the same <see cref="ClaimAsync"/>, so the guarantees that matter (one live session per station,
/// a cashier's open sale is never handed to a customer, the race is lost safely) hold either way.
/// </para>
/// </summary>
public sealed class TrolleyAllocator
{
    public static readonly Error NotSignedIn =
        new("shopper.not_signed_in", "Sign in before connecting to a counter.");

    public static readonly Error CodeAmbiguous =
        new("trolley.code_ambiguous", "More than one shop has a counter with that number. Choose your store first.");

    public static readonly Error StationBusy =
        new("cart.station_busy", "That counter is mid-sale. Use another one, or ask staff to clear it.");

    public static readonly Error NotAShopperStation =
        new("trolley.not_a_shopper_station", "That counter is not available from the app.");

    /// <summary>
    /// Every self-checkout station is in use. A real condition in a busy shop rather than a fault, so
    /// it is worth its own message: the shopper should wait or use a staffed till, not retry.
    /// </summary>
    public static readonly Error NoStationFree =
        new("trolley.none_free", "Every self-checkout counter is busy right now. Please try again in a moment.");

    private readonly IApplicationDbContext _db;
    private readonly CartOpener _opener;
    private readonly ICartStore _store;
    private readonly IDateTime _clock;
    private readonly TrolleyOptions _options;

    public TrolleyAllocator(
        IApplicationDbContext db,
        CartOpener opener,
        ICartStore store,
        IDateTime clock,
        IOptions<TrolleyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _opener = opener;
        _store = store;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>
    /// Gives the shopper a station without being told which one — the self-checkout path.
    /// <para>
    /// Order matters twice over. An existing counter is always preferred to a new one, lowest code
    /// first, so a shop with 301–320 standing in it fills those before inventing 321; and the walk
    /// only creates a station once every existing one is taken, which is the "it increases
    /// automatically" behaviour asked for, bounded by the configured range so it can never grow into
    /// the numbers staffed tills use.
    /// </para>
    /// </summary>
    public async Task<Result<ShopperCartDto>> IssueNextFreeAsync(
        long shopperId,
        long? locationId,
        CancellationToken ct)
    {
        // Already shopping? Then this is a restart, a reconnect, or a second sign-in on a new handset,
        // and the answer is the basket they already have — not a second station holding nothing. This
        // is what stops a customer stranding a full trolley by killing the app.
        var live = await _db.TrolleySessions
            .FirstOrDefaultAsync(s => s.State == TrolleySessionState.Shopping && s.ShopperId == shopperId, ct);

        if (live is not null)
        {
            var held = await _db.Trolleys.FirstOrDefaultAsync(t => t.Id == live.TrolleyId, ct);

            if (held is not null)
            {
                return await ProjectAsync(live, held, ct);
            }
        }

        var candidates = await ClaimableCodesAsync(locationId, ct);

        foreach (var code in candidates)
        {
            var attempt = await ClaimAsync(shopperId, code, locationId, ct);

            if (attempt.IsSuccess)
            {
                return attempt;
            }

            // Taken between the shortlist and the attempt, or mid-sale at the till. Both mean "not
            // this one" rather than "this failed" — keep walking.
            if (!IsUnavailable(attempt.Error))
            {
                // Anything else is a genuine fault, and repeating it against ninety-nine more codes
                // would turn one clear error into a slow one.
                return attempt;
            }
        }

        return Result.Failure<ShopperCartDto>(NoStationFree
            .With("range", $"{_options.MinStationCode}-{_options.MaxStationCode}"));
    }

    /// <summary>
    /// Connects the shopper to the station whose code they gave, bringing it into service on first use.
    /// </summary>
    public async Task<Result<ShopperCartDto>> ClaimAsync(
        long shopperId,
        string? rawCode,
        long? locationId,
        CancellationToken ct)
    {
        var code = Trolley.NormalizeCode(rawCode);

        if (code.Length == 0)
        {
            return Result.Failure<ShopperCartDto>(Trolley.CodeInvalid.With("value", rawCode));
        }

        var resolved = await ResolveTrolleyAsync(code, locationId, ct);

        if (resolved.IsFailure)
        {
            return Result.Failure<ShopperCartDto>(resolved.Error);
        }

        var trolley = resolved.Value;

        if (!trolley.IsActive)
        {
            return Result.Failure<ShopperCartDto>(Trolley.OutOfService.With("code", code));
        }

        // Already shopping? Two cases that look alike and are not.
        var live = await _db.TrolleySessions
            .FirstOrDefaultAsync(
                s => s.State == TrolleySessionState.Shopping
                    && (s.ShopperId == shopperId || s.TrolleyId == trolley.Id),
                ct);

        if (live is not null)
        {
            // The same shopper, on the same trolley: the app restarted, or lost signal in an aisle,
            // and is asking to reconnect. Handing back the existing trip is the whole point of
            // storing it — refusing would strand a full basket.
            if (live.ShopperId == shopperId && live.TrolleyId == trolley.Id)
            {
                return await ProjectAsync(live, trolley, ct);
            }

            return Result.Failure<ShopperCartDto>(
                live.ShopperId == shopperId
                    ? TrolleySession.AlreadyShopping
                    : Trolley.AlreadyClaimed.With("code", code));
        }

        // A counter already ringing a sale is not free, and this is the check that says so.
        //
        // CartOpener deliberately hands back a station's existing cart rather than starting a second
        // one — that is what lets a till survive a browser refresh or an agent reconnect. Here, that
        // same helpful behaviour would hand a shopper whatever the cashier currently has on screen.
        // So the question has to be asked before the cart is opened, not after.
        //
        // The cart store is the authority, not the carts table: the table keeps finished rows around,
        // while the store is what "this station is mid-sale" actually means. Releasing a trolley
        // clears the station here, which is what makes a counter reusable.
        var openHere = await _store.GetByStationAsync(trolley.StationId, ct);

        if (openHere is { Cart.IsActive: true })
        {
            return Result.Failure<ShopperCartDto>(StationBusy.With("code", code));
        }

        // Staff id 0: nobody is serving this sale. See CartOpener.
        var cart = await _opener.OpenAsync(trolley.StationId, staffId: 0L, ct);

        if (cart.IsFailure)
        {
            return Result.Failure<ShopperCartDto>(cart.Error);
        }

        var session = TrolleySession.Claim(
            trolley.Id,
            shopperId,
            cart.Value.Id,
            trolley.LocationId,
            _clock.Now);

        _db.TrolleySessions.Add(session);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index fired: between the check above and this insert, another phone
            // claimed the same trolley. This is the race the index exists to lose safely — the second
            // shopper is told to take another trolley instead of silently joining the first one's
            // basket. On the self-checkout path the caller reads this as "not this one" and walks on.
            _db.TrolleySessions.Remove(session);

            return Result.Failure<ShopperCartDto>(Trolley.AlreadyClaimed.With("code", code));
        }

        return new ShopperCartDto(
            session.Id,
            trolley.Id,
            trolley.Code,
            session.State,
            cart.Value);
    }

    /// <summary>"Somebody else has it", as opposed to "it is broken".</summary>
    private static bool IsUnavailable(Error error)
        => error.Code is "trolley.already_claimed" or "cart.station_busy" or "trolley.out_of_service";

    /// <summary>
    /// The station codes worth trying, best first: every existing shopper station in ascending code
    /// order, then — only if the range has room above them — the next unused numbers.
    /// </summary>
    private async Task<IReadOnlyList<string>> ClaimableCodesAsync(long? locationId, CancellationToken ct)
    {
        var stations = await _db.Stations
            .Where(s => s.IsActive)
            .Where(s => locationId == null || s.LocationId == locationId)
            .Select(s => s.StationCode)
            .ToListAsync(ct);

        var existing = stations
            .Where(_options.IsClaimable)
            .Select(code => int.Parse(code, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(code => code)
            .ToList();

        var codes = existing
            .Select(code => code.ToString("000", CultureInfo.InvariantCulture))
            .ToList();

        if (!_options.AutoCreateStation)
        {
            return codes;
        }

        // Grow from above the highest counter that exists, never from the bottom of the range: a shop
        // whose counters start at 301 must not have a 300 conjured underneath them the first time
        // everything is busy.
        var next = existing.Count == 0 ? _options.MinStationCode : existing[^1] + 1;

        for (var code = next; code <= _options.MaxStationCode; code++)
        {
            codes.Add(code.ToString("000", CultureInfo.InvariantCulture));
        }

        return codes;
    }

    /// <summary>
    /// Resolves the station code the shopper typed, bringing it into service on first use.
    /// <para>
    /// The number is a <b>station code</b> — the counter the shopper is standing at, the same code
    /// staff administer in the setup screen. Existing rows are matched first; a code inside the
    /// shopper range that names a station nobody has used from the app yet gets registered, and a code
    /// inside the range naming no station at all creates one. That last step is the
    /// "increases automatically" behaviour: once 301–320 are taken, 321 comes into existence on
    /// demand rather than failing.
    /// </para>
    /// <para>
    /// Everything hinges on the range. Outside it, nothing is created and nothing is claimable, which
    /// is what keeps the front counter out of a shopper's hands.
    /// </para>
    /// </summary>
    private async Task<Result<Trolley>> ResolveTrolleyAsync(string code, long? locationId, CancellationToken ct)
    {
        var existing = await _db.Trolleys
            .Where(t => t.Code == code)
            .Where(t => locationId == null || t.LocationId == locationId)
            .Take(2)
            .ToListAsync(ct);

        // Two shops, same code, no store named. Guessing would put somebody's shopping on a counter
        // in another town, so it is reported instead.
        if (existing.Count > 1)
        {
            return Result.Failure<Trolley>(CodeAmbiguous.With("code", code));
        }

        if (existing.Count == 1)
        {
            return Result.Success(existing[0]);
        }

        // The number may be the station's id or its code, and both are accepted.
        //
        // Staff know these counters by id — 3 to 22 — because that is what the setup screen lists,
        // while the code (301-320) is what is printed on the counter itself. Refusing one of the two
        // would be refusing a number the shopper is looking straight at.
        var typedId = long.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : (long?)null;

        var station = await _db.Stations
            .Where(s => s.IsActive)
            .Where(s => s.StationCode == code || (typedId != null && s.Id == typedId))
            .Where(s => locationId == null || s.LocationId == locationId)
            .OrderBy(s => s.StationCode == code ? 0 : 1)
            .FirstOrDefaultAsync(ct);

        // The range is checked against the station's own code, never against what was typed. Reaching
        // the front counter by its id has to be refused exactly as reaching it by "001" is.
        if (station is not null && !_options.IsClaimable(station.StationCode))
        {
            return Result.Failure<Trolley>(NotAShopperStation
                .With("code", code)
                .With("range", $"{_options.MinStationCode}-{_options.MaxStationCode}"));
        }

        if (station is null && !_options.IsClaimable(code))
        {
            return Result.Failure<Trolley>(NotAShopperStation
                .With("code", code)
                .With("range", $"{_options.MinStationCode}-{_options.MaxStationCode}"));
        }

        if (station is null)
        {
            if (!_options.AutoCreateStation)
            {
                return Result.Failure<Trolley>(Trolley.NotFound.With("code", code));
            }

            var created = await CreateStationAsync(code, locationId, ct);

            if (created.IsFailure)
            {
                return Result.Failure<Trolley>(created.Error);
            }

            station = created.Value;
        }
        else if (!_options.AutoRegister)
        {
            return Result.Failure<Trolley>(Trolley.NotFound.With("code", code));
        }

        // Is this counter already registered? Asked by station, not by the number that was typed.
        //
        // The lookup at the top of this method matches on the trolley's code, which is the station's
        // code — "301". A shopper who types the station's id, "3", finds nothing there and would fall
        // through to registering a second trolley for a station that already has one. The unique index
        // then rejects it, and because the rejected entity stays tracked, the next SaveChanges inside
        // CartOpener re-throws the same violation and the whole claim 500s.
        var already = await _db.Trolleys.FirstOrDefaultAsync(t => t.StationId == station.Id, ct);

        if (already is not null)
        {
            return Result.Success(already);
        }

        var registration = Trolley.Create(station.LocationId, station.Id, station.StationCode, station.Name);

        if (registration.IsFailure)
        {
            return Result.Failure<Trolley>(registration.Error);
        }

        _db.Trolleys.Add(registration.Value);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two phones connecting to a brand-new counter in the same instant. The unique index
            // rejected the loser; the row it wanted now exists, so read it back rather than failing a
            // shopper for arriving second.
            //
            // Detaching first is not optional. A rejected insert stays in the change tracker as Added,
            // and every later SaveChanges on this same request — CartOpener's, in particular — tries
            // it again and fails again, turning a handled race into an unhandled 500.
            _db.Trolleys.Remove(registration.Value);

            var raced = await _db.Trolleys.FirstOrDefaultAsync(t => t.StationId == station.Id, ct);

            return raced is null
                ? Result.Failure<Trolley>(Trolley.NotFound.With("code", code))
                : Result.Success(raced);
        }

        return Result.Success(registration.Value);
    }

    /// <summary>
    /// Brings a new shopper counter into existence, copying its hardware profile from one that
    /// already works so tags read into it are debounced and priced exactly as everywhere else.
    /// </summary>
    private async Task<Result<Station>> CreateStationAsync(string code, long? locationId, CancellationToken ct)
    {
        // Modelled on a station that already exists rather than on defaults: the reader profile is
        // what makes RFID work at all, and a counter created without one reads nothing and says
        // nothing about why.
        var template = await _db.Stations
            .Where(s => s.IsActive && s.ReaderProfileId != null)
            .Where(s => locationId == null || s.LocationId == locationId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (template is null)
        {
            return Result.Failure<Station>(Trolley.NotFound.With("code", code));
        }

        var creation = Station.Create(template.LocationId, code, $"Counter {code}");

        if (creation.IsFailure)
        {
            return Result.Failure<Station>(creation.Error);
        }

        var station = creation.Value;

        station.AssignPeripherals(null, template.ReaderProfileId, null, null);

        // Continuous, because nobody presses "read" on a self-service counter — the whole promise is
        // that putting something down is the entire interaction.
        station.SetReaderMode(ReaderMode.Continuous);

        _db.Stations.Add(station);
        await _db.SaveChangesAsync(ct);

        return Result.Success(station);
    }

    private async Task<Result<ShopperCartDto>> ProjectAsync(
        TrolleySession session,
        Trolley trolley,
        CancellationToken ct)
    {
        var cart = await _opener.OpenAsync(trolley.StationId, staffId: 0L, ct);

        if (cart.IsFailure)
        {
            return Result.Failure<ShopperCartDto>(cart.Error);
        }

        // The station may have handed back a different cart than the session remembers — see
        // TrolleySession.AdoptCart. Left unreconciled, the phone subscribes to the cart it was told
        // about while the till fills a different one, and the live feed is silent for ever.
        if (cart.Value.Id != session.CartId)
        {
            session.AdoptCart(cart.Value.Id);
        }

        session.Touch(_clock.Now);
        await _db.SaveChangesAsync(ct);

        return new ShopperCartDto(
            session.Id,
            trolley.Id,
            trolley.Code,
            session.State,
            cart.Value);
    }
}
