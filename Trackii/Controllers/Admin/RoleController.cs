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
    public IActionResult Index()
    {
        return View(new RoleListVm { Items = _svc.GetAll() });
    }

    [HttpGet("Create")]
    public IActionResult Create() => View(new RoleEditVm());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(RoleEditVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

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

        if (!ModelState.IsValid)
            return View(vm);

        if (!_svc.Update(vm))
            TempData["Error"] = "No se puede editar un Role en uso.";

        return RedirectToAction(nameof(Index));
    }
}
