using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Product;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Product")]
public class ProductController : Controller
{
    private readonly ProductService _svc;

    public ProductController(ProductService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(
        uint? areaId,
        uint? familyId,
        uint? subfamilyId,
        string? search,
        bool? showInactive,
        int page = 1)
    {
        var vm = _svc.GetPaged(
            areaId,
            familyId,
            subfamilyId,
            search,
            showInactive ?? false,
            page,
            10);

        return View(vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
        return View(new ProductEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(ProductEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (Ya existente)
        if (_svc.Exists(vm.PartNumber))
        {
            ModelState.AddModelError("PartNumber", "Este número de parte ya existe.");
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }

        // 2. INTENTO DE CREACIÓN (Con validación de Subfamilia Activa)
        try
        {
            _svc.Create(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message); // "La Subfamilia seleccionada está inactiva"
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }
    }

    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();

        ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
        return View(vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, ProductEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (Ya existente)
        if (_svc.Exists(vm.PartNumber, id))
        {
            ModelState.AddModelError("PartNumber", "Este número de parte ya existe.");
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }

        // 2. INTENTO DE ACTUALIZACIÓN (Con validación de Subfamilia Activa)
        try
        {
            _svc.Update(vm);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Subfamilies = _svc.GetActiveSubfamilies();
            return View(vm);
        }
    }

    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        if (!_svc.SetActive(id, active == 1))
            TempData["Error"] = "No se puede cambiar el estado del Product por reglas de negocio.";

        return RedirectToAction(nameof(Index));
    }
}
