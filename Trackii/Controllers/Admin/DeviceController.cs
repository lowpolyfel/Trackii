using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.Device;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Device")]
public class DeviceController : Controller
{
    private readonly DeviceService _svc;
    private const string ViewBase = "~/Views/Management/Device/";

    public DeviceController(DeviceService svc)
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
        ViewBag.Locations = _svc.GetActiveLocations();
        return View($"{ViewBase}Create.cshtml", new DeviceEditVm());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(DeviceEditVm vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Locations = _svc.GetActiveLocations();
            return View($"{ViewBase}Create.cshtml", vm);
        }

        if (_svc.ExistsDeviceUid(vm.DeviceUid))
        {
            ModelState.AddModelError("DeviceUid", "Este UID ya existe.");
            ViewBag.Locations = _svc.GetActiveLocations();
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
        ViewBag.Locations = _svc.GetActiveLocations();
        return View($"{ViewBase}Edit.cshtml", vm);
    }

    [HttpPost("Edit/{id:long}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, DeviceEditVm vm)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            ViewBag.Locations = _svc.GetActiveLocations();
            return View($"{ViewBase}Edit.cshtml", vm);
        }

        if (_svc.ExistsDeviceUid(vm.DeviceUid, id))
        {
            ModelState.AddModelError("DeviceUid", "Este UID ya existe.");
            ViewBag.Locations = _svc.GetActiveLocations();
            return View($"{ViewBase}Edit.cshtml", vm);
        }

        _svc.Update(vm);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Toggle")]
    [ValidateAntiForgeryToken]
    public IActionResult Toggle(uint id, int active)
    {
        _svc.SetActive(id, active == 1);
        return RedirectToAction(nameof(Index));
    }
}
