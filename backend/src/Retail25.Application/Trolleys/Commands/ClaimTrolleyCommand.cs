using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Services;
using Retail25.Application.Trolleys.Dtos;
using Retail25.Domain.Common;
using Retail25.Domain.Terminals;
using Retail25.Domain.Trolleys;

namespace Retail25.Application.Trolleys.Commands;

/// <summary>
/// Connects the phone to the trolley whose code the shopper typed, and opens the basket.
/// <para>
/// Carries no <c>[RequiresPermission]</c> and must never carry one. A shopper token resolves to the
/// empty permission set, so an attribute here would make the feature refuse everybody. Authorisation
/// is <see cref="ICurrentShopper"/> being non-null for the claim, and from then on the trolley
/// session row is the only thing that says which cart is yours.
/// </para>
/// </summary>
/// <param name="LocationId">
/// Optional. Codes are unique per shop, not globally, so a chain with a trolley 482 in two branches
/// needs to know which. Where the app knows the store â€” scanned at the door, or the only store there
/// is â€” it says so, and an ambiguous code is reported rather than guessed.
/// </param>
public sealed record ClaimTrolleyCommand(string? Code, long? LocationId = null)
    : IRequest<Result<ShopperCartDto>>;

public sealed class ClaimTrolleyHandler : IRequestHandler<ClaimTrolleyCommand, Result<ShopperCartDto>>
{
    public static readonly Error NotSignedIn =
        new("shopper.not_signed_in", "Sign in before connecting to a counter.");

    public static readonly Error CodeAmbiguous =
        new("trolley.code_ambiguous", "More than one shop has a counter with that number. Choose your store first.");

    public static readonly Error StationBusy =
        new("cart.station_busy", "That counter is mid-sale. Use another one, or ask staff to clear it.");

    public static readonly Error NotAShopperStation =
        new("trolley.not_a_shopper_station", "That counter is not available from the app.");

    private readonly IApplicationDbContext _db;
    private readonly ICurrentShopper _shopper;
    private readonly CartOpener _opener;
    private readonly ICartStore _store;
    private readonly IDateTime _clock;
    private readonly TrolleyOptions _options;

    public ClaimTrolleyHandler(
        IApplicationDbContext db,
        ICurrentShopper shopper,
        CartOpener opener,
        ICartStore store,
        IDateTime clock,
        IOptions<TrolleyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = db;
        _shopper = shopper;
        _opener = opener;
        _store = store;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<ShopperCartDto>> Handle(ClaimTrolleyCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_shopper.ShopperId is not { } shopperId)
        {
            return Result.Failure<ShopperCartDto>(NotSignedIn);
        }

        var code = Trolley.NormalizeCode(request.Code);

        if (code.Length == 0)
        {
            return Result.Failure<ShopperCartDto>(Trolley.CodeInvalid.With("value", request.Code));
        }

        var resolved = await ResolveTrolleyAsync(code, request.LocationId, ct);

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
            // storing it â€” refusing would strand a full basket.
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
        // one â€” that is what lets a till survive a browser refresh or an agent reconnect. Here, that
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
            // claimed the same trolley. This is the race the index exists to lose safely â€” the second
            // shopper is told to take another trolley instead of silently joining the first one's
            // basket.
            return Result.Failure<ShopperCartDto>(Trolley.AlreadyClaimed.With("code", code));
        }

        return new ShopperCartDto(
            session.Id,
            trolley.Id,
            trolley.Code,
            session.State,
            cart.Value);
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
