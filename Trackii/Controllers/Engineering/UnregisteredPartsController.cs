using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Engineering;
using Trackii.Services.Engineering;

namespace Trackii.Controllers.Engineering;

[Authorize(Roles = "Admin,Engineering,Ingenieria")]
[Route("Engineering/UnregisteredParts")]
public class UnregisteredPartsController : Controller
{
    private readonly UnregisteredPartsService _svc;
    private const string ViewBase = "~/Views/Engineering/";

    public UnregisteredPartsController(UnregisteredPartsService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? search, bool onlyActive = true, int page = 1)
    {
        var vm = _svc.GetPaged(search, onlyActive, page, 10);
        return View($"{ViewBase}UnregisteredParts.cshtml", vm);
    }

    [HttpGet("Create")]
    public IActionResult Create(string? partNumber)
    {
        return RedirectToAction("Create", "Product", new { partNumber });
    }

    [HttpPost("Close")]
    [ValidateAntiForgeryToken]
    public IActionResult Close(uint partId)
    {
        _svc.Close(partId);
        return RedirectToAction(nameof(Index));
    }
}
