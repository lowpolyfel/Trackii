
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Area;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Area")]
public class AreaController : Controller
{
    private readonly AreaService _svc;
    private const string ViewBase = "~/Views/Management/Area/";

    public AreaController(AreaService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? search, bool showInactive = false, int page = 1)
    {
        var vm = _svc.GetPaged(search, showInactive, page, 10);
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View($"{ViewBase}Create.cshtml", new AreaEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(AreaEditVm vm)
    {
        if (!ModelState.IsValid)
            return View($"{ViewBase}Create.cshtml", vm);

        // Validación de duplicados
        if (_svc.Exists(vm.Name))
        {
            ModelState.AddModelError("Name", "Este nombre de Área ya existe.");
            return View($"{ViewBase}Create.cshtml", vm);
        }

        _svc.Create(vm);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();
        return View($"{ViewBase}Edit.cshtml", vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, AreaEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
            return View($"{ViewBase}Edit.cshtml", vm);

        // Validación de duplicados
        if (_svc.Exists(vm.Name, id))
        {
            ModelState.AddModelError("Name", "Este nombre de Área ya existe.");
            return View($"{ViewBase}Edit.cshtml", vm);
        }

        _svc.Update(vm);
        return RedirectToAction(nameof(Index));
    }

    // CORREGIDO: Ahora coincide con el formulario de tu Index.cshtml
    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        bool isActive = (active == 1);
        _svc.SetActive(id, isActive);
        return RedirectToAction(nameof(Index));
    }
}
