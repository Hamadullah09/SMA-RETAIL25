using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>A Retail25 location carried over: the store and the location it became, and its clock.</summary>
    private sealed record CarriedStore(Guid StoreId, Guid LocationId, string Code, TimeZoneInfo Zone, TimeSpan DayStart, SourceRow Row);

    /// <summary>Carried locations by Retail25 location id.</summary>
    private readonly Dictionary<long, CarriedStore> _stores = [];

    /// <summary>Station numbers by Retail25 station id.</summary>
    private readonly Dictionary<long, int> _stations = [];

    private CarriedStore? StoreOf(long? locationId) => locationId is long id ? _stores.GetValueOrDefault(id) : null;

    /// <summary>A Retail25 instant as the store's local time, to the second (the new model's business times).</summary>
    private static DateTime Local(CarriedStore store, DateTimeOffset at) => Seconds(TimeZoneInfo.ConvertTime(at, store.Zone).DateTime);

    /// <summary>
    /// One Retail25 location is one store with one stock location, both with the location's three-letter code. A store
    /// or location already in the target with that code is used as it is (its id kept).
    /// </summary>
    private async Task StoresAsync(CancellationToken ct)
    {
        Log("Stores, locations and stations");
        var storeArea = _report.Area("Stores", "locations");
        var locationArea = _report.Area("Locations", "locations");
        Dictionary<string, Guid> storesByCode;
        Dictionary<(Guid, string), Guid> locationsByCode;
        await using (var db = _w.NewContext())
        {
            storesByCode = await db.Stores.ToDictionaryAsync(s => s.Code.ToUpperInvariant(), s => s.Id, ct);
            locationsByCode = (await db.Locations.Select(l => new { l.Id, l.StoreId, l.Code }).ToListAsync(ct))
                .ToDictionary(l => (l.StoreId, l.Code.ToUpperInvariant()), l => l.Id);
        }

        var stores = new List<Store>();
        var locations = new List<Location>();
        foreach (var row in _src["locations"].Rows.OrderBy(r => r.Id))
        {
            storeArea.Read++;
            locationArea.Read++;
            var code = (row.Text("legacy_code") ?? "L" + Number(row.Id % 100)).ToUpperInvariant();
            if (_report.StoreCode is { } only && code != only)
            {
                storeArea.Skip($"not store {only} (--store-code)");
                locationArea.Skip($"not store {only} (--store-code)");
                continue;
            }
            if (row.Bool("is_deleted")) Warn($"locations {row.Id} ({code}) is deleted in Retail25; it is carried over with its history all the same");
            var (zone, zoneId) = FindZone(row.Text("time_zone_id"), code);
            var storeId = storesByCode.GetValueOrDefault(code, StableIds.For("store", row.Id));
            var locationId = locationsByCode.GetValueOrDefault((storeId, code), StableIds.For("location", row.Id));
            stores.Add(From(new Store { Id = storeId, Code = code, Name = row.Text("name") ?? code, TimeZoneId = zoneId, CreatedAtUtc = Utc(row.Instant("created_at")) }, row));
            locations.Add(From(new Location { Id = locationId, StoreId = storeId, Code = code, Name = row.Text("name"), CreatedAtUtc = Utc(row.Instant("created_at")) }, row));
            _stores[row.Id] = new CarriedStore(storeId, locationId, code, zone, row.TimeOfDay("business_day_start") ?? TimeSpan.Zero, row);
        }
        if (_stores.Count == 0)
            throw new CutoverRefusedException(_report.StoreCode is { } wanted
                ? $"Retail25 has no location with the code {wanted}."
                : "Retail25 has no locations.");
        await _w.UpsertAsync(storeArea, stores, ct);
        await _w.UpsertAsync(locationArea, locations, ct);

        await StationsAsync(ct);
        await SettingsAsync(ct);
        await BrandingAsync(ct);
    }

    /// <summary>Retail25 keeps IANA zone names; the new model keeps the Windows id (both work for converting times).</summary>
    private (TimeZoneInfo Zone, string Id) FindZone(string? id, string code)
    {
        if (id is null) return (TimeZoneInfo.Utc, "UTC");
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return (zone, TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) ? windows : zone.Id);
        }
        catch (TimeZoneNotFoundException)
        {
            Warn($"Store {code}: time zone \"{id}\" is not known on this computer; the computer's own zone is used");
            return (TimeZoneInfo.Local, TimeZoneInfo.Local.Id);
        }
    }

    /// <summary>
    /// Stations keep their Retail25 number (station code 001 is station 1). Station numbers are unique across the company
    /// here, because each one numbers documents from its own range; a number already taken gets the next free one.
    /// </summary>
    private async Task StationsAsync(CancellationToken ct)
    {
        var area = _report.Area("Stations", "stations");
        Dictionary<int, Guid> taken;
        await using (var db = _w.NewContext())
            taken = await db.Stations.ToDictionaryAsync(s => s.Number, s => s.Id, ct);
        var rows = new List<Station>();
        foreach (var row in _src["stations"].Rows.OrderBy(r => r.Long("location_id")).ThenBy(r => r.Id))
        {
            area.Read++;
            if (StoreOf(row.Long("location_id")) is not { } store) { area.Skip("store not carried over"); continue; }
            var id = StableIds.For("station", row.Id);
            var code = row.Text("station_code");
            int number = int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 999 ? n : 0;
            if (number == 0 || (taken.TryGetValue(number, out var holder) && holder != id))
            {
                int wanted = number;
                // The number an earlier run gave the station, or else the first free one.
                number = taken.Where(t => t.Value == id).Select(t => t.Key).FirstOrDefault();
                if (number == 0) number = Enumerable.Range(1, 999).FirstOrDefault(k => !taken.ContainsKey(k));
                if (number == 0) { area.Skip("no station number left (1-999)"); continue; }
                Note($"Retail25 station {code} of {store.Code} is station {number} here ({(wanted == 0 ? "its code is not a number from 1 to 999" : $"station {wanted} is another store's")}).");
            }
            taken[number] = id;
            _stations[row.Id] = number;
            rows.Add(From(new Station { Id = id, StoreId = store.StoreId, Number = number, Name = row.Text("name") ?? $"Station {number:000}", CreatedAtUtc = Utc(row.Instant("created_at")) }, row));
            if (!row.Bool("is_active")) Warn($"stations {row.Id} ({store.Code} {code}) is not active in Retail25; carried over as station {number}");
        }
        await _w.UpsertAsync(area, rows, ct);
    }

    /// <summary>Retail25's tax configuration in effect on <paramref name="day"/> for a location (the latest that started).</summary>
    private SourceRow? TaxOn(long locationId, DateOnly day) => _src["tax_configurations"].Rows
        .Where(t => t.Long("location_id") == locationId && t.Date("effective_from") <= day && (t.Date("effective_to") is not { } to || to >= day))
        .OrderByDescending(t => t.Date("effective_from")).ThenByDescending(t => t.Id).FirstOrDefault();

    private static SourceRow? OfLocation(SourceTable table, long locationId) =>
        table.Rows.Where(r => r.Long("location_id") == locationId).OrderByDescending(r => r.Id).FirstOrDefault();

    /// <summary>
    /// The store's settings row from Retail25's business profile, tax configuration in effect today, POS policy, loyalty
    /// policy, late charge policy and base currency. Retail25 settings with no field of their own are kept as named extras.
    /// </summary>
    private async Task SettingsAsync(CancellationToken ct)
    {
        var area = _report.Area("Store settings", "business_profiles, tax_configurations, pos_policies, loyalty_policies, late_charge_policies, currencies");
        var extrasArea = _report.Area("Store settings kept by name", "pos_policies, business_profiles, locations, tax_configurations");
        Dictionary<Guid, Guid> existing;
        await using (var db = _w.NewContext())
            existing = await db.StoreSettings.ToDictionaryAsync(s => s.StoreId, s => s.Id, ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = new List<StoreSettings>();
        var extras = new List<StoreSettingExtra>();
        foreach (var (locationId, store) in _stores)
        {
            area.Read++;
            var profile = OfLocation(_src["business_profiles"], locationId);
            var tax = TaxOn(locationId, today);
            var pos = OfLocation(_src["pos_policies"], locationId);
            var loyalty = OfLocation(_src["loyalty_policies"], locationId);
            var late = OfLocation(_src["late_charge_policies"], locationId);
            var currency = _src["currencies"].Rows.FirstOrDefault(c => string.Equals(c.Text("code"), store.Row.Text("base_currency_code"), StringComparison.OrdinalIgnoreCase))
                ?? _src["currencies"].Rows.FirstOrDefault(c => c.Bool("is_base_currency"));
            var settings = new StoreSettings
            {
                Id = existing.GetValueOrDefault(store.StoreId, StableIds.For("settings", locationId)),
                StoreId = store.StoreId,
                CompanyName = profile?.Text("business_name") ?? store.Row.Text("name"),
                Address1 = profile?.Text("address_line1") ?? store.Row.Text("address_line1"),
                Address2 = profile?.Text("address_line2") ?? store.Row.Text("address_line2"),
                City = profile?.Text("address_city") ?? store.Row.Text("address_city"),
                State = profile?.Text("address_state_or_province") ?? store.Row.Text("address_state_or_province"),
                Zip = profile?.Text("address_postal_code") ?? store.Row.Text("address_postal_code"),
                Country = profile?.Text("address_country") ?? store.Row.Text("address_country"),
                Phone = profile?.Text("contact_phone") ?? store.Row.Text("contact_phone"),
                Fax = profile?.Text("contact_fax") ?? store.Row.Text("contact_fax"),
                TaxRegistrationNumber = tax?.Text("registration_number") ?? profile?.Text("tax_registration_number"),
                Tax1Name = tax?.Text("tax1name") ?? "Tax 1",
                Tax1Rate = tax is not null && tax.Bool("tax1enabled") ? Three(tax.Dec("tax1rate"), "StoreSettings.Tax1Rate") : 0,
                Tax2Name = tax?.Text("tax2name") ?? "Tax 2",
                Tax2Rate = tax is not null && tax.Bool("tax2enabled") ? Three(tax.Dec("tax2rate"), "StoreSettings.Tax2Rate") : 0,
                Tax2Compound = tax?.Bool("tax2compound") ?? false,
                TaxInclusive = string.Equals(tax?.Text("taxation_type"), "Inclusive", StringComparison.OrdinalIgnoreCase),
                StaffMayDiscount = pos?.Bool("staff_may_discount") ?? false,
                SupervisorForVoids = pos?.Bool("require_supervisor_to_void") ?? false,
                TrackStaffSales = pos?.Bool("track_staff_sales") ?? false,
                CarryOverCityStateZip = pos?.Bool("carry_over_city_state_zip") ?? false,
                ScanRandomWeightBarcodes = pos?.Bool("scan_random_weight_barcodes") ?? false,
                BonusPointsEnabled = loyalty?.Bool("is_enabled") ?? false,
                PointsPerDollar = Three(loyalty?.Dec("points_per_dollar") ?? 0, "StoreSettings.PointsPerDollar"),
                PointsRequired = loyalty?.Dec("minimum_required") ?? 0,
                PercentDiscountReward = loyalty?.Bool("percent_enabled") ?? false,
                RewardPercent = Three(loyalty?.Dec("reward_percent") ?? 0, "StoreSettings.RewardPercent"),
                FixedDiscountReward = loyalty?.Bool("fixed_enabled") ?? false,
                RewardAmount = Money(loyalty?.Dec("reward_fixed_amount") ?? 0, "StoreSettings.RewardAmount"),
                InterestRatePerMonth = late is not null && late.Bool("is_enabled") ? Three(late.Dec("monthly_rate"), "StoreSettings.InterestRatePerMonth") : 0,
                GracePeriodDays = late?.Int("grace_period_days") ?? 30,
                MinimumTender = Money(currency?.Dec("minimum_tender") is > 0 and var m ? m : 0.01m, "StoreSettings.MinimumTender"),
                CreatedAtUtc = Utc(profile?.Instant("created_at") ?? store.Row.Instant("created_at")),
            };
            rows.Add(From(settings, profile ?? store.Row));
            if (tax is null) Warn($"Store {store.Code}: Retail25 has no tax configuration in effect today; taxes are left at 0");
            if (tax is not null && tax.Bool("add_on_charge_enabled"))
                Warn($"Store {store.Code}: Retail25 adds a charge \"{tax.Text("add_on_charge_name")}\" of {tax.Dec("add_on_charge_rate"):0.###}% to sales; the new model has no such charge (it is kept as a setting by name)");

            void Extra(string name, string? value, SourceRow? from)
            {
                extrasArea.Read++;
                if (value is null) { extrasArea.Skip("empty"); return; }
                extras.Add(From(new StoreSettingExtra { Id = StableIds.For("setting-extra", Number(locationId) + "/" + name), StoreId = store.StoreId, Name = name, Value = value }, from ?? store.Row));
            }
            static string? Flag(SourceRow? row, string column) => row is null || !row.Has(column) ? null : row.Bool(column) ? "Y" : "N";
            Extra("R25_EMAIL", profile?.Text("contact_email") ?? store.Row.Text("contact_email"), profile);
            Extra("R25_WEBSITE", profile?.Text("contact_website") ?? store.Row.Text("contact_website"), profile);
            Extra("R25_MOBILE", profile?.Text("contact_mobile") ?? store.Row.Text("contact_mobile"), profile);
            Extra("R25_LICENCE", profile?.Text("licence_number"), profile);
            Extra("R25_CURRENCY", store.Row.Text("base_currency_code"), store.Row);
            Extra("R25_DAY_START", store.DayStart == TimeSpan.Zero ? null : store.DayStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture), store.Row);
            Extra("R25_TIME_CLOCK", Flag(pos, "use_employee_time_clock"), pos);
            Extra("R25_SIGNATURE_LINE", Flag(pos, "print_credit_card_signature_line"), pos);
            Extra("R25_CLIENT_ON_SLIP", Flag(pos, "print_client_name_on_sales_slip"), pos);
            Extra("R25_TAX_OVERRIDE", Flag(pos, "allow_tax_override"), pos);
            Extra("R25_FAST_SCAN", Flag(pos, "fast_scan_mode"), pos);
            Extra("R25_CONFIRM_SAVE", Flag(pos, "confirm_before_saving_sales"), pos);
            Extra("R25_ADD_ON_CHARGE", tax is not null && tax.Bool("add_on_charge_enabled")
                ? string.Create(CultureInfo.InvariantCulture, $"{tax.Text("add_on_charge_name")} {tax.Dec("add_on_charge_rate"):0.###}%{(tax.Bool("add_on_charge_taxable") ? " taxable" : "")}") : null, tax);
            Extra("R25_LOYALTY_NO_DISC", Flag(loyalty, "suppress_if_subtotal_discount_applied"), loyalty);
        }
        await _w.UpsertAsync(area, rows, ct);
        await _w.UpsertAsync(extrasArea, extras, ct);

        // Tax configurations that are not in effect today: sales keep the rates they were made with; future ones must be re-entered.
        int other = _src["tax_configurations"].Rows.Count(t => _stores.ContainsKey(t.Long("location_id")) && TaxOn(t.Long("location_id"), today)?.Id != t.Id);
        if (other > 0)
            _report.NotCarriedOver.Add(new NotCarried("Tax configurations not in effect today (tax_configurations)", other,
                "The new model keeps one current tax setup per store; every sale keeps the rates it was made with. Enter a future change when it is due."));
    }

    /// <summary>The store's logo and watermark (PNG or JPEG, which the tills can show).</summary>
    private async Task BrandingAsync(CancellationToken ct)
    {
        var area = _report.Area("Branding", "branding_assets");
        Dictionary<(Guid, StoreImageKind), Guid> existing;
        await using (var db = _w.NewContext())
            existing = (await db.StoreImages.Select(i => new { i.Id, i.StoreId, i.Kind }).ToListAsync(ct)).ToDictionary(i => (i.StoreId, i.Kind), i => i.Id);
        var rows = new List<StoreImage>();
        foreach (var row in _src["branding_assets"].Rows.OrderBy(r => r.Id))
        {
            area.Read++;
            if (StoreOf(row.Long("location_id")) is not { } store) { area.Skip("store not carried over"); continue; }
            var kind = string.Equals(row.Text("slot"), "Watermark", StringComparison.OrdinalIgnoreCase) || row.Text("slot") == "0" ? StoreImageKind.Watermark : StoreImageKind.Logo;
            var type = row.Text("content_type");
            if (type is not ("image/png" or "image/jpeg")) { area.Skip($"{type ?? "unknown"} picture (the tills show PNG and JPEG)"); continue; }
            var data = row.Bytes("content") ?? [];
            if (data.Length == 0 || data.Length > RetailPlus.Data.Services.BrandingService.MaxBytes) { area.Skip("empty or larger than 2 MB"); continue; }
            if (rows.Any(r => r.StoreId == store.StoreId && r.Kind == kind)) { area.Skip("a second picture for the same place"); continue; }
            rows.Add(From(new StoreImage
            {
                Id = existing.GetValueOrDefault((store.StoreId, kind), StableIds.For("branding", row.Id)),
                StoreId = store.StoreId, Kind = kind, Data = data, CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row));
            if (kind == StoreImageKind.Watermark && row.Int("opacity_pct") is > 0 and var opacity)
                Note($"Store {store.Code}: Retail25 showed the watermark at {opacity}% opacity; the tills print it at their own fixed strength.");
        }
        await _w.UpsertAsync(area, rows, ct);
    }
}
