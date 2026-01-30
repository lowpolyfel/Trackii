using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Family;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Family")]
public class FamilyController : Controller
{
    private readonly FamilyService _svc;

    public FamilyController(FamilyService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(uint? areaId, string? search, bool showInactive = false, int page = 1)
    {
        var vm = _svc.GetPaged(areaId, search, showInactive, page, 10);
        return View(vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        ViewBag.Areas = _svc.GetActiveAreas();
        return View(new FamilyEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(FamilyEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (La que ya tenías)
        if (_svc.Exists(vm.Name))
        {
            ModelState.AddModelError("Name", "Este nombre de Familia ya existe.");
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }

        // 2. INTENTO DE CREACIÓN (Con validación de Área Activa)
        try
        {
            _svc.Create(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            // Captura si el Área está inactiva y muestra el error
            ModelState.AddModelError("", ex.Message);
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }
    }

    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();

        ViewBag.Areas = _svc.GetActiveAreas();
        return View(vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, FamilyEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (La que ya tenías)
        if (_svc.Exists(vm.Name, id))
        {
            ModelState.AddModelError("Name", "Este nombre de Familia ya existe.");
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }

        // 2. INTENTO DE ACTUALIZACIÓN (Con validación de Área Activa)
        try
        {
            _svc.Update(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Areas = _svc.GetActiveAreas();
            return View(vm);
        }
    }
    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        var ok = _svc.SetActive(id, active == 1);

        if (!ok)
        {
            TempData["Error"] = "No se puede activar la Family porque el Área está inactiva.";
        }

        return RedirectToAction(nameof(Index));
    }


}
