using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/WorkOrderReactivation")]
public class WorkOrderReactivationController : Controller
{
    private readonly WorkOrderReactivationService _service;
    private const string ViewBase = "~/Views/Management/WorkOrderReactivation/";

    public WorkOrderReactivationController(WorkOrderReactivationService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public IActionResult Index([FromQuery] string? search)
    {
        var vm = _service.GetInactiveOrders(search);
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpPost("Reactivate")]
    [ValidateAntiForgeryToken]
    public IActionResult Reactivate([FromForm] uint workOrderId, [FromForm] string? search)
    {
        var updated = _service.Reactivate(workOrderId);
        TempData[updated ? "Ok" : "Error"] = updated
            ? "Orden reactivada correctamente (active=1, status=IN_PROGRESS)."
            : "No se pudo reactivar la orden seleccionada.";

        return RedirectToAction(nameof(Index), new { search });
    }
}
