using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services;

namespace Trackii.Controllers;

[Authorize(Roles = "Admin,Engineering,Ingenieria")]
public class LobbyController : Controller
{
    private readonly LobbyService _svc;

    public LobbyController(LobbyService svc)
    {
        _svc = svc;
    }

    public IActionResult Index()
    {
        if (User.IsInRole("Admin"))
        {
            var adminVm = _svc.GetAdminDashboard();
            return View("Admin", adminVm);
        }

        if (User.IsInRole("Engineering") || User.IsInRole("Ingenieria"))
        {
            var engineeringVm = _svc.GetEngineeringDashboard();
            return View("Engineering", engineeringVm);
        }

        var vm = _svc.GetDashboard();
        return View(vm);
    }
}
