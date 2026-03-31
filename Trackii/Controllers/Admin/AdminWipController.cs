using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Wip;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin,Engineering,Ingenieria")]
[Route("Admin/Wip")]
public class AdminWipController : Controller
{
    private readonly AdminWipService _service;
    private const string ViewPath = "~/Views/Management/Wip/ManageWip.cshtml";

    public AdminWipController(AdminWipService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View(ViewPath, _service.GetInitialVm());
    }

    [HttpPost("CargarOrden")]
    [ValidateAntiForgeryToken]
    public IActionResult CargarOrden(AdminWipManagerVm model)
    {
        try
        {
            var loaded = _service.LoadOrder(model.WoNumber, model.PartNumber);
            TempData["Success"] = loaded.IsNewOrder
                ? "Orden no encontrada. Se preparó como nueva para captura."
                : "Orden cargada correctamente.";
            return View(ViewPath, loaded);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.ErrorCodes = _service.GetInitialVm().ErrorCodes;
            return View(ViewPath, model);
        }
    }

    [HttpPost("GuardarProgreso")]
    [ValidateAntiForgeryToken]
    public IActionResult GuardarProgreso(AdminWipManagerVm model)
    {
        if (string.IsNullOrWhiteSpace(model.WoNumber) || string.IsNullOrWhiteSpace(model.PartNumber))
        {
            ModelState.AddModelError(string.Empty, "WO Number y Part Number son requeridos.");
            return View(ViewPath, model);
        }

        try
        {
            var username = User.FindFirstValue(ClaimTypes.Name)
                           ?? throw new Exception("No se pudo obtener el usuario de sesión.");

            _service.SaveProgress(model, username);
            TempData["Success"] = "Progreso guardado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var loaded = _service.LoadOrder(model.WoNumber, model.PartNumber);
            return View(ViewPath, loaded);
        }
    }
}
