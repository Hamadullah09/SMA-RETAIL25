using XSharp;
using XSharp.RDD.Enums;

namespace RddSpike;

/// <summary>Thin helpers over the X# CoreDb API, configured the way a VO 2.8 application runs.</summary>
internal static class Rdd
{
    public static int AssertCount;

    /// <summary>
    /// The NuGet X# build keeps Debug.Assert calls; on .NET (Core) a failed assert kills the process through the
    /// DefaultTraceListener. Replacing the listeners routes asserts here so they are logged instead.
    /// </summary>
    private sealed class AssertLogger : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void Fail(string? message, string? detailMessage)
        {
            if (Interlocked.Increment(ref AssertCount) <= 3)
            {
                var frames = new System.Diagnostics.StackTrace(1, false).ToString().Split('\n')
                    .Select(f => f.Trim()).Where(f => f.IndexOf("XSharp", StringComparison.Ordinal) >= 0).Take(4);
                Console.WriteLine($"    [X# assert] {message} {detailMessage}\n     " + string.Join("\n     ", frames));
            }
        }
    }

    public static void Init()
    {
        System.Diagnostics.Trace.Listeners.Clear();
        System.Diagnostics.Trace.Listeners.Add(new AssertLogger());
        // .NET (Core) only ships UTF/ASCII encodings; the DBF code page 1252 needs the code-pages provider.
#if NET
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
        RuntimeState.Dialect = XSharpDialect.VO;
        RuntimeState.Ansi = true;          // VO default: SetAnsi(TRUE); also selects the VO record lock scheme
        RuntimeState.AutoOpen = true;      // open the production CDX with the table
        RuntimeState.AutoOrder = 0;        // natural order until a tag is selected
        RuntimeState.Deleted = false;      // the spike wants to see deleted records too
        RuntimeState.WinCodePage = 1252;
        // Experiment switches, e.g. RDDSPIKE_SET=StrictRead,HardCommit turns those X# SETs on.
        foreach (var name in (Environment.GetEnvironmentVariable("RDDSPIKE_SET") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            RuntimeState.SetValue((Set)Enum.Parse(typeof(Set), name.Trim(), ignoreCase: true), true);
        CoreDb.RddSetDefault("DBFCDX");
    }

    public static void Open(string path, bool shared = true, string alias = "T")
    {
        if (!CoreDb.UseArea(true, "DBFCDX", path, alias, shared, false))
            throw new InvalidOperationException($"UseArea failed for {path}: {CoreDb._ErrInfoPtr()?.Message}");
    }

    public static object? Info(DbInfo what)
    {
        object? v = null;
        CoreDb.Info((uint)what, ref v);
        return v;
    }

    public static object? OrderInfo(DbOrder_Info what, object? order = null)
    {
        object? v = null;
        CoreDb.OrderInfo((uint)what, null!, order!, ref v);
        return v;
    }

    public static int TagCount() => Convert.ToInt32(OrderInfo(DbOrder_Info.DBOI_ORDERCOUNT));

    public static IEnumerable<(int No, string Name, string Expr)> Tags()
    {
        int count = TagCount();
        for (int i = 1; i <= count; i++)
            yield return (i, (string)OrderInfo(DbOrder_Info.DBOI_NAME, i)!, (string)OrderInfo(DbOrder_Info.DBOI_EXPRESSION, i)!);
    }

    public static uint FieldPos(string name)
    {
        for (uint i = 1; i <= CoreDb.FCount(); i++)
            if (string.Equals(CoreDb.FieldName(i), name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new ArgumentException($"No field {name}");
    }

    public static object? Get(string field)
    {
        object? v = null;
        if (!CoreDb.FieldGet(FieldPos(field), ref v))
            throw new InvalidOperationException($"FieldGet {field} failed");
        return v;
    }

    public static void Put(string field, object value)
    {
        if (!CoreDb.FieldPut(FieldPos(field), value))
            throw new InvalidOperationException($"FieldPut {field} failed: {CoreDb._ErrInfoPtr()?.Message}");
    }

    public static string Str(string field) => (Get(field)?.ToString() ?? "").TrimEnd();

    // X# 3 renamed CoreDb.Goto(object) to GoTo(DWORD) and made LastRec() return DWORD.
#if XSHARP3
    public static bool Goto(uint recno) => CoreDb.GoTo(recno);
#else
    public static bool Goto(uint recno) => CoreDb.Goto(recno);
#endif
    public static int LastRec() => (int)CoreDb.LastRec();
}
