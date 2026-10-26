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
            Rfid.RfidTagEvent Read(string epc) => new(epc, AppServices.Session.StationNumber, 0, "smoke", 1, -40, DateTime.Now);
            await pos.Vm.ClearSaleAsync(ask: false);
            await pos.Vm.OnTagAsync(Read(epc1));
            await pos.Vm.OnTagAsync(Read(epc2));
            await pos.Vm.AddCodeAsync(barcode);
            var line = pos.Vm.Lines.SingleOrDefault(l => l.ItemId == item.Id);
            if (line is null || line.Quantity != 2 || line.Epcs.Count != 2)
                failures.Add($"2 pieces read by RFID, then the barcode: quantity {line?.Quantity}, {line?.Epcs.Count} tag(s); expected 2 and 2 (the scan refused)");
            if (!pos.Vm.MessageIsAlert) failures.Add($"the refused scan was not shown as a red alert: {pos.Vm.Message}");

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
