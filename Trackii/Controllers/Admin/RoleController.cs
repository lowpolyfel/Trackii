using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Role;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Role")]
public class RoleController : Controller
{
    private readonly RoleService _svc;

    public RoleController(RoleService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? search, bool showInactive = false, int page = 1)
    {
        var vm = _svc.GetPaged(search, showInactive, page, 10);
        return View(vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new RoleEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(RoleEditVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // VALIDACIÓN
        if (_svc.Exists(vm.Name))
        {
            ModelState.AddModelError("Name", "Este rol ya existe.");
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
    public IActionResult Edit(uint id, RoleEditVm vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) return View(vm);

        // VALIDACIÓN DUPLICADO
        if (_svc.Exists(vm.Name, id))
        {
            ModelState.AddModelError("Name", "Este rol ya existe.");
            return View(vm);
        }

        // Validación de uso (la que ya tenías)
        if (!_svc.Update(vm))
            TempData["Error"] = "No se puede editar un Role en uso.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id)
    {
        if (!_svc.Toggle(id))
            TempData["Error"] = "No se puede desactivar: hay usuarios activos usando este Role.";

        return RedirectToAction(nameof(Index));
    }
}
