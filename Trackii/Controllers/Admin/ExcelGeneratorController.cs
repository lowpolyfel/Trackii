using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/ExcelGenerator")]
public class ExcelGeneratorController : Controller
{
    private readonly ExcelGeneratorService _service;
    private const string ViewBase = "~/Views/Management/ExcelGenerator/";

    public ExcelGeneratorController(ExcelGeneratorService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var vm = _service.GetLandingSummary();
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("RoutesBySubfamily")]
    public IActionResult RoutesBySubfamily()
    {
        var vm = _service.GetRoutesBySubfamilyPreview();
        return View($"{ViewBase}RoutesBySubfamily.cshtml", vm);
    }

    [HttpPost("RoutesBySubfamily/Export")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportRoutesBySubfamily()
    {
        var fileBytes = _service.BuildRoutesBySubfamilyExcelFile();
        var fileName = $"Rutas_Subfamilia_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpGet("ActiveOrders")]
    public IActionResult ActiveOrders([FromQuery] string? sort = "oldest")
    {
        var vm = _service.GetActiveOrdersPreview(sort ?? "oldest");
        return View($"{ViewBase}ActiveOrders.cshtml", vm);
    }

    [HttpPost("ActiveOrders/Export")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportActiveOrders([FromForm] string? sort = "oldest")
    {
        var fileBytes = _service.BuildActiveOrdersExcelFile(sort ?? "oldest");
        var fileName = $"Ordenes_Activas_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
