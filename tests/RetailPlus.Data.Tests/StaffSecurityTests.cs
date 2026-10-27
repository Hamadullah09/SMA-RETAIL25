using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>Users and roles, approvals, user ids on what users do, the audit log, the time clock and commission.</summary>
public abstract class StaffSecurityTests(DatabaseFixture db)
{
    private static int _station = 300;

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<Guid> RoleIdAsync(string name) => (await new SetupService(db.Data).GetRolesAsync()).Single(r => r.Name == name).Id;

    /// <summary>A new user with one of the standard roles, a password and optionally a staff code and PIN.</summary>
    private async Task<UserAccount> UserAsync(string role, string? pin = null)
    {
        var setup = new SetupService(db.Data);
        var user = await setup.SaveUserAsync(new UserAccount { UserName = Unique("u"), RoleId = await RoleIdAsync(role) }, "pass");
        if (pin is not null) await setup.SetPinAsync(user.Id, Unique("c"), pin);
        return (await setup.AuthenticateAsync(user.UserName, "pass"))!;
    }

    /// <summary>Services working for a signed-in user at a station of its own, at a new location.</summary>
    private async Task<(DataContext Data, SessionInfo Session, Location Location)> SignedInAsync(UserAccount user)
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var data = db.Data;
        var location = await new SetupService(data).AddLocationAsync(db.NextLocationCode(), "Staff test");
        var session = new SessionInfo { StationNumber = Interlocked.Increment(ref _station), LocationId = location.Id, LocationCode = location.Code };
        session.SignIn(user);
        data.CurrentUser = session;
        return (data, session, location);
    }

    private static async Task<Item> ItemAsync(DataContext data, Guid locationId, string plu, decimal price, string? department = "STAFFTEST", decimal commissionPercent = 0) =>
        await new InventoryService(data).SaveAsync(new Item
        {
            LocationId = locationId, Plu = plu, Name = "Item " + plu, Price1 = price, LastCost = 4m, AverageCost = 4m, OnHand = 50, Commission = commissionPercent,
        }, department, null, null);

    private static SaleDraftLine Line(Item item, decimal qty) => new()
    {
        ItemId = item.Id, ItemType = item.ItemType, Plu = item.Plu, Description = item.Name, Quantity = qty, UnitPrice = item.Price1,
        UnitCost = item.AverageCost, CommissionPercent = item.Commission, Department = "STAFFTEST",
    };

    private static TenderEntry Cash(decimal amount) => new("Cash", TenderKind.Cash, amount, amount);

    private static Dictionary<string, JsonElement> Values(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    // Roles and users -----------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Roles_in_use_cannot_be_deleted_and_standard_roles_keep_their_names()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var setup = new SetupService(db.Data);
        var role = await setup.SaveRoleAsync(new Role { Name = Unique("Floor "), Level = 1 }, [Permissions.PosSell]);
        var user = await setup.SaveUserAsync(new UserAccount { UserName = Unique("r"), RoleId = role.Id }, "pass");
        Assert.Equal(1, (await setup.GetRoleUsageAsync())[role.Id]);

        var used = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.DeleteRoleAsync(role.Id));
        Assert.Contains("1 user has", used.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveRoleAsync(new Role { Name = role.Name, Level = 1 }, []));

        var cashier = (await setup.GetRolesAsync()).Single(r => r.Name == Permissions.CashierRole);
        cashier.Name = "Till staff";
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveRoleAsync(cashier, cashier.Permissions.Select(p => p.Permission)));

        await setup.DeleteUserAsync(user.Id);
        await setup.DeleteRoleAsync(role.Id);
        Assert.DoesNotContain(await setup.GetRolesAsync(), r => r.Id == role.Id);
    }

    [SkippableFact]
    public async Task The_last_user_who_manages_users_cannot_be_removed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var setup = new SetupService(db.Data);
        var admin = await UserAsync(Permissions.AdministratorRole);
        // Leave this administrator as the only active user who may manage users.
        await using (var ctx = db.CreateContext())
        {
            var now = DateTime.UtcNow;
            var others = await ctx.UserAccounts.Where(u => u.Id != admin.Id && u.IsActive && u.Role!.Permissions.Any(p => p.Permission == Permissions.SetupUsers))
                .Select(u => u.Id).ToListAsync();
            await ctx.UserAccounts.Where(u => others.Contains(u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false).SetProperty(u => u.UpdatedAtUtc, now));
        }
        admin.IsActive = false;
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveUserAsync(admin, null));
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.DeleteUserAsync(admin.Id));
        var adminRole = (await setup.GetRolesAsync()).Single(r => r.Name == Permissions.AdministratorRole);
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.SaveRoleAsync(adminRole, adminRole.Permissions.Select(p => p.Permission).Where(p => p != Permissions.SetupUsers)));
        Assert.True((await setup.GetUsersAsync()).Single(u => u.Id == admin.Id).IsActive);
        Assert.Contains((await setup.GetRolesAsync()).Single(r => r.Name == Permissions.AdministratorRole).Permissions, p => p.Permission == Permissions.SetupUsers);

        await UserAsync(Permissions.AdministratorRole);
        await setup.SaveUserAsync(admin, null);
        Assert.False((await setup.GetUsersAsync()).Single(u => u.Id == admin.Id).IsActive);
    }

    [SkippableFact]
    public async Task A_locked_account_says_how_long_and_a_reset_password_must_be_changed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var setup = new SetupService(db.Data);
        var user = await UserAsync(Permissions.CashierRole, pin: "2468");
        for (int i = 1; i < SetupService.SignInAttempts; i++)
            Assert.Equal(SignInFailure.WrongCredentials, (await setup.SignInWithPinAsync(user.StaffCode!, "0000")).Failure);
        var locked = await setup.SignInWithPinAsync(user.StaffCode!, "0000");
        Assert.Equal(SignInFailure.Locked, locked.Failure);
        Assert.Equal(5, locked.MinutesLeft(DateTime.UtcNow));
        Assert.Contains("5 minutes", locked.Message, StringComparison.Ordinal);
        Assert.Equal(SignInFailure.Locked, (await setup.SignInWithPinAsync(user.StaffCode!, "2468")).Failure);

        await setup.ResetPasswordAsync(user.Id, "temp");
        var after = await setup.SignInAsync(user.UserName, "temp");
        Assert.NotNull(after.User);
        Assert.True(after.User!.MustChangePassword);
        var gone = (await setup.GetUsersAsync()).Single(u => u.Id == user.Id);
        gone.IsActive = false;
        await setup.SaveUserAsync(gone, null);
        Assert.Equal(SignInFailure.NotActive, (await setup.SignInAsync(user.UserName, "temp")).Failure);

        // A user whose home store is another store cannot sign in at this store's tills.
        Guid elsewhere;
        await using (var ctx = db.CreateContext())
        {
            var store = ctx.Stores.Add(new Store { Code = Unique("S"), Name = "Elsewhere" }).Entity;
            await ctx.SaveChangesAsync();
            elsewhere = store.Id;
        }
        var away = await UserAsync(Permissions.CashierRole);
        away.StoreId = elsewhere;
        await setup.SaveUserAsync(away, null);
        Assert.Equal(SignInFailure.OtherStore, (await setup.SignInAsync(away.UserName, "pass")).Failure);
    }

    // Approval ----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task An_approver_needs_approve_and_the_permission_by_password_or_pin()
    {
        var cashier = await UserAsync(Permissions.CashierRole, pin: "1357");
        var (data, _, _) = await SignedInAsync(cashier);
        var setup = new SetupService(data);
        var supervisor = await UserAsync(Permissions.SupervisorRole, pin: "9753");
        var floor = await setup.SaveRoleAsync(new Role { Name = Unique("Approver only "), Level = 2 }, [Permissions.PosApprove]);
        var approverOnly = await setup.SaveUserAsync(new UserAccount { UserName = Unique("a"), RoleId = floor.Id }, "pass");

        var (byPassword, _) = await setup.ApproveAsync(supervisor.UserName, "pass", false, Permissions.PosVoidSale, "Voiding a sale");
        Assert.Equal(supervisor.Id, byPassword?.Id);
        var (byPin, _) = await setup.ApproveAsync(supervisor.StaffCode!, "9753", true, Permissions.PosVoidSale, "Voiding a sale");
        Assert.Equal(supervisor.Id, byPin?.Id);
        var (wrongPin, problem) = await setup.ApproveAsync(supervisor.StaffCode!, "1111", true, Permissions.PosVoidSale, "Voiding a sale");
        Assert.Null(wrongPin);
        Assert.NotNull(problem);
        // Approve alone is not enough, nor is the permission without approve, nor what a supervisor may not do.
        Assert.Null((await setup.ApproveAsync(approverOnly.UserName, "pass", false, Permissions.PosVoidSale, "Voiding a sale")).Approver);
        Assert.Null((await setup.ApproveAsync(cashier.StaffCode!, "1357", true, Permissions.PosDrawer, "Money out")).Approver);
        Assert.Null((await setup.ApproveAsync(supervisor.UserName, "pass", false, Permissions.SetupUsers, "Changing users")).Approver);

        var log = await new AuditService(data).SearchAsync(new AuditFilter(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)));
        Assert.Contains(log, a => a.Action == AuditActions.Approval && a.EntityId == supervisor.Id && a.Details!.Contains(cashier.UserName, StringComparison.Ordinal));
        Assert.Contains(log, a => a.Action == AuditActions.ApprovalRefused && a.EntityId == approverOnly.Id);
        Assert.Contains(log, a => a.Action == AuditActions.FailedSignIn && a.UserId == supervisor.Id);
    }

    // User ids ----------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task What_a_user_does_carries_their_id_next_to_their_name()
    {
        var user = await UserAsync(Permissions.ManagerRole);
        var (data, session, location) = await SignedInAsync(user);
        var item = await ItemAsync(data, location.Id, "UID1", 10m);
        var client = await new PartyService(data).SaveClientAsync(new Client { LastName = "Ids" });

        var sales = new SalesService(data);
        var sale = await sales.PostSaleAsync(new SaleDraft { Lines = [Line(item, 2)] }, [Cash(20m)], session);
        Assert.Equal((user.UserName, (Guid?)user.Id), (sale.Staff, sale.StaffId));
        var onAccount = await sales.PostSaleAsync(new SaleDraft { ClientId = client.Id, ClientName = "Ids", Lines = [Line(item, 1)] },
            [new TenderEntry("On Account", TenderKind.OnAccount, 10m, 10m)], session);
        await sales.VoidSaleAsync(sale.Id, session, "test");
        var voided = (await sales.GetSaleAsync(sale.Id))!;
        Assert.Equal((user.UserName, (Guid?)user.Id), (voided.VoidedBy, voided.VoidedById));

        var drawer = new DrawerService(data);
        await drawer.AddMovementAsync(session, DrawerMovementKind.NoSale, 0, "change");
        var closed = await drawer.EndOfDayAsync(session);
        Assert.Equal((user.UserName, (Guid?)user.Id), (closed.Session.ClosedBy, closed.Session.ClosedById));
        Assert.All(closed.Movements, m => Assert.Equal((Guid?)user.Id, m.UserId));

        var invoices = new InvoiceService(data);
        var invoice = (await invoices.ListAsync(InvoiceKind.Invoice, true, clientId: client.Id)).Single(i => i.InvoiceNumber == onAccount.InvoiceNumber);
        await invoices.ApplyPaymentAsync(invoice.Id, 5m, "Cash", DateOnly.FromDateTime(DateTime.Today), session);
        Assert.Equal((Guid?)user.Id, (await invoices.GetAsync(invoice.Id))!.Payments.Single().TakenById);
        Assert.Equal((Guid?)user.Id, (await invoices.GetAsync(invoice.Id))!.SalesPersonId);

        var purchasing = new PurchasingService(data);
        var po = await purchasing.CreateAsync(location.Id, OrderMethod.Blank, "ids");
        await purchasing.AddItemAsync(po.Id, item.Plu, 3);
        var lines = (await purchasing.GetAsync(po.Id))!.Lines;
        await purchasing.PostShipmentAsync(po.Id, lines.Select(l => new ReceivedLine(l.Id, 3, 4m)).ToList());
        var received = (await purchasing.GetAsync(po.Id))!;
        Assert.Equal(((Guid?)user.Id, (Guid?)user.Id), (received.CreatedById, received.ReceivedById));

        await new InventoryService(data).AdjustStockAsync(item.Id, 1, "found", session.UserName, userId: session.UserId);
        var movements = await new InventoryService(data).GetStockMovementsAsync(item.Id);
        Assert.All(movements, m => Assert.Equal((Guid?)user.Id, m.UserId));
        Assert.Contains(movements, m => m.Kind == StockMovementKind.Void);
        Assert.Contains(movements, m => m.Kind == StockMovementKind.Receipt);

        await sales.SaveRmaAsync(new Rma { Plu = item.Plu, Description = "ids", Quantity = 1, IsActive = true });
        Assert.Contains(await sales.GetRmasAsync(true), r => r.Description == "ids" && r.CreatedById == user.Id);
        await sales.SaveCustomerOrderAsync(new CustomerOrder { LocationId = location.Id, Description = "ids order", Quantity = 1 });
        Assert.Contains(await sales.GetCustomerOrdersAsync(true), o => o.Description == "ids order" && o.SoldById == user.Id);

        var log = await new AuditService(data).SearchAsync(new AuditFilter(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today), user.Id));
        Assert.Contains(log, a => a.Action == AuditActions.VoidSale && a.EntityId == sale.Id);
        Assert.Contains(log, a => a.Action == AuditActions.NoSale);
        Assert.Contains(log, a => a.Action == AuditActions.EndOfDay && a.EntityId == closed.Session.Id);
        // The sales themselves are the record: the client's balance is not audited with every sale.
        Assert.DoesNotContain(await new AuditService(data).HistoryAsync(nameof(Client), client.Id), a => a.Action == AuditActions.Changed);
    }

    // Audit log ---------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Changes_to_audited_tables_are_logged_with_the_values_before_and_after()
    {
        var user = await UserAsync(Permissions.ManagerRole);
        var (data, _, location) = await SignedInAsync(user);
        var inventory = new InventoryService(data);
        var audit = new AuditService(data);
        var item = await ItemAsync(data, location.Id, "AUD1", 10m);

        // Saved the way the item editor saves: a copy read earlier, so only what really changed is logged.
        var copy = (await inventory.GetAsync(item.Id))!;
        copy.Price1 = 12.5m;
        await inventory.SaveAsync(copy, "STAFFTEST", null, null);
        await inventory.DeleteAsync(item.Id);
        await inventory.RestoreAsync(item.Id);

        var history = await audit.HistoryAsync(nameof(Item), item.Id);
        Assert.Equal(new[] { AuditActions.Added, AuditActions.Changed, AuditActions.Deleted, AuditActions.Undeleted }.Order(), history.Select(a => a.Action).Order());
        Assert.All(history, a => Assert.Equal(((Guid?)user.Id, user.UserName), (a.UserId, a.UserName)));
        Assert.All(history, a => Assert.Equal($"AUD1 Item AUD1", a.Details));
        var changed = history.Single(a => a.Action == AuditActions.Changed);
        Assert.Equal(10m, Values(changed.Before)[nameof(Item.Price1)].GetDecimal());
        Assert.Equal(12.5m, Values(changed.After)[nameof(Item.Price1)].GetDecimal());
        Assert.DoesNotContain(nameof(Item.Name), Values(changed.After).Keys);
        Assert.DoesNotContain(nameof(Item.LastUpdated), Values(changed.After).Keys);
        Assert.Contains(AuditService.Changes(changed), c => c.Property == nameof(Item.Price1) && c.Before == 10.ToString(System.Globalization.CultureInfo.CurrentCulture)
                                                           && c.After == 12.5m.ToString(System.Globalization.CultureInfo.CurrentCulture));
        Assert.Equal("Item AUD1", Values(history.Single(a => a.Action == AuditActions.Added).After)[nameof(Item.Name)].GetString());

        // Nothing changed, nothing logged.
        await inventory.SaveAsync((await inventory.GetAsync(item.Id))!, "STAFFTEST", null, null);
        Assert.Equal(4, (await audit.HistoryAsync(nameof(Item), item.Id)).Count);

        var found = await audit.SearchAsync(new AuditFilter(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today), user.Id, AuditActions.Changed, "aud1"));
        Assert.Contains(found, a => a.EntityId == item.Id);
        Assert.Contains(AuditActions.Changed, await audit.GetActionsAsync());
    }

    [SkippableFact]
    public async Task Passwords_are_never_logged_and_sign_ins_are()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var setup = new SetupService(db.Data);
        var user = await UserAsync(Permissions.CashierRole);
        await setup.SaveUserAsync(user, "another secret");
        Assert.Null(await setup.AuthenticateAsync(user.UserName, "wrong"));

        var history = await new AuditService(db.Data).HistoryAsync(nameof(UserAccount), user.Id);
        var changed = history.Single(a => a.Action == AuditActions.Changed);
        Assert.Equal("(hidden)", Values(changed.After)[nameof(UserAccount.PasswordHash)].GetString());
        Assert.All(history, a => Assert.DoesNotContain("pbkdf2", (a.Before ?? "") + (a.After ?? ""), StringComparison.Ordinal));
        Assert.Contains(history, a => a.Action == AuditActions.SignIn && a.UserId == user.Id);
        Assert.Contains(history, a => a.Action == AuditActions.FailedSignIn && a.UserId == user.Id && a.Details!.Contains("wrong password", StringComparison.Ordinal));
        // Failed sign-ins are their own lines: the counter they raise is not logged as a change.
        Assert.DoesNotContain(history, a => (a.After ?? "").Contains(nameof(UserAccount.FailedSignIns), StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Nothing_is_logged_while_audit_is_suppressed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        Client quiet, loud;
        await using (var ctx = db.CreateContext())
        {
            ctx.SuppressAudit = true;
            quiet = ctx.Clients.Add(new Client { LastName = "Quiet", Number = Unique("Q") }).Entity;
            await ctx.SaveChangesAsync();
            quiet.City = "Nowhere";
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext())
        {
            loud = ctx.Clients.Add(new Client { LastName = "Loud", Number = Unique("L") }).Entity;
            await ctx.SaveChangesAsync();
        }
        var audit = new AuditService(db.Data);
        Assert.Empty(await audit.HistoryAsync(nameof(Client), quiet.Id));
        Assert.Equal(AuditActions.Added, Assert.Single(await audit.HistoryAsync(nameof(Client), loud.Id)).Action);

        // A pooled context handed out again does not keep the flag.
        var data = db.Data;
        await using (var first = data.Create()) first.SuppressAudit = true;
        await using (var second = data.Create()) Assert.False(second.SuppressAudit);
    }

    // Time clock --------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_user_punches_in_once_and_only_supervisors_change_entries_with_a_reason()
    {
        var cashier = await UserAsync(Permissions.CashierRole);
        var supervisor = await UserAsync(Permissions.SupervisorRole);
        var (data, cashierSession, _) = await SignedInAsync(cashier);
        var staff = new StaffService(data);

        var shift = await staff.PunchInAsync(cashier.Id);
        Assert.True(shift.IsOpen);
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.PunchInAsync(cashier.Id));
        Assert.Contains(await staff.WhoIsInAsync(), e => e.UserId == cashier.Id);
        var done = await staff.PunchAsync(cashier.Id);
        Assert.False(done.IsOpen);
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.PunchOutAsync(cashier.Id));

        var day = DateTime.Today.AddDays(-3);
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.EditEntryAsync(shift.Id, day.AddHours(9), day.AddHours(17), "forgot", cashierSession));
        var supervisorSession = cashierSession.As(supervisor);
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.EditEntryAsync(shift.Id, day.AddHours(9), day.AddHours(17), " ", supervisorSession));
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.EditEntryAsync(shift.Id, day.AddHours(17), day.AddHours(9), "backwards", supervisorSession));
        var edited = await staff.EditEntryAsync(shift.Id, day.AddHours(9), day.AddHours(17), "Forgot to punch", supervisorSession);
        Assert.Equal(("Forgot to punch", supervisor.UserName, (Guid?)supervisor.Id), (edited.EditReason, edited.EditedBy, edited.EditedById));
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.AddEntryAsync(cashier.Id, day.AddHours(16), day.AddHours(18), "overlap", supervisorSession));

        var log = await new AuditService(data).HistoryAsync(nameof(TimeClockEntry), shift.Id);
        var edit = log.Single(a => a.Action == AuditActions.TimeClockEdit);
        Assert.Contains("Forgot to punch", edit.Details, StringComparison.Ordinal);
        Assert.Equal(day.AddHours(9), Values(edit.After)[nameof(TimeClockEntry.ClockIn)].GetDateTime());
        Assert.Contains(log, a => a.Action == AuditActions.TimeClock);

        // A role without the time clock cannot punch.
        var setup = new SetupService(data);
        var noClock = await setup.SaveRoleAsync(new Role { Name = Unique("No clock "), Level = 1 }, [Permissions.PosSell]);
        var other = await setup.SaveUserAsync(new UserAccount { UserName = Unique("n"), RoleId = noClock.Id }, "pass");
        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.PunchInAsync(other.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.DeleteUserAsync(cashier.Id));
    }

    [SkippableFact]
    public async Task Hours_add_up_per_day_across_midnight()
    {
        var worker = await UserAsync(Permissions.CashierRole);
        var supervisor = await UserAsync(Permissions.SupervisorRole);
        var (data, session, _) = await SignedInAsync(supervisor);
        var staff = new StaffService(data);
        var monday = TimeClock.WeekStart(DateOnly.FromDateTime(DateTime.Today)).AddDays(-14).ToDateTime(TimeOnly.MinValue);
        await staff.AddEntryAsync(worker.Id, monday.AddHours(9), monday.AddHours(13), "paper sheet", session);
        await staff.AddEntryAsync(worker.Id, monday.AddHours(14), monday.AddHours(17.5), "paper sheet", session);
        await staff.AddEntryAsync(worker.Id, monday.AddHours(22), monday.AddHours(26.25), "night shift", session);

        var hours = await staff.HoursByDayAsync(DateOnly.FromDateTime(monday), DateOnly.FromDateTime(monday.AddDays(6)), worker.Id);
        Assert.Equal([(DateOnly.FromDateTime(monday), 9.5m), (DateOnly.FromDateTime(monday.AddDays(1)), 2.25m)], hours.Select(h => (h.Day, h.Hours)));
        Assert.Equal(11.75m, hours.Sum(h => h.Hours));
        // Only the days asked for: the night shift's hours after midnight belong to Tuesday.
        Assert.Equal(9.5m, (await staff.HoursByDayAsync(DateOnly.FromDateTime(monday), DateOnly.FromDateTime(monday), worker.Id)).Sum(h => h.Hours));

        var report = await new Reports.ReportService(data).RunAsync("hours-week", Guid.Empty, DateOnly.FromDateTime(monday), DateOnly.FromDateTime(monday.AddDays(6)));
        Assert.Contains(report.Rows, r => (string?)r[0] == worker.UserName && (decimal)r[3]! == 11.75m);
    }

    // Commission --------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Commission_comes_from_the_best_rule_or_else_the_item_rate()
    {
        var seller = await UserAsync(Permissions.CashierRole);
        var supervisor = await UserAsync(Permissions.SupervisorRole);
        var (data, session, location) = await SignedInAsync(seller);
        var department = Unique("COMM");
        var ruled = await ItemAsync(data, location.Id, "COM1", 100m, department, commissionPercent: 2);
        var plain = await ItemAsync(data, location.Id, "COM2", 50m, null, commissionPercent: 4);
        var staff = new StaffService(data);
        Guid deptId;
        await using (var ctx = data.Create()) deptId = (await ctx.Departments.SingleAsync(d => d.Name == department)).Id;

        await Assert.ThrowsAsync<BusinessRuleException>(() => staff.SaveRuleAsync(new CommissionRule { Name = "x", Scope = CommissionScope.AllItems, Rate = 1 }, session));
        var boss = session.As(supervisor);
        await staff.SaveRuleAsync(new CommissionRule { Name = "Dept 10%", Scope = CommissionScope.Department, DepartmentId = deptId, Rate = 10 }, boss);
        await staff.SaveRuleAsync(new CommissionRule { Name = "Seller 1 a unit", Scope = CommissionScope.Item, ItemId = ruled.Id, UserId = seller.Id, Rate = 1, Basis = CommissionBasis.AmountPerUnit }, boss);
        await staff.SaveRuleAsync(new CommissionRule { Name = "Expired", Scope = CommissionScope.Item, ItemId = plain.Id, Rate = 50, EffectiveTo = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) }, boss);

        var line = Line(plain, 1);
        line.Department = null;
        await new SalesService(data).PostSaleAsync(new SaleDraft { Lines = [Line(ruled, 3), line] }, [Cash(350m)], session);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var row = Assert.Single(await staff.CommissionByStaffAsync(today, today, location.Id));
        Assert.Equal(((Guid?)seller.Id, seller.UserName, 1, 4m, 350m), (row.StaffId, row.Staff, row.Sales, row.Units, row.NetSales));
        Assert.Equal(3m, row.FromRules);   // 3 units at 1 each, the seller's item rule beating the department's 10%
        Assert.Equal(2m, row.FromItems);   // 4% of 50, the item's own rate: the expired rule no longer counts
        var lines = await staff.CommissionLinesAsync(today, today, location.Id, row.Key);
        Assert.Equal(["Seller 1 a unit", null], lines.Select(l => l.Rule));

        var staffReport = await new Reports.ReportService(data).RunAsync("staff", location.Id, today, today);
        Assert.Equal(5m, (decimal)staffReport.Rows.Single()[4]!);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new SetupService(data).DeleteDepartmentAsync(department));
    }
}

public sealed class StaffSecurityTestsOnSqlite(SqliteFixture db) : StaffSecurityTests(db), IClassFixture<SqliteFixture>;

public sealed class StaffSecurityTestsOnSqlServer(SqlServerFixture db) : StaffSecurityTests(db), IClassFixture<SqlServerFixture>;
