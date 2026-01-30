using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Location;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Location")]
public class LocationController : Controller
{
    private readonly LocationService _svc;

    public LocationController(LocationService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(
        string? search,
        bool? showInactive,
        int page = 1)
    {
        var vm = _svc.GetPaged(
            search,
            showInactive ?? false,
            page,
            10);

        return View(vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new LocationEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(LocationEditVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        // VALIDACIÓN
        if (_svc.Exists(vm.Name))
        {
            ModelState.AddModelError("Name", "Esta ubicación ya existe.");
            return View(vm);
        }

        _svc.Create(vm);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();

        return View(vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, LocationEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
            return View(vm);

        // VALIDACIÓN
        if (_svc.Exists(vm.Name, id))
        {
            ModelState.AddModelError("Name", "Esta ubicación ya existe.");
            return View(vm);
        }

        _svc.Update(vm);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        if (!_svc.SetActive(id, active == 1))
            TempData["Error"] = "No se puede desactivar la Location porque está en uso.";

        return RedirectToAction(nameof(Index));
    }
}
