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
        }
        catch (Exception ex)
        {
            failures.Add("Barcode drawing: " + ex);
        }

        try
        {
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
            var label = Dialogs.LabelCard.Create("Smoke label", "SMOKE-1", code, 450m, Dialogs.LabelLayout.All[0], options);
            var read = Decode(label);
            if (read?.Text != code || read.BarcodeFormat != format) failures.Add($"label barcode {code} read back as {read?.Text ?? "nothing"} ({read?.BarcodeFormat})");
        }
        var receipt = Dialogs.ReceiptPaper.Create(Reports.Receipt.FromSale(MainWindow.SampleSale(), new StoreSettings(), null, null));
        var fromReceipt = Decode(receipt);
        if (fromReceipt?.Text != "1005") failures.Add($"receipt barcode read back as {fromReceipt?.Text ?? "nothing"}");
        Log.Write($"Barcodes drawn and read back: UPC-A, EAN-13, receipt Code 128 ({failures.Count} problem(s))");
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
