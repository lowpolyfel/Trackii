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
        return RedirectToAction(nameof(InventarioReal));
    }

    [HttpGet("Lobby")]
    public IActionResult Lobby()
    {
        return RedirectToAction(nameof(InventarioReal));
    }

    [HttpGet("InventarioReal")]
    public IActionResult InventarioReal()
    {
        var vm = _svc.GetRealInventoryLobby();
        return View($"{ViewBase}InventarioReal.cshtml", vm);
    }

    [HttpGet("MapaDiscretos")]
    public IActionResult DiscreteMap(string? periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate, string? sortBy, string? metricView, string? selectedSubfamily)
    {
        var vm = _svc.GetDiscreteMap(periodType, weekValue, monthValue, fromDate, toDate, sortBy, metricView, selectedSubfamily);
        return View($"{ViewBase}DiscreteMap.cshtml", vm);
    }

    [HttpGet("LobbyGerencia")]
    public IActionResult BackendLobby(string? mode)
    {
        var vm = _svc.GetBackendLobby(mode);
        return View($"{ViewBase}BackendLobby.cshtml", vm);
    }

    [HttpGet("LobbyInventarioDetalle")]
    public IActionResult BackendLobbyInventoryDetail(string location, string familyGroup, string? mode)
    {
        var vm = _svc.GetBackendLobbyCellDetail(location, familyGroup, mode);
        return View($"{ViewBase}BackendLobbyInventoryDetail.cshtml", vm);
    }

    [HttpGet("DiaDiscretos")]
    public IActionResult DiscreteDay(DateTime day, string? sortBy)
    {
        var vm = _svc.GetDiscreteDayDetail(day, sortBy);
        return View($"{ViewBase}DiscreteDay.cshtml", vm);
    }

    [HttpGet("DetalleMapaDiscretos")]
    public IActionResult DiscreteMapDetail(string location, string subfamily, string? periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate, DateTime? day)
    {
        var vm = _svc.GetDiscreteMapCellDetail(location, subfamily, periodType, weekValue, monthValue, fromDate, toDate, day);
        return View($"{ViewBase}DiscreteMapDetail.cshtml", vm);
    }

    [HttpGet("PanelesDiariosDiscretos")]
    public IActionResult DiscreteDailyPanels()
    {
        var vm = _svc.GetDiscreteDailyPanels();
        return View($"{ViewBase}DiscreteDailyPanels.cshtml", vm);
    }

    [HttpGet("CausasScrap")]
    public IActionResult ScrapCauses(DateTime? day, string? woNumber, string? product)
    {
        var vm = _svc.GetScrapCauses(day, woNumber, product);
        return View($"{ViewBase}ScrapCauses.cshtml", vm);
    }

    [HttpGet("OrdenesActivas")]
    public IActionResult ActiveOrders(string? location, string? subfamily)
    {
        var vm = _svc.GetActiveOrdersDetail();
        ViewBag.SelectedLocation = location;
        ViewBag.SelectedSubfamily = subfamily;
        return View($"{ViewBase}ActiveOrders.cshtml", vm);
    }

    [HttpGet("CausasErrores")]
    public IActionResult ErrorCauses()
    {
        var vm = _svc.GetErrorCauses();
        return View($"{ViewBase}ErrorCauses.cshtml", vm);
    }

    [HttpGet("TendenciaDiaria")]
    public IActionResult DailyTrend()
    {
        var vm = _svc.GetDailyTrend();
        return View($"{ViewBase}DailyTrend.cshtml", vm);
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

    [HttpGet("Throughput")]
    public IActionResult Throughput()
    {
        var vm = _svc.GetThroughput();
        return View($"{ViewBase}Throughput.cshtml", vm);
    }

    [HttpGet("SalidaSemanal")]
    public IActionResult WeeklyOutput(string? periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate)
    {
        var vm = _svc.GetWeeklyOutput(periodType, weekValue, monthValue, fromDate, toDate);
        return View($"{ViewBase}WeeklyOutput.cshtml", vm);
    }

    [HttpGet("ReworkSummary")]
    public IActionResult ReworkSummary()
    {
        var vm = _svc.GetReworkSummary();
        return View($"{ViewBase}ReworkSummary.cshtml", vm);
    }

    [HttpGet("WoHealth")]
    public IActionResult WoHealth()
    {
        var vm = _svc.GetWoHealth();
        return View($"{ViewBase}WoHealth.cshtml", vm);
    }
}
