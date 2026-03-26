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
    public IActionResult Index(string? wipItemLookup, uint? wipStepExecutionId)
    {
        var vm = _svc.GetViewModel(wipItemLookup, wipStepExecutionId);
        vm.SuccessMessage = TempData["Success"] as string;
        vm.ErrorMessage = TempData["Error"] as string;
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpPost("DeleteWipItem")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteWipItem(uint wipItemId)
    {
        var result = _svc.HardDeleteWipItem(wipItemId);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("DeleteWipStepExecution")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteWipStepExecution(uint wipStepExecutionId)
    {
        var result = _svc.HardDeleteWipStepExecution(wipStepExecutionId);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
