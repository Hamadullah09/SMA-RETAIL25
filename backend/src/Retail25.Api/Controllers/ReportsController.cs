using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Retail25.Application.Reports;

namespace Retail25.Api.Controllers;

/// <summary>
/// The analytical reports (guide p.15–27, p.56, p.83–84). Every one of them also exports as CSV,
/// which is what the back office actually lives on between month-ends.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/reports")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    private readonly ISender _sender;

    public ReportsController(ISender sender) => _sender = sender;

    // -----------------------------------------------------------------------------------------
    // Sales analysis — by product, department, client or period; top sellers when Top is set.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Revenue only. Cost and margin are stripped server-side, so a manager without cost visibility
    /// receives a payload that never held them rather than one the browser is trusted to hide.
    /// </summary>
    [HttpGet("sales-analysis")]
    public async Task<IActionResult> SalesAnalysis(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] SalesAnalysisGroupBy groupBy = SalesAnalysisGroupBy.Product,
        [FromQuery] long? departmentId = null,
        [FromQuery] long? productId = null,
        [FromQuery] long? customerId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int? top = null,
        [FromQuery] string? sortBy = null)
        => Ok(await _sender.Send(new SalesAnalysisQuery(
            locationId, from, to, groupBy, departmentId, productId, customerId,
            includeVoided, top, sortBy, HideCost: true)));

    [HttpGet("sales-analysis/export")]
    public async Task<IActionResult> ExportSalesAnalysis(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] SalesAnalysisGroupBy groupBy = SalesAnalysisGroupBy.Product,
        [FromQuery] long? departmentId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] string? sortBy = null)
    {
        var csv = await _sender.Send(new ExportSalesAnalysisQuery(new SalesAnalysisQuery(
            locationId, from, to, groupBy, departmentId, null, null, includeVoided, null, sortBy, HideCost: true)));

        return Csv(csv, "sales-analysis");
    }

    /// <summary>The same analysis with cost, margin and COGS — the owner's view.</summary>
    [HttpGet("margin")]
    public async Task<IActionResult> Margin(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] SalesAnalysisGroupBy groupBy = SalesAnalysisGroupBy.Product,
        [FromQuery] long? departmentId = null,
        [FromQuery] long? productId = null,
        [FromQuery] long? customerId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int? top = null,
        [FromQuery] string? sortBy = null)
        => Ok(await _sender.Send(new MarginAnalysisQuery(new SalesAnalysisQuery(
            locationId, from, to, groupBy, departmentId, productId, customerId,
            includeVoided, top, sortBy, HideCost: false))));

    [HttpGet("margin/export")]
    public async Task<IActionResult> ExportMargin(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] SalesAnalysisGroupBy groupBy = SalesAnalysisGroupBy.Product,
        [FromQuery] long? departmentId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] string? sortBy = null)
    {
        var csv = await _sender.Send(new ExportMarginAnalysisQuery(new SalesAnalysisQuery(
            locationId, from, to, groupBy, departmentId, null, null, includeVoided, null, sortBy, HideCost: false)));

        return Csv(csv, "margin-analysis");
    }

    // -----------------------------------------------------------------------------------------
    // Tax
    // -----------------------------------------------------------------------------------------

    [HttpGet("tax")]
    public async Task<IActionResult> Tax(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeVoided = false)
        => Ok(await _sender.Send(new GetTaxReportQuery(locationId, from, to, includeVoided)));

    [HttpGet("tax/export")]
    public async Task<IActionResult> ExportTax(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeVoided = false)
    {
        var csv = await _sender.Send(new ExportTaxReportQuery(new GetTaxReportQuery(locationId, from, to, includeVoided)));
        return Csv(csv, "tax-report");
    }

    // -----------------------------------------------------------------------------------------
    // Stock valuation
    // -----------------------------------------------------------------------------------------

    [HttpGet("stock-value")]
    public async Task<IActionResult> StockValue(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null)
        => Ok(await _sender.Send(new GetStockValuationQuery(locationId, departmentId)));

    [HttpGet("stock-value/detail")]
    public async Task<IActionResult> StockValueDetail(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200)
        => Ok(await _sender.Send(new GetStockValuationDetailQuery(locationId, departmentId, skip, take)));

    [HttpGet("stock-value/export")]
    public async Task<IActionResult> ExportStockValue(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null)
    {
        var csv = await _sender.Send(new ExportStockValuationQuery(locationId, departmentId));
        return Csv(csv, "stock-valuation");
    }

    // -----------------------------------------------------------------------------------------
    // Understock / overstock
    // -----------------------------------------------------------------------------------------

    [HttpGet("stock-position")]
    public async Task<IActionResult> StockPosition(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null,
        [FromQuery] StockPosition? only = null)
        => Ok(await _sender.Send(new GetStockPositionQuery(locationId, departmentId, only)));

    [HttpGet("stock-position/export")]
    public async Task<IActionResult> ExportStockPosition(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? departmentId = null,
        [FromQuery] StockPosition? only = null)
    {
        var csv = await _sender.Send(new ExportStockPositionQuery(
            new GetStockPositionQuery(locationId, departmentId, only)));

        return Csv(csv, "stock-position");
    }

    // -----------------------------------------------------------------------------------------
    // On order
    // -----------------------------------------------------------------------------------------

    [HttpGet("on-order")]
    public async Task<IActionResult> OnOrder(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? supplierId = null,
        [FromQuery] long? departmentId = null)
        => Ok(await _sender.Send(new GetOnOrderQuery(locationId, supplierId, departmentId)));

    [HttpGet("on-order/export")]
    public async Task<IActionResult> ExportOnOrder(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] long? supplierId = null,
        [FromQuery] long? departmentId = null)
    {
        var csv = await _sender.Send(new ExportOnOrderQuery(new GetOnOrderQuery(locationId, supplierId, departmentId)));
        return Csv(csv, "on-order");
    }

    // -----------------------------------------------------------------------------------------
    // Stock received
    // -----------------------------------------------------------------------------------------

    [HttpGet("stock-received")]
    public async Task<IActionResult> StockReceived(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? supplierId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200)
        => Ok(await _sender.Send(new GetStockReceivedQuery(locationId, from, to, supplierId, skip, take)));

    [HttpGet("stock-received/export")]
    public async Task<IActionResult> ExportStockReceived(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? supplierId = null)
    {
        var csv = await _sender.Send(new ExportStockReceivedQuery(
            new GetStockReceivedQuery(locationId, from, to, supplierId, 0, int.MaxValue)));

        return Csv(csv, "stock-received");
    }

    // -----------------------------------------------------------------------------------------
    // Reward points
    // -----------------------------------------------------------------------------------------

    [HttpGet("reward-points")]
    public async Task<IActionResult> RewardPoints(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? customerId = null)
        => Ok(await _sender.Send(new GetRewardPointsActivityQuery(locationId, from, to, customerId)));

    [HttpGet("reward-points/export")]
    public async Task<IActionResult> ExportRewardPoints(
        [FromQuery][BindRequired] long locationId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? customerId = null)
    {
        var csv = await _sender.Send(new ExportRewardPointsActivityQuery(
            new GetRewardPointsActivityQuery(locationId, from, to, customerId)));

        return Csv(csv, "reward-points");
    }

    private FileContentResult Csv(string content, string name)
        => File(Encoding.UTF8.GetBytes(content), "text/csv", $"{name}.csv");
}
