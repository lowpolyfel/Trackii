using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/HardMod")]
public class HardModController : Controller
{
    private readonly HardModService _svc;
    private const string ViewBase = "~/Views/Management/HardMod/";

    public HardModController(HardModService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? search)
    {
        var vm = _svc.GetViewModel(search);
        vm.SuccessMessage = TempData["Success"] as string;
        vm.ErrorMessage = TempData["Error"] as string;
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpPost("DeleteByWorkOrder")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteByWorkOrder(uint workOrderId)
    {
        var result = _svc.HardDeleteByWorkOrder(workOrderId);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
