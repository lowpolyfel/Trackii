using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Admin;
using Trackii.Models.Admin.User;

namespace Trackii.Controllers.Admin;

[Authorize]
[Route("Admin/User")]
public class UserController : Controller
{
    private readonly UserService _svc;

    public UserController(UserService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? search, int page = 1)
    {
        var vm = _svc.GetPaged(search, page, 10);
        return View(vm);
    }

    [HttpGet("Edit/{id}")]
    public IActionResult Edit(uint id)
    {
        var vm = _svc.GetById(id);
        if (vm == null) return NotFound();
        return View(vm);
    }

    [HttpPost("Edit/{id}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(uint id, UserEditVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        _svc.Update(vm);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Toggle/{id}")]
    public IActionResult Toggle(uint id)
    {
        _svc.Toggle(id);
        return RedirectToAction(nameof(Index));
    }
}
