using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RetailPlus.Devices;

/// <summary>Sends raw bytes (ESC/POS) to a Windows printer queue, bypassing the printer driver's rendering.</summary>
public static partial class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        public string DocName;
        public string? OutputFile;
        public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, ref DocInfo docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] bytes, int count, out int written);

    public static void Send(string printerName, byte[] bytes, string documentName = "Retail Plus receipt")
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot open printer {printerName}.");
        try
        {
            var info = new DocInfo { DocName = documentName, DataType = "RAW" };
            if (StartDocPrinter(handle, 1, ref info) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                StartPagePrinter(handle);
                if (!WritePrinter(handle, bytes, bytes.Length, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }
}
