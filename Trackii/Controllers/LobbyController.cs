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

    [Authorize(Roles = "Admin,Engineering,Ingenieria")]
    public IActionResult EngineeringOrders(string? search, string? status, uint? familyId, uint? subfamilyId, uint? focusSubfamilyId, uint? locationId, uint? routeId)
    {
        var vm = _svc.GetEngineeringActiveOrders(search, status, familyId, subfamilyId, focusSubfamilyId, locationId, routeId);
        return View("EngineeringOrders", vm);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateAdminDeviceLocation(uint deviceId, uint locationId)
    {
        var updated = _svc.UpdateDeviceLocationFromAdminDashboard(deviceId, locationId);
        TempData[updated ? "ToastSuccess" : "ToastError"] = updated
            ? "Localidad del dispositivo actualizada correctamente."
            : "No se pudo actualizar la localidad del dispositivo.";
        return RedirectToAction(nameof(Index));
    }
}
