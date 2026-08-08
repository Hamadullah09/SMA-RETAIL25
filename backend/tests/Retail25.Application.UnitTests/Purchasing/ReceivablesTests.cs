using FluentAssertions;
using Retail25.Application.Receivables;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Configuration;
using Retail25.Domain.Receivables;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>
/// Accounts receivable depth (guide p.56–58): distribute-payment across open invoices oldest-first,
/// penalty-first allocation within an invoice, void, refund, and late-charge accrual with a grace
/// period. The harness's clock is fixed at 2026-07-29 (<see cref="MastersTestHarness"/>).
/// </summary>
public sealed class ReceivablesTests
{
    [Fact]
    public async Task A_payment_that_fully_covers_the_only_open_invoice_marks_it_paid()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(customer.Id, 100m, harness.Today.AddDays(-10), harness.Today.AddDays(20));
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        var result = await harness.Receivables.Handle(
            new TakeInvoicePaymentCommand(customer.Id, 100m, cash.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AmountApplied.Should().Be(100m);
        result.Value.AmountUnapplied.Should().Be(0m);

        var refreshedInvoice = await harness.Db.Invoices.FindAsync(invoice.Id);
        refreshedInvoice!.Status.Should().Be(InvoiceStatus.Paid);
        refreshedInvoice.BalanceDue.Should().Be(0m);

        (await harness.Db.CustomerAccounts.FindAsync(account.Id))!.BalanceDue.Should().Be(0m);
    }

    [Fact]
    public async Task A_payment_distributes_across_open_invoices_oldest_due_date_first()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var older = await harness.AddInvoiceAsync(customer.Id, 60m, harness.Today.AddDays(-40), harness.Today.AddDays(-10));
        var newer = await harness.AddInvoiceAsync(customer.Id, 60m, harness.Today.AddDays(-5), harness.Today.AddDays(25));
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        // Enough to fully pay the older invoice and put $30 towards the newer one.
        var result = await harness.Receivables.Handle(
            new TakeInvoicePaymentCommand(customer.Id, 90m, cash.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.AmountApplied.Should().Be(90m);

        (await harness.Db.Invoices.FindAsync(older.Id))!.BalanceDue.Should().Be(0m);
        (await harness.Db.Invoices.FindAsync(older.Id))!.Status.Should().Be(InvoiceStatus.Paid);
        (await harness.Db.Invoices.FindAsync(newer.Id))!.BalanceDue.Should().Be(30m);
        (await harness.Db.Invoices.FindAsync(newer.Id))!.Status.Should().Be(InvoiceStatus.Open);

        harness.Db.InvoicePayments.Should().OnlyContain(p => p.WasDistributed);
    }

    [Fact]
    public async Task A_single_invoice_payment_is_not_marked_as_distributed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        await harness.AddInvoiceAsync(customer.Id, 100m, harness.Today.AddDays(-10), harness.Today.AddDays(20));
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        await harness.Receivables.Handle(new TakeInvoicePaymentCommand(customer.Id, 50m, cash.Id), default);

        harness.Db.InvoicePayments.Should().ContainSingle(p => !p.WasDistributed);
    }

    [Fact]
    public async Task Payment_applies_to_penalty_before_principal_on_an_invoice()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(
            customer.Id, invoiceTotal: 100m, harness.Today.AddDays(-60), harness.Today.AddDays(-30), penaltyAccrued: 10m);
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);

        await harness.Receivables.Handle(new TakeInvoicePaymentCommand(customer.Id, 15m, cash.Id), default);

        var payment = harness.Db.InvoicePayments.Single();
        payment.AppliedToPenalty.Should().Be(10m);
        payment.AppliedToPrincipal.Should().Be(5m);

        var refreshed = await harness.Db.Invoices.FindAsync(invoice.Id);
        refreshed!.PenaltyAccrued.Should().Be(0m);
        refreshed.BalanceDue.Should().Be(95m);
    }

    [Fact]
    public async Task Voiding_an_invoice_zeroes_its_balance_and_releases_the_account_balance()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(customer.Id, 80m, harness.Today, harness.Today.AddDays(30));
        account.BalanceDue = 80m;
        await harness.Db.SaveChangesAsync();

        var result = await harness.Receivables.Handle(new VoidInvoiceCommand(invoice.Id, "Billed in error"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(InvoiceStatus.Void);
        result.Value.BalanceDue.Should().Be(0m);
        (await harness.Db.CustomerAccounts.FindAsync(account.Id))!.BalanceDue.Should().Be(0m);
    }

    [Fact]
    public async Task A_refund_cannot_exceed_what_was_actually_paid()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(customer.Id, 100m, harness.Today, harness.Today.AddDays(30));
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);
        await harness.Receivables.Handle(new TakeInvoicePaymentCommand(customer.Id, 40m, cash.Id), default);

        var result = await harness.Receivables.Handle(new RefundInvoiceCommand(invoice.Id, 50m, "Overpayment"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("receivables.refund_exceeds_paid");
    }

    [Fact]
    public async Task A_refund_reopens_a_paid_invoice_and_restores_the_balance()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(customer.Id, 50m, harness.Today, harness.Today.AddDays(30));
        var cash = await harness.AddTenderAsync("CASH", "Cash", TenderBehaviour.Cash);
        await harness.Receivables.Handle(new TakeInvoicePaymentCommand(customer.Id, 50m, cash.Id), default);

        var result = await harness.Receivables.Handle(new RefundInvoiceCommand(invoice.Id, 50m, "Cheque bounced"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(InvoiceStatus.Open);
        result.Value.BalanceDue.Should().Be(50m);
        (await harness.Db.CustomerAccounts.FindAsync(account.Id))!.BalanceDue.Should().Be(50m);
    }

    [Fact]
    public async Task Late_charges_accrue_only_past_the_grace_period_and_only_on_the_principal()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");

        // 45 days overdue, well past a 10-day grace period.
        var overdue = await harness.AddInvoiceAsync(customer.Id, 1000m, harness.Today.AddDays(-75), harness.Today.AddDays(-45));

        // Due yesterday — inside the grace period, nothing should accrue yet.
        var withinGrace = await harness.AddInvoiceAsync(customer.Id, 500m, harness.Today.AddDays(-29), harness.Today.AddDays(-1));

        harness.Db.LateChargePolicies.Add(new LateChargePolicy
        {
            LocationId = harness.Location.Id,
            MonthlyRate = 1.5m,
            GracePeriodDays = 10,
            IsEnabled = true,
            CreatedAt = harness.Clock.Now,
        });
        await harness.Db.SaveChangesAsync();

        var result = await harness.Receivables.Handle(new AccrueLateChargesCommand(harness.Location.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var overdueInvoice = await harness.Db.Invoices.FindAsync(overdue.Id);
        overdueInvoice!.PenaltyAccrued.Should().Be(15m); // 1000 * 1.5%
        overdueInvoice.BalanceDue.Should().Be(1000m); // principal untouched by the charge itself

        (await harness.Db.Invoices.FindAsync(withinGrace.Id))!.PenaltyAccrued.Should().Be(0m);

        // 1000 + 500 principal from the two invoices, plus the 15 late charge just accrued.
        (await harness.Db.CustomerAccounts.FindAsync(account.Id))!.BalanceDue.Should().Be(1515m);
    }

    [Fact]
    public async Task Running_the_late_charge_job_twice_in_one_day_does_not_double_charge()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        var invoice = await harness.AddInvoiceAsync(customer.Id, 1000m, harness.Today.AddDays(-75), harness.Today.AddDays(-45));

        harness.Db.LateChargePolicies.Add(new LateChargePolicy
        {
            LocationId = harness.Location.Id,
            MonthlyRate = 1.5m,
            GracePeriodDays = 10,
            IsEnabled = true,
            CreatedAt = harness.Clock.Now,
        });
        await harness.Db.SaveChangesAsync();

        await harness.Receivables.Handle(new AccrueLateChargesCommand(harness.Location.Id), default);
        var second = await harness.Receivables.Handle(new AccrueLateChargesCommand(harness.Location.Id), default);

        second.Value.Should().Be(0);
        (await harness.Db.Invoices.FindAsync(invoice.Id))!.PenaltyAccrued.Should().Be(15m);
    }

    [Fact]
    public async Task Browsing_accounts_returns_only_this_locations_customers_with_their_balance()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        await harness.AddInvoiceAsync(customer.Id, 40m, harness.Today, harness.Today.AddDays(30));
        account.BalanceDue = 40m;
        await harness.Db.SaveChangesAsync();

        var page = await harness.Receivables.Handle(new BrowseCustomerAccountsQuery(harness.Location.Id), default);

        page.Items.Should().ContainSingle(r => r.CustomerId == customer.Id && r.BalanceDue == 40m && r.OpenInvoiceCount == 1);
    }

    [Fact]
    public async Task The_aging_report_buckets_an_overdue_invoice_by_days_past_due()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, account) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        await harness.AddInvoiceAsync(customer.Id, 200m, harness.Today.AddDays(-70), harness.Today.AddDays(-40));
        account.BalanceDue = 200m;
        await harness.Db.SaveChangesAsync();

        var aging = await harness.Receivables.Handle(new GetReceivablesAgingQuery(harness.Location.Id), default);

        var row = aging.Should().ContainSingle(r => r.CustomerId == customer.Id).Subject;
        row.Days60.Should().Be(200m); // 40 days past due falls in the 30<d<=60 bucket
        row.Total.Should().Be(200m);
    }

    [Fact]
    public async Task A_customer_statement_lists_invoices_and_ledger_history()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        await harness.AddInvoiceAsync(customer.Id, 75m, harness.Today, harness.Today.AddDays(30));

        var result = await harness.Receivables.Handle(new GetCustomerStatementQuery(customer.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().ContainSingle(i => i.InvoiceTotal == 75m);
        result.Value.Ledger.Should().ContainSingle(e => e.EntryType == AREntryType.Charge && e.Amount == 75m);
    }
}
