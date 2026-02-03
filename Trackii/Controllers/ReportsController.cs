using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Reports;

namespace Trackii.Controllers;

[Authorize(Roles = "Admin,Engineering,Ingenieria,Gerencia")]
[Route("Reportes")]
public class ReportsController : Controller
{
    private readonly ReportsService _svc;
    private const string ViewBase = "~/Views/Reports/";

    public ReportsController(ReportsService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View($"{ViewBase}Index.cshtml");
    }

    [HttpGet("WorkOrders")]
    public IActionResult WorkOrders(string? search, string? status, int page = 1)
    {
        var vm = _svc.GetWorkOrders(search, status, page, 10);
        return View($"{ViewBase}WorkOrders.cshtml", vm);
    }

    [HttpGet("Products")]
    public IActionResult Products(string? search, bool showInactive = false, int page = 1)
    {
        var vm = _svc.GetProducts(search, showInactive, page, 10);
        return View($"{ViewBase}Products.cshtml", vm);
    }

    [HttpGet("Rework")]
    public IActionResult Rework(string? search, DateTime? from, DateTime? to, int page = 1)
    {
        var vm = _svc.GetRework(search, from, to, page, 10);
        return View($"{ViewBase}Rework.cshtml", vm);
    }

    [HttpGet("ProductsByArea")]
    public IActionResult ProductsByArea(int page = 1)
    {
        var vm = _svc.GetProductsByArea(page, 10);
        return View($"{ViewBase}ProductsByArea.cshtml", vm);
    }

    [HttpGet("Wip")]
    public IActionResult Wip(string? status, int page = 1)
    {
        var vm = _svc.GetWipItems(status, page, 10);
        return View($"{ViewBase}Wip.cshtml", vm);
    }

    [HttpGet("Devices")]
    public IActionResult Devices(string? search, bool onlyActive = true, int page = 1)
    {
        var vm = _svc.GetDevices(search, onlyActive, page, 10);
        return View($"{ViewBase}Devices.cshtml", vm);
    }

    [HttpGet("Users")]
    public IActionResult Users(string? search, bool onlyActive = true, int page = 1)
    {
        var vm = _svc.GetUsers(search, onlyActive, page, 10);
        return View($"{ViewBase}Users.cshtml", vm);
    }
}
