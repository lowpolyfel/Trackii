using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Subfamily;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize]
[Route("Admin/[controller]")]
public class SubfamilyController : Controller
{
    private readonly SubfamilyService _svc;

    public SubfamilyController(SubfamilyService svc)
    {
        _svc = svc;
    }

    // ===================== INDEX =====================
    [HttpGet("")]
    public IActionResult Index(
        uint? areaId,
        uint? familyId,
        string? search,
        bool showInactive = false,
        int page = 1)
    {
        var vm = _svc.GetPaged(areaId, familyId, search, showInactive, page, 10);
        return View(vm);
    }

    // ===================== CREATE =====================
    [HttpGet("Create")]
    public IActionResult Create()
    {
        ViewBag.Families = _svc.GetActiveFamilies();
        return View(new SubfamilyEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(SubfamilyEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (La que ya tenías)
        if (_svc.Exists(vm.Name))
        {
            ModelState.AddModelError("Name", "Este nombre de Subfamilia ya existe.");
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }

        // 2. INTENTO DE CREACIÓN (Con validación de Familia Activa)
        try
        {
            _svc.Create(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message); // "La Familia seleccionada está inactiva"
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }
    }

    // ===================== EDIT =====================
    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();

        ViewBag.Families = _svc.GetActiveFamilies();
        return View(vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(SubfamilyEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (La que ya tenías)
        if (_svc.Exists(vm.Name, vm.Id))
        {
            ModelState.AddModelError("Name", "Este nombre de Subfamilia ya existe.");
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }

        // 2. INTENTO DE ACTUALIZACIÓN (Con validación de Familia Activa)
        try
        {
            _svc.Update(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Families = _svc.GetActiveFamilies();
            return View(vm);
        }
    }
    // ===================== TOGGLE =====================
    [HttpPost("Toggle/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        var ok = _svc.SetActive(id, active == 1); // el service valida padre activo
        if (!ok)
        {
            TempData["Error"] = "No se puede activar la Subfamilia porque la Family está inactiva.";
        }

        return RedirectToAction(nameof(Index));
    }
}
