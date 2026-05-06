using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Engineering;

namespace Trackii.Controllers.Engineering;

[Authorize(Roles = "Admin,Engineering,Ingenieria")]
[Route("Engineering/OrderTools")]
public class OrderToolsController : Controller
{
    private readonly OrderToolsService _svc;
    private const string ViewBase = "~/Views/Engineering/OrderTools/";

    public OrderToolsController(OrderToolsService svc)
    {
        _svc = svc;
    }

    [HttpGet("Activate")]
    public IActionResult Activate([FromQuery] string? search)
    {
        return View($"{ViewBase}Activate.cshtml", _svc.GetOrdersForActivation(search));
    }

    [HttpPost("Activate")]
    [ValidateAntiForgeryToken]
    public IActionResult ActivateOrder([FromForm] uint workOrderId, [FromForm] string? search)
    {
        var ok = _svc.ReactivateOrder(workOrderId);
        TempData[ok ? "Ok" : "Error"] = ok
            ? "Orden activada: status=IN_PROGRESS y active=1."
            : "No se pudo activar la orden seleccionada.";
        return RedirectToAction(nameof(Activate), new { search });
    }

    [HttpGet("EditPieces")]
    public IActionResult EditPieces([FromQuery] string? search, [FromQuery] string? selectedWo)
    {
        return View($"{ViewBase}EditPieces.cshtml", _svc.GetOrderPiecesEditor(search, selectedWo));
    }

    [HttpPost("EditPieces")]
    [ValidateAntiForgeryToken]
    public IActionResult SavePieces([FromForm] uint workOrderId, [FromForm] uint routeStepId, [FromForm] int qtyIn, [FromForm] bool cascade, [FromForm] string? search, [FromForm] string? selectedWo)
    {
        var result = _svc.UpdateStepQuantity(workOrderId, routeStepId, qtyIn, cascade);
        TempData[result.ok ? "Ok" : "Error"] = result.message;
        return RedirectToAction(nameof(EditPieces), new { search, selectedWo });
    }
}
