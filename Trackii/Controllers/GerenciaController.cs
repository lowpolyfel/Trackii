using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services;

namespace Trackii.Controllers;

[Authorize(Roles = "Admin,Gerencia")]
[Route("Gerencia")]
public class GerenciaController : Controller
{
    private readonly GerenciaService _svc;
    private const string ViewBase = "~/Views/Gerencia/";

    public GerenciaController(GerenciaService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var vm = _svc.GetDashboard();
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("Produccion")]
    public IActionResult Production()
    {
        var vm = _svc.GetProduction();
        return View($"{ViewBase}Production.cshtml", vm);
    }

    [HttpGet("Ordenes")]
    public IActionResult WorkOrders()
    {
        var vm = _svc.GetWorkOrders();
        return View($"{ViewBase}WorkOrders.cshtml", vm);
    }

    [HttpGet("Wip")]
    public IActionResult Wip()
    {
        var vm = _svc.GetWipOverview();
        return View($"{ViewBase}Wip.cshtml", vm);
    }

    [HttpGet("ScanEvents")]
    public IActionResult ScanEvents()
    {
        var vm = _svc.GetScanEvents();
        return View($"{ViewBase}ScanEvents.cshtml", vm);
    }
}
