using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public record PrinterInfo(string Name, string Driver, string Port, bool Offline, bool IsDefault)
{
    // Nom/drayverdan taxmin: POS-80C, XP-58, TM-T20, RP80, "Receipt", "Thermal"...
    static readonly Regex ReceiptRx = new(@"pos|receipt|thermal|xprinter|gprinter|rongta|bixolon|sunmi|\btm-|\bxp-|\brp-?\d|\b(58|80)(mm|c)?\b", RegexOptions.IgnoreCase);
    static readonly Regex VirtualRx = new(@"pdf|xps|onenote|fax|anydesk|microsoft", RegexOptions.IgnoreCase);

    public bool Receipt => !VirtualRx.IsMatch(Name + " " + Driver) && ReceiptRx.IsMatch(Name + " " + Driver);
    public int Columns => Regex.IsMatch(Name + " " + Driver, @"\b58") ? 32 : 48;
}

static class Printers
{
    static readonly string SavedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MebelimPrinterAgent", "printer.txt");

    // Tanlangan printer shu kompyuterda saqlanadi — brauzer/xodim almashsa ham o'zgarmaydi
    public static string? Saved
    {
        get => File.Exists(SavedPath) ? File.ReadAllText(SavedPath).Trim() : null;
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedPath)!);
            File.WriteAllText(SavedPath, value ?? "");
        }
    }

    public static List<PrinterInfo> List()
    {
        const int flags = 2 | 4; // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
        EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0) return new();

        var def = DefaultName();
        var buf = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(flags, null, 2, buf, needed, out _, out var count)) return new();
            var size = Marshal.SizeOf<PRINTER_INFO_2>();
            var list = new List<PrinterInfo>();
            for (int i = 0; i < count; i++)
            {
                var p = Marshal.PtrToStructure<PRINTER_INFO_2>(buf + i * size);
                // WORK_OFFLINE atributi — USB printer uzilganda Windows o'zi qo'yadi
                var offline = (p.Attributes & 0x400) != 0 || (p.Status & (0x80 | 0x1000)) != 0;
                list.Add(new(p.pPrinterName ?? "", p.pDriverName ?? "", p.pPortName ?? "", offline, p.pPrinterName == def));
            }
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static bool SendRaw(string printer, byte[] data)
    {
        if (!OpenPrinter(printer, out var h, IntPtr.Zero)) return false;
        try
        {
            if (!StartDocPrinter(h, 1, new DOC_INFO_1())) return false;
            try
            {
                if (!StartPagePrinter(h)) return false;
                try
                {
                    var p = Marshal.AllocHGlobal(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, p, data.Length);
                        return WritePrinter(h, p, data.Length, out var written) && written == data.Length;
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
                finally { EndPagePrinter(h); }
            }
            finally { EndDocPrinter(h); }
        }
        finally { ClosePrinter(h); }
    }

    static string? DefaultName()
    {
        int size = 0;
        GetDefaultPrinter(null, ref size);
        if (size == 0) return null;
        var sb = new StringBuilder(size);
        return GetDefaultPrinter(sb, ref size) ? sb.ToString() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PRINTER_INFO_2
    {
        public string? pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation;
        public IntPtr pDevMode;
        public string? pSepFile, pPrintProcessor, pDatatype, pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class DOC_INFO_1
    {
        public string pDocName = "Mebelim chek";
        public string? pOutputFile;
        public string pDatatype = "RAW";
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumPrinters(int flags, string? name, int level, IntPtr buf, int cbBuf, out int needed, out int returned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDefaultPrinter(StringBuilder? buf, ref int size);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr h, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool StartDocPrinter(IntPtr h, int level, [In] DOC_INFO_1 di);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndDocPrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool StartPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr h, IntPtr buf, int count, out int written);
}
