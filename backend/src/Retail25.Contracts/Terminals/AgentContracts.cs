namespace Retail25.Contracts.Terminals;

/// <summary>
/// What the agent tells the server about its hardware. Sent on connect and on a heartbeat, so the
/// status strip shows red within fifteen seconds of a device going away rather than silently
/// reading nothing (doc 06 §3).
/// </summary>
public sealed record AgentStatusReport(
    string StationId,
    string? AgentVersion,
    bool ReaderOnline,
    bool PrinterOnline,
    bool ScaleOnline,
    bool DrawerOnline,
    bool PoleDisplayOnline,
    int ReadRate);

public sealed record WeightReport(string StationId, decimal Value, string Unit, bool Stable);

/// <summary>
/// A print job's outcome. A failure is reported but is never fatal: the sale is already saved and
/// the receipt stays reprintable, which preserves the legacy "printer jammed" story (guide p.12).
/// </summary>
public sealed record PrintResult(string StationId, Guid TransactionId, bool Succeeded, string? Error);

/// <summary>
/// Method names on <c>TerminalHub</c>, in one place so the agent and the server cannot disagree
/// about a string. Invoked by name because SignalR has no compile-time contract of its own.
/// </summary>
public static class TerminalHubMethods
{
    public static class ToServer
    {
        public const string RegisterStation = "RegisterStation";
        public const string PublishTags = "PublishTags";
        public const string ReportWeight = "ReportWeight";
        public const string ReportStatus = "ReportStatus";
        public const string ReportPrintResult = "ReportPrintResult";
    }

    public static class ToAgent
    {
        public const string PrintReceipt = "PrintReceipt";
        public const string OpenDrawer = "OpenDrawer";
        public const string DisplayPole = "DisplayPole";
        public const string RequestWeight = "RequestWeight";
        public const string ZeroScale = "ZeroScale";
        public const string SetReaderMode = "SetReaderMode";
        public const string UpdateProfile = "UpdateProfile";
    }
}
