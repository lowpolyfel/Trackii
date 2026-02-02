using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Route;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Route")]
public class RouteController : Controller
{
    private readonly RouteService _service;
    private const string ViewBase = "~/Views/Management/Route/";

    public RouteController(RouteService service)
    {
        _service = service;
    }

    private void LoadLookups(uint? selectedSubfamilyId = null)
    {
        ViewBag.Subfamilies = _service.GetActiveSubfamilies();
        ViewBag.Locations = _service.GetActiveLocations();
        ViewBag.SelectedSubfamilyId = selectedSubfamilyId;
    }

    [HttpGet("")]
    public IActionResult Index(uint? subfamilyId, string? search, bool showInactive = false, int page = 1)
    {
        LoadLookups(subfamilyId);
        var vm = _service.GetPaged(subfamilyId, search, showInactive, page, 10);
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        LoadLookups(null);
        return View($"{ViewBase}Create.cshtml", _service.GetForCreate());
    }

    [HttpGet("Edit/{id}")]
    public IActionResult Edit(uint id)
    {
        var vm = _service.GetForEdit(id);
        LoadLookups(vm.SubfamilyId);
        return View($"{ViewBase}Edit.cshtml", vm);
    }

    [HttpGet("Ver/{id}")]
    public IActionResult Ver(uint id)
    {
        var vm = _service.GetForView(id);
        return View($"{ViewBase}Ver.cshtml", vm);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public IActionResult Save(RouteEditVm vm)
    {
        try
        {
            // Validaciones manuales básicas si ModelState falla por listas dinámicas
            if (vm.SubfamilyId == 0) ModelState.AddModelError("SubfamilyId", "Requerido");

            _service.Save(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            // Error de negocio (ej: WIP activo)
            ModelState.AddModelError("", ex.Message);
            LoadLookups(vm.SubfamilyId);
            var viewName = vm.Id == 0 ? "Create" : "Edit";
            return View($"{ViewBase}{viewName}.cshtml", vm);
        }
    }

    [HttpPost("Activate/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Activate(uint id)
    {
        try
        {
            _service.Activate(id);
            TempData["Success"] = "Ruta activada correctamente.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Deactivate/{id}")]
    public IActionResult Deactivate(uint id)
    {
        try
        {
            var vm = _service.GetDeactivateVm(id);
            return View($"{ViewBase}Deactivate.cshtml", vm);
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("Deactivate/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Deactivate(uint id, RouteDeactivateVm vm)
    {
        if (id != vm.RouteId)
        {
            TempData["Error"] = "La ruta seleccionada no coincide.";
            return RedirectToAction(nameof(Index));
        }

        if (!vm.ReplacementRouteId.HasValue)
        {
            ModelState.AddModelError("ReplacementRouteId", "Selecciona la ruta que quedará activa.");
            var reload = _service.GetDeactivateVm(id);
            reload.ReplacementRouteId = vm.ReplacementRouteId;
            return View($"{ViewBase}Deactivate.cshtml", reload);
        }

        try
        {
            _service.DeactivateAndActivate(id, vm.ReplacementRouteId.Value);
            TempData["Success"] = "Ruta desactivada. La nueva ruta quedó activa.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            var reload = _service.GetDeactivateVm(id);
            reload.ReplacementRouteId = vm.ReplacementRouteId;
            return View($"{ViewBase}Deactivate.cshtml", reload);
        }
    }
}
