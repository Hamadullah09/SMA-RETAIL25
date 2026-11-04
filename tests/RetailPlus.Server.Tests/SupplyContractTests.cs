using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Server.Tests;

/// <summary>
/// The supply chain answers and requests (phase E2) as the web app reads them: every field the server writes (camelCase,
/// as ASP.NET Core does) is a field of the matching type in <c>web/src/lib/types.ts</c>, in the same order, and every
/// status the server writes as a string is one the web knows. So the web pages, tested against fixtures, work against the
/// real API. Skipped when the web app's sources are not next to the tests.
/// </summary>
public sealed partial class SupplyContractTests
{
    /// <summary>Each server record and the web type it travels as.</summary>
    public static TheoryData<Type, string> Shapes => new()
    {
        { typeof(BatchInfoRow), "BatchRow" }, { typeof(BatchRequest), "BatchRequest" }, { typeof(BatchAdjustRequest), "BatchAdjustRequest" },
        { typeof(ExpiryReport), "ExpiryReport" }, { typeof(BinInfo), "BinRow" }, { typeof(BinRequest), "BinRequest" }, { typeof(BinListRow), "BinListRow" },
        { typeof(BinList), "BinList" }, { typeof(PurchaseOrderListRow), "PurchaseOrderRow" }, { typeof(PurchaseOrderLineInfo), "PurchaseOrderLine" },
        { typeof(ReceivedBatchInfo), "ReceivedBatch" }, { typeof(PurchaseOrderDetail), "PurchaseOrderDetail" }, { typeof(VersionRequest), "VersionRequest" },
        { typeof(ReceiveLineRequest), "ReceiveLineRequest" }, { typeof(ReceiveRequest), "ReceiveRequest" }, { typeof(RequisitionListRow), "RequisitionRow" },
        { typeof(RequisitionLineInfo), "RequisitionLine" }, { typeof(RequisitionDetail), "RequisitionDetail" },
        { typeof(RequisitionLineRequest), "RequisitionLineRequest" }, { typeof(RequisitionRequest), "RequisitionRequest" }, { typeof(OrderMade), "OrderMade" },
        { typeof(OrdersMade), "OrdersMade" }, { typeof(ApprovalLimitRow), "ApprovalLimitRow" }, { typeof(MatchRow), "MatchRow" },
        { typeof(MatchRun), "MatchRun" }, { typeof(MatchReportInfo), "MatchReportRow" }, { typeof(LandedCostListRow), "LandedCostRow" },
        { typeof(LandedChargeInfo), "LandedCharge" }, { typeof(LandedAllocationInfo), "LandedAllocation" }, { typeof(LandedReceiptInfo), "LandedReceipt" },
        { typeof(LandedCostDetail), "LandedCostDetail" }, { typeof(LandedCostRequest), "LandedCostRequest" }, { typeof(ReceiptChoiceInfo), "ReceiptChoice" },
        { typeof(ReorderRow), "ReorderRow" }, { typeof(ReorderLineRequest), "ReorderLineRequest" }, { typeof(ReorderRequest), "ReorderRequest" },
        { typeof(SupplierPerformanceRow), "SupplierPerformanceRow" }, { typeof(SupplySettingsInfo), "SupplySettings" },
    };

    /// <summary>Each status the server writes as a string, and the web's union of it.</summary>
    public static TheoryData<Type, string> Statuses => new()
    {
        { typeof(PurchaseOrderStatus), "PurchaseOrderStatus" }, { typeof(RequisitionStatus), "RequisitionStatus" }, { typeof(BillMatchStatus), "BillMatchStatus" },
        { typeof(LandedCostStatus), "LandedCostStatus" }, { typeof(LandedCostBasis), "LandedCostBasis" }, { typeof(LandedCostChargeKind), "LandedCostChargeKind" },
    };

    [SkippableTheory]
    [MemberData(nameof(Shapes))]
    public void The_web_type_has_the_fields_the_server_writes(Type record, string webType)
    {
        var web = WebTypes();
        Skip.If(web is null, "web/src/lib/types.ts is not next to the tests.");
        var fields = Interface(web, webType);
        Assert.True(fields is not null, $"types.ts has no interface {webType}.");
        // A record's properties come in the order of its parameters, which is the order the server writes them in.
        var server = record.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.DeclaringType == record)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));
        Assert.Equal(string.Join(",", server), fields);
    }

    [SkippableTheory]
    [MemberData(nameof(Statuses))]
    public void The_web_knows_every_status_the_server_writes(Type status, string webType)
    {
        var web = WebTypes();
        Skip.If(web is null, "web/src/lib/types.ts is not next to the tests.");
        var union = Regex.Match(web, $@"export type {webType} = ([^;]+);");
        Assert.True(union.Success, $"types.ts has no type {webType}.");
        Assert.Equal(Enum.GetNames(status), Quoted().Matches(union.Groups[1].Value).Select(m => m.Groups[1].Value));
    }

    private static string? Interface(string web, string name)
    {
        var m = Regex.Match(web, $@"export interface {name} \{{(.*?)\n\}}", RegexOptions.Singleline);
        return m.Success ? string.Join(",", Field().Matches(m.Groups[1].Value).Select(f => f.Groups[1].Value)) : null;
    }

    private static string? WebTypes()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "web", "src", "lib", "types.ts");
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        return null;
    }

    [GeneratedRegex(@"^\s+(\w+)\??:", RegexOptions.Multiline)]
    private static partial Regex Field();

    [GeneratedRegex(@"'(\w+)'")]
    private static partial Regex Quoted();
}
