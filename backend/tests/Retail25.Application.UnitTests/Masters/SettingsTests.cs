using FluentAssertions;
using Xunit;
using NSubstitute;
using Retail25.Application.Settings;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.UnitTests.Masters;

/// <summary>
/// The Setup screen (guide p.76–84). The exit criterion for this phase is that a store's catalogue,
/// taxes and stations can be configured end to end through the UI — these are the rules that stop a
/// configuration change from quietly breaking something already recorded.
/// </summary>
public sealed class SettingsTests
{
    [Fact]
    public async Task The_settings_screen_loads_every_tab_in_one_call()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddStationAsync("001");
        await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        var settings = await harness.Settings.Handle(new GetSettingsQuery(harness.Location.Id), default);

        settings.IsSuccess.Should().BeTrue();
        settings.Value.Business.LegacyCode.Should().Be("TST");
        settings.Value.Taxes.Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
        settings.Value.Stations.Should().ContainSingle();
        settings.Value.Tenders.Should().ContainSingle();
        settings.Value.PricingRules.Should().HaveCount(PricingRuleKeys.DefaultOrder.Count);
        settings.Value.Numbering.Should().HaveCount(Enum.GetValues<SequenceKind>().Length);
    }

    [Fact]
    public async Task A_tax_change_writes_a_new_row_and_closes_the_old_one()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var effectiveFrom = harness.Today.AddDays(7);

        var saved = await harness.SettingsCommands.Handle(
            new SaveTaxSettingsCommand(
                harness.Location.Id, effectiveFrom,
                true, "GST", 5m,
                true, "PST", 8m,
                false,
                false, string.Empty, 0m, false,
                TaxationType.Exclusive, null),
            default);

        saved.IsSuccess.Should().BeTrue();

        var rows = harness.Db.TaxConfigurations.OrderBy(t => t.EffectiveFrom).ToList();

        rows.Should().HaveCount(2);

        // The old row keeps serving historical documents — that is what makes a reprint of last
        // month's invoice show last month's tax (guide p.56).
        rows[0].EffectiveTo.Should().Be(effectiveFrom.AddDays(-1));
        rows[0].Tax2Rate.Value.Should().Be(7m);
        rows[1].Tax2Rate.Value.Should().Be(8m);
    }

    [Fact]
    public async Task A_tax_change_cannot_be_backdated()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.SettingsCommands.Handle(
            new SaveTaxSettingsCommand(
                harness.Location.Id, harness.Today.AddDays(-1),
                true, "GST", 5m,
                false, string.Empty, 0m,
                false,
                false, string.Empty, 0m, false,
                TaxationType.Exclusive, null),
            default);

        // Sales already rung would change retroactively, and their stored snapshots would disagree
        // with the configuration they claim to come from.
        saved.Error.Code.Should().Be("tax.effective_date_in_past");
    }

    [Fact]
    public async Task Correcting_a_tax_change_on_the_same_day_replaces_it_rather_than_stacking()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var effectiveFrom = harness.Today.AddDays(3);

        SaveTaxSettingsCommand Change(decimal tax2) => new(
            harness.Location.Id, effectiveFrom,
            true, "GST", 5m,
            true, "PST", tax2,
            false,
            false, string.Empty, 0m, false,
            TaxationType.Exclusive, null);

        await harness.SettingsCommands.Handle(Change(88m), default);
        await harness.SettingsCommands.Handle(Change(8m), default);

        var scheduled = harness.Db.TaxConfigurations.Where(t => t.EffectiveFrom == effectiveFrom).ToList();

        scheduled.Should().ContainSingle().Which.Tax2Rate.Value.Should().Be(8m);
    }

    [Fact]
    public async Task Repointing_a_counter_restarts_the_live_sequence()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.SettingsCommands.Handle(
            new SaveNumberSequenceCommand(harness.Location.Id, SequenceKind.Customer, "C-", 5, 4183),
            default);

        saved.IsSuccess.Should().BeTrue();
        saved.Value.Sample.Should().Be("C-04183");

        // Saving the row alone would change nothing that issues numbers: the sequence was created
        // from that row the first time it was used and never reads it again.
        (await harness.Sequences.NextAsync(SequenceKind.Customer, harness.Location.Id)).Should().Be(4183);
    }

    [Fact]
    public async Task A_counter_cannot_be_moved_back_onto_numbers_already_issued()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var sequence = harness.Db.NumberSequences.Single(s => s.Kind == SequenceKind.Invoice);
        sequence.Take();
        sequence.Take();
        await harness.Db.SaveChangesAsync();

        var saved = await harness.SettingsCommands.Handle(
            new SaveNumberSequenceCommand(harness.Location.Id, SequenceKind.Invoice, "INV-", 6, 1),
            default);

        // Duplicate invoice numbers are the kind of mistake that surfaces months later at an audit.
        saved.Error.Code.Should().Be("sequence.would_go_backwards");
    }

    [Fact]
    public async Task A_pricing_ladder_missing_a_rung_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var partial = harness.Db.PricingRuleSettings
            .Where(r => r.LocationId == harness.Location.Id)
            .OrderBy(r => r.Order)
            .Take(3)
            .Select(r => new PricingRuleDto(r.Id, r.RuleKey, r.Order, r.Enabled, r.ParametersJson))
            .ToList();

        var saved = await harness.SettingsCommands.Handle(
            new SavePricingLadderCommand(harness.Location.Id, partial), default);

        // A missing rung is not a partial save, it is a pricing engine with a hole in it.
        saved.Error.Code.Should().Be("pricing_rule.ladder_incomplete");
    }

    [Fact]
    public async Task The_pricing_ladder_can_be_reordered_without_a_code_change()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var rules = harness.Db.PricingRuleSettings
            .Where(r => r.LocationId == harness.Location.Id)
            .OrderBy(r => r.Order)
            .Select(r => new PricingRuleDto(r.Id, r.RuleKey, r.Order, r.Enabled, r.ParametersJson))
            .ToList();

        // Decision P1's alternative: promote the sale window above break points.
        var reordered = rules
            .Select(r => r.RuleKey == PricingRuleKeys.SaleWindow ? r with { Order = 5 } : r)
            .ToList();

        var saved = await harness.SettingsCommands.Handle(
            new SavePricingLadderCommand(harness.Location.Id, reordered), default);

        saved.IsSuccess.Should().BeTrue();
        saved.Value[0].RuleKey.Should().Be(PricingRuleKeys.SaleWindow);
    }

    [Fact]
    public async Task The_last_active_cash_tender_cannot_be_switched_off()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        var dto = SettingsQueryHandler.ToDto(cash) with { IsActive = false };

        var saved = await harness.Commerce.Handle(new SaveTenderTypeCommand(harness.Location.Id, dto), default);

        // A till that cannot take cash has a drawer that cannot be counted.
        saved.Error.Code.Should().Be("tender_type.last_cash");
    }

    [Fact]
    public async Task A_tender_used_on_a_sale_cannot_be_removed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var card = await harness.AddTenderAsync("CARD", "Card", TenderBehaviour.Card);

        harness.Db.SaleTenders.Add(new SaleTender
        {
            TransactionId = TestIds.Next(),
            TenderTypeId = card.Id,
            Behaviour = TenderBehaviour.Card,
            Amount = 10m,
            AmountTendered = 10m,
        });
        await harness.Db.SaveChangesAsync();

        var deleted = await harness.Commerce.Handle(
            new DeleteTenderTypeCommand(harness.Location.Id, card.Id), default);

        deleted.Error.Code.Should().Be("tender_type.in_use");
    }

    [Fact]
    public async Task The_base_currency_cannot_be_switched_after_the_fact()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var currency = harness.Db.Currencies.Single();

        var dto = SettingsQueryHandler.ToDto(currency) with { IsBaseCurrency = false };

        var saved = await harness.Commerce.Handle(new SaveCurrencyCommand(harness.Location.Id, dto), default);

        // Every ledger is denominated in the base currency; switching it reinterprets every amount.
        saved.Error.Code.Should().Be("currency.base_fixed");
    }

    [Fact]
    public async Task Saving_a_station_pushes_the_new_profile_to_its_agent()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var station = await harness.AddStationAsync("001");

        var saved = await harness.Hardware.Handle(
            new SaveStationCommand(
                harness.Location.Id, station.Id, "001", "Front counter",
                FastScanMode: true, AutoSaveSales: null, ConfirmBeforeSaving: null, ScanRandomWeightBarcodes: null,
                DefaultTenderTypeId: null,
                PrinterProfileId: null, ReaderProfileId: null, ScaleProfileId: null, PoleDisplayProfileId: null,
                ReaderMode.Continuous, IsActive: true),
            default);

        saved.IsSuccess.Should().BeTrue();
        saved.Value.FastScanMode.Should().BeTrue();

        // Waiting for the agent's next poll would leave a cashier pressing a key that does nothing.
        await harness.Terminals.Received().UpdateProfileAsync(station.Id, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_duplicate_station_code_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddStationAsync("001");

        var saved = await harness.Hardware.Handle(
            new SaveStationCommand(
                harness.Location.Id, null, "1", null,
                null, null, null, null, null, null, null, null, null,
                ReaderMode.OnDemand, true),
            default);

        saved.Error.Code.Should().Be("station.duplicate_code");
    }

    [Fact]
    public async Task A_station_with_an_open_drawer_cannot_be_retired()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var station = await harness.AddStationAsync("001");

        harness.Db.DrawerSessions.Add(
            DrawerSession.Open(station.Id, harness.Location.Id, TestIds.Next(), 200m, harness.Today, harness.Clock.Now).Value);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Hardware.Handle(new DeactivateStationCommand(station.Id), default);

        // An open drawer belongs to a shift that has to be counted.
        result.Error.Code.Should().Be("station.drawer_open");
    }

    [Fact]
    public async Task Saving_the_printer_profile_keeps_the_drawer_kick_as_data()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.Hardware.Handle(
            new SavePrinterProfileCommand(
                harness.Location.Id,
                new PrinterSettingsDto(
                    0L, null, "Star at counter",
                    SetupCommand: null, CutterCommand: "27,100,48", RedCommand: null, BlackCommand: null,
                    Port: "COM1", DefaultCopies: 2, PageEject: false, ExtraCopyOnCard: true, InitializeSerial: true,
                    Output: PrinterOutput.Slip40, Columns: 40,
                    DrawerTrigger: "7", DrawerRepeat: 1, OpenDrawerOnPrint: true, IsActive: true)),
            default);

        saved.IsSuccess.Should().BeTrue();

        // Star cuts with 27,100,48 and kicks the drawer with a bare BEL. Neither is an Epson default,
        // and neither belongs in a driver.
        saved.Value.CutterCommand.Should().Be("27,100,48");
        saved.Value.DrawerTrigger.Should().Be("7");
    }

    [Fact]
    public async Task An_unknown_time_zone_is_refused_rather_than_stored()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.SettingsCommands.Handle(
            new SaveBusinessSettingsCommand(
                harness.Location.Id, "Test Store",
                new Address(Line1: "1 High Street"), new ContactDetails(Phone: "555-0100"),
                null, null, "Test Store", "Mars/Olympus", TimeOnly.MinValue),
            default);

        // A stored zone the server cannot resolve would throw the first time a business date is
        // derived — which is inside a sale.
        saved.Error.Code.Should().Be("location.time_zone_unknown");
    }

    [Fact]
    public async Task Saving_the_business_tab_updates_what_prints_on_a_receipt()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.SettingsCommands.Handle(
            new SaveBusinessSettingsCommand(
                harness.Location.Id, "Corner Hardware",
                new Address(Line1: "1 High Street", City: "Kingston"), new ContactDetails(Phone: "555-0100"),
                "LIC-9", "GST-123", "Kingston branch", "UTC", new TimeOnly(4, 0)),
            default);

        saved.IsSuccess.Should().BeTrue();
        saved.Value.BusinessName.Should().Be("Corner Hardware");
        saved.Value.Address.City.Should().Be("Kingston");

        var location = harness.Db.Locations.Single();
        location.Name.Should().Be("Kingston branch");
        location.BusinessDayStart.Should().Be(new TimeOnly(4, 0));
    }

    [Fact]
    public async Task Staff_are_listed_without_their_pin_hash()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var saved = await harness.Commerce.Handle(
            new SaveStaffCommand(harness.Location.Id, null, TestIds.Next(), "sk", "Sam", "Kelly", 3, true),
            default);

        saved.IsSuccess.Should().BeTrue();
        saved.Value.StaffCode.Should().Be("SK");
        saved.Value.HasPin.Should().BeFalse();

        var settings = await harness.Settings.Handle(new GetSettingsQuery(harness.Location.Id), default);
        settings.Value.Staff.Should().ContainSingle().Which.AccessLevel.Should().Be(3);
    }

    [Fact]
    public async Task Two_staff_cannot_share_a_code()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        await harness.Commerce.Handle(
            new SaveStaffCommand(harness.Location.Id, null, TestIds.Next(), "SK", "Sam", "Kelly", 3, true), default);

        var duplicate = await harness.Commerce.Handle(
            new SaveStaffCommand(harness.Location.Id, null, TestIds.Next(), "sk", "Sue", "King", 2, true), default);

        // The staff code is what a cashier types at a PIN prompt; two people sharing one makes every
        // attribution on a receipt ambiguous.
        duplicate.Error.Code.Should().Be("staff.duplicate_code");
    }
}
