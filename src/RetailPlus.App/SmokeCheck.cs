using Microsoft.EntityFrameworkCore;
using RetailPlus.App.Infrastructure;
using RetailPlus.App.Views;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.App;

/// <summary>
/// <c>--smoke</c>: opens every screen, runs every report and rings up one cash sale through the POS screen's own code,
/// then exits with 0 when all of it worked. Results go to the check log.
/// </summary>
public static class SmokeCheck
{
    public static async Task<int> RunAsync(MainWindow main)
    {
        var failures = new List<string>();
        Log.Write("Smoke check started on " + AppServices.Data.DatabaseName);
        foreach (var entry in MainWindow.AllEntries)
        {
            try
            {
                main.Navigate(entry.Key);
                await MainWindow.Settle();
                Log.Write($"Screen {entry.Key} ok");
            }
            catch (Exception ex)
            {
                failures.Add($"screen {entry.Key}: {ex.Message}");
            }
        }

        failures.AddRange(await ReportsView.RunAllAsync());

        try
        {
            const string code = "SMOKE-TEST";
            var (item, _) = await AppServices.Inventory.FindByCodeAsync(AppServices.Session.LocationId, code);
            if (item is null)
            {
                item = await AppServices.Inventory.SaveAsync(new Item
                {
                    LocationId = AppServices.Session.LocationId, Plu = code, Name = "Smoke test item", Price1 = 9.99m, LastCost = 4m, AverageCost = 4m,
                    Taxable1 = true, Taxable2 = true, OnHand = 100,
                }, "SMOKE", null, null);
            }
            main.Navigate("pos");
            await MainWindow.Settle();
            var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
            // A barcode-only sale: RFID antennas on this station must not add their tags to it.
            pos.Vm.StopRfid();
            await pos.Vm.ClearSaleAsync(ask: false);
            var before = (await AppServices.Inventory.GetAsync(item.Id))!.OnHand;
            // Scanned twice by its UPC-A barcode (once as a scanner sends EAN-13): one line of 2.
            if (item.Barcode is null) await AppServices.Inventory.AssignInStoreBarcodesAsync([item.Id]);
            var barcode = (await AppServices.Inventory.GetAsync(item.Id))!.Barcode!;
            await pos.Vm.AddCodeAsync(barcode);
            await pos.Vm.AddCodeAsync("0" + barcode);
            if (pos.Vm.Lines.Count != 1 || pos.Vm.Lines[0].Quantity != 2)
                failures.Add($"scanning barcode {barcode} twice gave {pos.Vm.Lines.Count} line(s), quantity {pos.Vm.Lines.Sum(l => l.Quantity)}; expected 1 line of 2");
            Log.Write($"Barcode {barcode} scanned twice: {pos.Vm.Lines.Count} line, quantity {pos.Vm.Lines.Sum(l => l.Quantity)}");
            var total = pos.Vm.Total;
            var cash = Money.RoundToMinimumTender(total, AppServices.Settings.MinimumTender);
            bool posted = await pos.Vm.PostAsync([new TenderEntry("Cash", TenderKind.Cash, cash, cash + 5)]);
            if (!posted) failures.Add("POS sale was not saved");
            var after = (await AppServices.Inventory.GetAsync(item.Id))!.OnHand;
            if (!AppServices.Session.IsTrainee && after != before - 2) failures.Add($"stock went from {before} to {after}, expected {before - 2}");
            var report = await AppServices.Drawer.GetCurrentAsync(AppServices.Session);
            Log.Write($"Sale posted: total {total}, drawer receipts now {report.Session.TotalReceipts}, customers {report.Session.CustomerCount}");

            // Its receipt went to a text file (no receipt printer here); a receipt printer would get logo and barcode too.
            var newest = new System.IO.DirectoryInfo(AppServices.Config.ReceiptFolderOrDefault).GetFiles("receipt_*.txt").MaxBy(f => f.LastWriteTimeUtc);
            var printed = newest is null ? "" : System.IO.File.ReadAllText(newest.FullName);
            if (!printed.Contains("TOTAL", StringComparison.Ordinal) || !printed.Contains("CHANGE", StringComparison.Ordinal))
                failures.Add($"receipt text has no TOTAL or CHANGE ({newest?.Name ?? "no file"})");
            var job = EscPosReceipt.Build(await Dialogs.ReceiptWindow.ForSaleAsync(MainWindow.SampleSale()), 40, ReceiptLogo.Raster()).ToArray();
            if (job.AsSpan().IndexOf(new byte[] { 0x1D, 0x76, 0x30, 0x00 }) < 0 || job.AsSpan().IndexOf(new byte[] { 0x1D, 0x6B, 0x49 }) < 0)
                failures.Add("the receipt printer job has no logo or no barcode");
            Log.Write($"Receipt ok: {newest?.Name}, printer job {job.Length} bytes");
        }
        catch (Exception ex)
        {
            failures.Add("POS sale: " + ex);
        }

        try
        {
            failures.AddRange(BarcodeDrawingCheck());
            failures.AddRange(LabelPdfCheck());
        }
        catch (Exception ex)
        {
            failures.Add("Barcode drawing: " + ex);
        }

        try
        {
            failures.AddRange(await StaffCheckAsync(main));
            failures.AddRange(HelpAndPaletteCheck(main));
            failures.AddRange(PermissionCheck(main));
        }
        catch (Exception ex)
        {
            failures.Add("Staff, audit, help: " + ex);
        }

        try
        {
            failures.AddRange(await PictureCheckAsync(main));
            failures.AddRange(await TaggedStockCheckAsync(main));
            failures.AddRange(await RfidCheckAsync(main));
        }
        catch (Exception ex)
        {
            failures.Add("RFID: " + ex);
        }

        foreach (var f in failures) Log.Write("FAIL " + f);
        Log.Write(failures.Count == 0 ? "Smoke check passed" : $"Smoke check failed ({failures.Count})");
        return failures.Count == 0 ? 0 : 5;
    }

    /// <summary>
    /// Staff and audit: a user is added (audited), signs in with a staff code and PIN, punches in and out, a supervisor's
    /// correction across midnight shows in the hours, and the Staff and Audit screens show it. The user and their entries
    /// are removed again so the database still opens without a login.
    /// </summary>
    private static async Task<List<string>> StaffCheckAsync(MainWindow main)
    {
        var failures = new List<string>();
        var name = "smoke" + DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var roles = await AppServices.Setup.GetRolesAsync();
        var user = await AppServices.Setup.SaveUserAsync(new UserAccount { UserName = name, RoleId = roles.Single(r => r.Name == Permissions.CashierRole).Id }, "smoke-pass");
        try
        {
            await AppServices.Setup.SetPinAsync(user.Id, name[^8..], "4711");
            var signIn = await AppServices.Setup.SignInWithPinAsync(name[^8..], "4711");
            if (signIn.User?.Id != user.Id) failures.Add($"staff code and PIN sign-in failed: {signIn.Message}");
            await AppServices.Staff.PunchInAsync(user.Id);
            try
            {
                await AppServices.Staff.PunchInAsync(user.Id);
                failures.Add("a user could punch in twice");
            }
            catch (Data.Services.BusinessRuleException)
            {
            }
            var shift = await AppServices.Staff.PunchOutAsync(user.Id);
            var night = DateTime.Today.AddDays(-1).AddHours(22);
            await AppServices.Staff.EditEntryAsync(shift.Id, night, night.AddHours(5), "Smoke check", AppServices.Session);
            var hours = await AppServices.Staff.HoursByDayAsync(DateOnly.FromDateTime(night), DateOnly.FromDateTime(night.AddDays(1)), user.Id);
            if (hours.Sum(h => h.Hours) != 5m || hours.Count != 2) failures.Add($"a 22:00 to 03:00 shift gave {string.Join(", ", hours.Select(h => $"{h.Day}: {h.Hours}"))}; expected 2 + 3 hours");

            var history = await AppServices.Audit.HistoryAsync(nameof(UserAccount), user.Id);
            if (!history.Any(a => a.Action == Data.AuditActions.Added)) failures.Add("adding a user was not audited");
            if (!history.Any(a => a.Action == Data.AuditActions.SignIn)) failures.Add("a sign-in was not audited");
            if (!(await AppServices.Audit.HistoryAsync(nameof(TimeClockEntry), shift.Id)).Any(a => a.Action == Data.AuditActions.TimeClockEdit))
                failures.Add("a time clock correction was not audited");

            main.Navigate("staff");
            await MainWindow.Settle();
            if (main.GetView<StaffView>() is null) failures.Add("the Staff screen did not open");
            main.Navigate("audit");
            await MainWindow.Settle();
            if (main.GetView<AuditView>() is null) failures.Add("the Audit screen did not open");
            Log.Write($"Staff: {name} signed in with a PIN, punched in and out, a corrected night shift counts {hours.Sum(h => h.Hours)} hours; {history.Count} audit lines");
        }
        finally
        {
            // Leave no user behind: a database with users asks for a login.
            await using var db = AppServices.Data.Create();
            db.TimeClockEntries.RemoveRange(await db.TimeClockEntries.Where(t => t.UserId == user.Id).ToListAsync());
            db.UserAccounts.RemoveRange(await db.UserAccounts.Where(u => u.Id == user.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
        return failures;
    }

    /// <summary>The help finds topics by their words, and the Ctrl+K list finds screens, reports, actions and help.</summary>
    private static List<string> HelpAndPaletteCheck(MainWindow main)
    {
        var failures = new List<string>();
        if (Help.HelpTopics.Search("end of day") is not [{ Key: "drawer" }, ..]) failures.Add("help search for \"end of day\" did not find the drawer topic first");
        if (Help.HelpTopics.Search("zzzz-nothing").Count != 0) failures.Add("help search found something for nonsense");
        if (Help.HelpTopics.All.Select(t => t.Key).Distinct().Count() != Help.HelpTopics.All.Count) failures.Add("two help topics have the same key");
        foreach (var topic in Help.HelpTopics.All.Where(t => t.Screen is not null && MainWindow.AllEntries.All(e => e.Key != t.Screen)))
            failures.Add($"help topic {topic.Key} points at a screen that does not exist ({topic.Screen})");
        main.Navigate("help");
        if (main.GetView<HelpView>() is not { } help) failures.Add("the Help screen did not open");
        else help.ShowTopic("timeclock");

        var palette = new Dialogs.CommandPalette(main);
        string First(string text) => palette.Matches(text) is [var first, ..] ? first.Title : "(nothing)";
        if (First("inv") != "Go to Inventory") failures.Add($"Ctrl+K \"inv\" gave {First("inv")}");
        if (First("end of day") != "End of day") failures.Add($"Ctrl+K \"end of day\" gave {First("end of day")}");
        if (!palette.Matches("hours").Any(c => c.Kind == "Report")) failures.Add("Ctrl+K \"hours\" found no report");
        if (!palette.Matches("pin").Any(c => c.Kind == "Help")) failures.Add("Ctrl+K \"pin\" found no help topic");
        Log.Write($"Help: {Help.HelpTopics.All.Count} topics; Ctrl+K: {palette.Matches(null).Count} screens and actions, \"inv\" → {First("inv")}");
        palette.Close();
        return failures;
    }

    /// <summary>A cashier's role hides the back office: the screens listed follow the permissions.</summary>
    private static List<string> PermissionCheck(MainWindow main)
    {
        var failures = new List<string>();
        var all = AppServices.Session.Permissions;
        try
        {
            AppServices.Session.Permissions = Permissions.DefaultRoles.Single(r => r.Name == Permissions.CashierRole).Permissions.ToHashSet(StringComparer.Ordinal);
            main.RefreshChrome();
            var keys = main.AllowedEntries.Select(e => e.Key).ToList();
            foreach (var hidden in new[] { "setup", "audit", "tools", "reports", "home" })
                if (keys.Contains(hidden)) failures.Add($"a cashier sees the {hidden} screen");
            foreach (var shown in new[] { "pos", "clients", "staff", "help" })
                if (!keys.Contains(shown)) failures.Add($"a cashier does not see the {shown} screen");
            Log.Write("Cashier sees: " + string.Join(", ", keys));
        }
        finally
        {
            AppServices.Session.Permissions = all;
            main.RefreshChrome();
        }
        return failures;
    }

    /// <summary>
    /// Item pictures: a large picture is made 800 pixels wide, kept on the first item, and shown at the POS when that item
    /// is rung up.
    /// </summary>
    private static async Task<List<string>> PictureCheckAsync(MainWindow main)
    {
        var failures = new List<string>();
        var first = (await AppServices.Inventory.SearchAsync(AppServices.Session.LocationId, null, take: 1)).Single();
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Gold, null, new System.Windows.Rect(0, 0, 1600, 1000));
            dc.DrawEllipse(System.Windows.Media.Brushes.DarkOrange, null, new System.Windows.Point(800, 500), 380, 380);
        }
        var big = new System.Windows.Media.Imaging.RenderTargetBitmap(1600, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        big.Render(visual);
        var jpeg = Pictures.Encode(big);
        var back = (System.Windows.Media.Imaging.BitmapSource)Pictures.ToImage(jpeg)!;
        if (back.PixelWidth != 800 || back.PixelHeight != 500) failures.Add($"a 1600 x 1000 picture was kept at {back.PixelWidth} x {back.PixelHeight}, expected 800 x 500");
        await AppServices.Inventory.SetPictureAsync(first.Id, jpeg);

        main.Navigate("pos");
        await MainWindow.Settle();
        var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
        pos.Vm.StopRfid();
        await pos.Vm.ClearSaleAsync(ask: false);
        await pos.Vm.AddCodeAsync(first.Plu);
        for (int i = 0; i < 30 && pos.Vm.LinePicture is null; i++) await Task.Delay(100);
        bool shown = pos.Vm.LinePicture is not null;
        if (!shown) failures.Add($"the POS did not show the picture of {first.Plu}");
        await pos.Vm.ClearSaleAsync(ask: false);
        Log.Write($"Item picture: {jpeg.Length / 1024} KB on {first.Plu}, shown at the POS: {shown}");
        return failures;
    }

    /// <summary>
    /// A tagged item cannot be sold beyond its pieces in stock: with both pieces read by RFID, its barcode scanned again
    /// adds nothing; scanned first, a piece is joined by its tag instead of counted twice.
    /// </summary>
    private static async Task<List<string>> TaggedStockCheckAsync(MainWindow main)
    {
        var failures = new List<string>();
        Log.Write("Speakers: " + (Sounds.IsSilenced() ? "muted or turned down, tag beeps cannot be heard" : "on"));
        string stamp = DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var item = await AppServices.Inventory.SaveAsync(new Item
        {
            LocationId = AppServices.Session.LocationId, Plu = "SMK-P" + stamp, Name = "Smoke tagged pieces", Price1 = 3m, OnHand = 2,
        }, "SMOKE", null, null);
        await AppServices.Inventory.AssignInStoreBarcodesAsync([item.Id]);
        var barcode = (await AppServices.Inventory.GetAsync(item.Id))!.Barcode!;
        string epc1 = "E2801171" + stamp + "00001", epc2 = "E2801171" + stamp + "00002";
        await AppServices.RfidData.RegisterTagAsync(epc1, item.Id);
        await AppServices.RfidData.RegisterTagAsync(epc2, item.Id);

        main.Navigate("pos");
        await MainWindow.Settle();
        var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
        pos.Vm.StopRfid();
        try
        {
            Rfid.RfidTagEvent Read(string epc) => new(epc, AppServices.Session.StationNumber, Guid.Empty, "smoke", 1, -40, DateTime.Now);
            await pos.Vm.ClearSaleAsync(ask: false);
            await pos.Vm.OnTagAsync(Read(epc1));
            await pos.Vm.OnTagAsync(Read(epc2));
            await pos.Vm.AddCodeAsync(barcode);
            var line = pos.Vm.Lines.SingleOrDefault(l => l.ItemId == item.Id);
            if (line is null || line.Quantity != 2 || line.Epcs.Count != 2)
                failures.Add($"2 pieces read by RFID, then the barcode: quantity {line?.Quantity}, {line?.Epcs.Count} tag(s); expected 2 and 2 (the scan refused)");
            if (!pos.Vm.MessageIsAlert) failures.Add($"the refused scan was not shown as a red alert: {pos.Vm.Message}");
            await Task.Delay(3600);
            if (pos.Vm.MessageIsAlert) failures.Add("the red alert was still showing after 3 seconds");

            await pos.Vm.ClearSaleAsync(ask: false);
            await pos.Vm.AddCodeAsync(barcode);
            await pos.Vm.OnTagAsync(Read(epc1));
            line = pos.Vm.Lines.SingleOrDefault(l => l.ItemId == item.Id);
            if (line is null || line.Quantity != 1 || line.Epcs.Count != 1)
                failures.Add($"a piece scanned, then its tag read: quantity {line?.Quantity}, {line?.Epcs.Count} tag(s); expected 1 and 1");
            Log.Write($"Tagged stock: a scan beyond the 2 pieces in stock is refused ({pos.Vm.Message})");
        }
        finally
        {
            await pos.Vm.ClearSaleAsync(ask: false);
            await AppServices.Inventory.DeleteAsync(item.Id);
        }
        return failures;
    }

    /// <summary>
    /// The barcodes the app draws must scan: a label's UPC-A and EAN-13 and the receipt's Code 128 are drawn, photographed
    /// at printer resolution and read back with ZXing's decoder.
    /// </summary>
    private static List<string> BarcodeDrawingCheck()
    {
        var failures = new List<string>();
        var options = new Dialogs.LabelOptions(true, true, "Rs", "SMOKE");
        var ean13 = "896101400001" + Epc.CheckDigit("896101400001");
        foreach (var (code, format) in new[] { (Upc.InStore(1001), ZXing.BarcodeFormat.UPC_A), (ean13, ZXing.BarcodeFormat.EAN_13) })
        {
            var label = Dialogs.LabelCard.Create("Smoke label", "SMOKE-1", code, 450m, Dialogs.LabelLayout.OneLabel, options);
            var read = Decode(label);
            if (read?.Text != code || read.BarcodeFormat != format) failures.Add($"label barcode {code} read back as {read?.Text ?? "nothing"} ({read?.BarcodeFormat})");
        }
        var receipt = Dialogs.ReceiptPaper.Create(Reports.Receipt.FromSale(MainWindow.SampleSale(), new StoreSettings(), null, null));
        var fromReceipt = Decode(receipt);
        if (fromReceipt?.Text != "1005") failures.Add($"receipt barcode read back as {fromReceipt?.Text ?? "nothing"}");
        Log.Write($"Barcodes drawn and read back: UPC-A, EAN-13, receipt Code 128 ({failures.Count} problem(s))");
        return failures;
    }

    /// <summary>
    /// Labels on A4 sheets of 30 saved as a PDF: 31 labels make two pages, a barcode on the sheet scans, and every entry
    /// of the PDF's cross-reference table points at its object.
    /// </summary>
    private static List<string> LabelPdfCheck()
    {
        var failures = new List<string>();
        var code = Upc.InStore(1001);
        var layout = Dialogs.LabelLayout.All.First(l => l.Columns * l.Rows == 30);
        var options = new Dialogs.LabelOptions(true, true, "Rs", "SMOKE");
        var pages = Dialogs.LabelSheet.Pages(Enumerable.Repeat(("Smoke label", "SMOKE-1", code, 450m), 31), layout, options, cutLines: true);
        if (pages.Count != 2) failures.Add($"31 labels on sheets of 30 gave {pages.Count} page(s)");
        var read = Decode(pages[0]);
        if (read?.Text != code) failures.Add($"a barcode on the sheet of 30 read back as {read?.Text ?? "nothing"}");

        using var pdf = new System.IO.MemoryStream();
        PdfPages.Write(pdf, pages, new System.Windows.Size(layout.PageWidth, layout.PageHeight), "Smoke labels");
        var text = System.Text.Encoding.Latin1.GetString(pdf.ToArray());
        if (!text.StartsWith("%PDF-1.4", StringComparison.Ordinal) || !text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal) || !text.Contains("/Count 2", StringComparison.Ordinal))
            failures.Add("the label PDF is not a two-page PDF");
        int xref = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        var table = text[xref..].Split('\n');
        int size = int.Parse(table[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        for (int id = 1; id < size; id++)
        {
            var entry = table[2 + id];
            int at = int.Parse(entry[..10], System.Globalization.CultureInfo.InvariantCulture);
            if (entry[17] == 'n' && !text[at..].StartsWith($"{id} 0 obj", StringComparison.Ordinal)) failures.Add($"label PDF object {id} is not where its cross-reference entry says");
        }
        Log.Write($"Label PDF: {pages.Count} pages of 30, {pdf.Length / 1024} KB, {size - 1} objects");
        return failures;
    }

    private static ZXing.Result? Decode(System.Windows.FrameworkElement element)
    {
        element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new System.Windows.Rect(element.DesiredSize));
        element.UpdateLayout();
        const double dpi = 300;
        int w = (int)Math.Ceiling(element.ActualWidth * dpi / 96), h = (int)Math.Ceiling(element.ActualHeight * dpi / 96);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
        var white = new System.Windows.Media.DrawingVisual();
        using (var dc = white.RenderOpen()) dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(white);
        bitmap.Render(element);
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        var reader = new ZXing.BarcodeReaderGeneric { Options = { TryHarder = true } };
        return reader.Decode(new ZXing.RGBLuminanceSource(pixels, w, h, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32));
    }

    /// <summary>
    /// RFID end to end: a simulated reader whose antenna 1 serves this station "sees" two tagged pieces of one item; the
    /// POS must ring them up by itself on one line of quantity 2, and paying must mark both tags sold.
    /// </summary>
    private static async Task<List<string>> RfidCheckAsync(MainWindow main)
    {
        var failures = new List<string>();
        int station = AppServices.Session.StationNumber;
        const string code = "SMOKE-RFID";
        var (item, _) = await AppServices.Inventory.FindByCodeAsync(AppServices.Session.LocationId, code);
        item ??= await AppServices.Inventory.SaveAsync(new Item
        {
            LocationId = AppServices.Session.LocationId, Plu = code, Name = "Smoke RFID item", Price1 = 5m, LastCost = 2m, AverageCost = 2m, OnHand = 50,
        }, "SMOKE", null, null);
        string epc = "E2801170000" + DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "00";
        string epc2 = epc[..^2] + "01";
        await AppServices.RfidData.RegisterTagAsync(epc, item.Id);
        await AppServices.RfidData.RegisterTagAsync(epc2, item.Id);

        var reader = Data.Services.RfidService.NewReader("Smoke sim " + DateTime.Now.ToString("HHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
        reader.Host = "sim";
        reader.Notes = $"1:{epc}\n1:{epc2}";
        reader.Antennas[0].StationNumber = station;
        reader.Antennas[0].IsEnabled = true;
        reader = await AppServices.RfidData.SaveReaderAsync(reader);

        var mode = AppServices.Config.RfidMode;
        AppServices.Config.RfidMode = Settings.RfidMode.ThisComputer;
        AppServices.Config.RfidAutoAdd = true;
        try
        {
            await AppServices.StartRfidAsync();
            main.Navigate("pos");
            await MainWindow.Settle();
            var pos = main.GetView<PosView>() ?? throw new InvalidOperationException("POS screen did not open");
            pos.Vm.StartRfid();
            var until = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < until && !pos.Vm.Lines.Any(l => l.Epcs.Contains(epc) && l.Epcs.Contains(epc2))) await Task.Delay(200);
            if (!pos.Vm.Lines.Any(l => l.Epcs.Contains(epc) && l.Epcs.Contains(epc2)))
            {
                failures.Add($"RFID tags were not rung up on one line at station {station} (RFID text: {pos.Vm.RfidText}; message: {pos.Vm.Message})");
                return failures;
            }
            var tagged = pos.Vm.Lines.Where(l => l.ItemId == item.Id).ToList();
            if (tagged.Count != 1 || tagged[0].Quantity != 2) failures.Add($"two pieces of one item gave {tagged.Count} line(s), quantity {tagged.Sum(l => l.Quantity)}; expected 1 line of 2");
            Log.Write($"RFID tags {epc} and {epc2} rung up by antenna 1 as one line ({pos.Vm.RfidText})");
            var cash = Money.RoundToMinimumTender(pos.Vm.Total, AppServices.Settings.MinimumTender);
            if (!await pos.Vm.PostAsync([new TenderEntry("Cash", TenderKind.Cash, cash, cash)])) failures.Add("RFID sale was not saved");
            foreach (var tag in (await AppServices.RfidData.GetTagsAsync(item.Id)).Where(t => t.Epc == epc || t.Epc == epc2))
                if (tag.Status != ItemTagStatus.Sold) failures.Add($"tag {tag.Epc} status after the sale is {tag.Status}, expected Sold");
            await Task.Delay(1500);
            if (pos.Vm.Lines.Any(l => l.Epcs.Contains(epc) || l.Epcs.Contains(epc2))) failures.Add("a sold tag still on the antenna was rung up again");
            Log.Write("RFID check done: tag sold, not added twice");
        }
        finally
        {
            await AppServices.RfidData.DeleteReaderAsync(reader.Id);
            AppServices.Config.RfidMode = mode;
            await AppServices.StartRfidAsync();
        }
        return failures;
    }
}
