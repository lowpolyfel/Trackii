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
        var vm = _service.GetPreview();
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpPost("ExportRoutes")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportRoutes()
    {
        var fileBytes = _service.BuildRoutesExcelFile();
        var fileName = $"Rutas_Subfamilia_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost("ExportStaleOrders")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportStaleOrders()
    {
        var fileBytes = _service.BuildStaleOrdersExcelFile();
        var fileName = $"Ordenes_Sin_Actualizar_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
