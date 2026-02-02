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
    private const string ViewBase = "~/Views/Management/Product/";

    public ProductController(ProductService svc)
    {
        _svc = svc;
    }

    private void LoadLookups(ProductEditVm vm)
    {
        if (vm.SubfamilyId != 0 && (!vm.AreaId.HasValue || !vm.FamilyId.HasValue))
        {
            var parents = _svc.GetParentsForSubfamily(vm.SubfamilyId);
            if (parents.HasValue)
            {
                vm.AreaId ??= parents.Value.AreaId;
                vm.FamilyId ??= parents.Value.FamilyId;
            }
        }

        ViewBag.Areas = _svc.GetActiveAreas();
        ViewBag.Families = _svc.GetActiveFamiliesWithArea();
        ViewBag.Subfamilies = _svc.GetActiveSubfamiliesWithFamily();
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

        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        var vm = new ProductEditVm();
        LoadLookups(vm);
        return View($"{ViewBase}Create.cshtml", vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(ProductEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            LoadLookups(vm);
            return View($"{ViewBase}Create.cshtml", vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (Ya existente)
        if (_svc.Exists(vm.PartNumber))
        {
            ModelState.AddModelError("PartNumber", "Este número de parte ya existe.");
            LoadLookups(vm);
            return View($"{ViewBase}Create.cshtml", vm);
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
            LoadLookups(vm);
            return View($"{ViewBase}Create.cshtml", vm);
        }
    }

    [HttpGet("Edit/{id:long}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();

        LoadLookups(vm);
        return View($"{ViewBase}Edit.cshtml", vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, ProductEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            LoadLookups(vm);
            return View($"{ViewBase}Edit.cshtml", vm);
        }

        // 1. VALIDACIÓN DUPLICADOS (Ya existente)
        if (_svc.Exists(vm.PartNumber, id))
        {
            ModelState.AddModelError("PartNumber", "Este número de parte ya existe.");
            LoadLookups(vm);
            return View($"{ViewBase}Edit.cshtml", vm);
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
            LoadLookups(vm);
            return View($"{ViewBase}Edit.cshtml", vm);
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
