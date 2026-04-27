using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Trackii.Services;
using Trackii.Services.Gerencia.RealInventory;

namespace Trackii.Controllers;

[Authorize(Roles = "Admin,Gerencia")]
[Route("Gerencia")]
public class GerenciaController : Controller
{
    private readonly GerenciaService _svc;
    private readonly RealInventoryMapService _realInventoryMapService;
    private readonly RealInventoryDaysMapService _realInventoryDaysMapService;
    private readonly RealInventoryOrderSearchService _realInventoryOrderSearchService;
    private const string ViewBase = "~/Views/Gerencia/";

    public GerenciaController(
        GerenciaService svc,
        RealInventoryMapService realInventoryMapService,
        RealInventoryDaysMapService realInventoryDaysMapService,
        RealInventoryOrderSearchService realInventoryOrderSearchService)
    {
        _svc = svc;
        _realInventoryMapService = realInventoryMapService;
        _realInventoryDaysMapService = realInventoryDaysMapService;
        _realInventoryOrderSearchService = realInventoryOrderSearchService;
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
        var vm = _realInventoryMapService.GetMap();
        var daysVm = _realInventoryDaysMapService.BuildDaysMap(vm);
        ViewBag.DaysMap = daysVm;
        return View($"{ViewBase}InventarioReal.cshtml", vm);
    }

    [HttpPost("SendInventoryExcel")]
    [Authorize]
    public async Task<IActionResult> SendInventoryExcel(
        [FromServices] EmailService emailService,
        [FromServices] IConfiguration cfg,
        [FromServices] RealInventoryDiscreteExcelService excelService)
    {
        try
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { message = "Usuario no autenticado correctamente." });
            }

            string? userEmail = null;
            var connString = cfg.GetConnectionString("TrackiiDb");
            if (string.IsNullOrWhiteSpace(connString))
            {
                return StatusCode(500, new { message = "No se encontró la cadena de conexión TrackiiDb." });
            }

            await using (var cn = new MySqlConnection(connString))
            {
                await cn.OpenAsync();
                await using var cmd = new MySqlCommand("SELECT email FROM `user` WHERE username = @username LIMIT 1", cn);
                cmd.Parameters.AddWithValue("@username", username);
                var result = await cmd.ExecuteScalarAsync();
                if (result is not null && result != DBNull.Value)
                {
                    userEmail = result.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return NotFound(new { message = "El usuario actual no tiene un correo configurado en la base de datos." });
            }

            var excelBytes = excelService.BuildExcel();
            var fileName = $"InventarioDiscretos_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            var subject = "Reporte de inventario de discretos - Trackii";
            var body = $@"
                <h2>Reporte de inventario de discretos</h2>
                <p>Se adjunta el archivo Excel con las hojas de resumen por familias e inventario detallado.</p>
                <p>Usuario que solicitó el envío: <strong>{username}</strong></p>
                <p>Generado el: <strong>{DateTime.Now:yyyy-MM-dd HH:mm}</strong></p>";

            await emailService.SendEmailWithAttachmentAsync(userEmail, subject, body, excelBytes, fileName);
            return Ok(new { message = $"Reporte enviado exitosamente a {userEmail}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error interno al enviar el reporte: {ex.Message}" });
        }
    }

    [HttpGet("InventarioRealDetalle")]
    public IActionResult InventarioRealDetalle(string location, string familyGroup)
    {
        var vm = _realInventoryMapService.GetCellDetail(location, familyGroup);
        return View($"{ViewBase}InventarioRealDetalle.cshtml", vm);
    }

    [HttpGet("InventarioRealWoDetalle")]
    public IActionResult InventarioRealWoDetalle(string woNumber, string? location, string? familyGroup)
    {
        var vm = _realInventoryMapService.GetWorkOrderDetail(woNumber, location, familyGroup);
        return View($"{ViewBase}InventarioRealWoDetalle.cshtml", vm);
    }

    [HttpGet("BuscadorOrdenes")]
    public IActionResult OrderSearch(string? woNumber, string? product, int page = 1)
    {
        var vm = _realInventoryOrderSearchService.Search(woNumber, product, page);
        return View($"{ViewBase}OrderSearch.cshtml", vm);
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
