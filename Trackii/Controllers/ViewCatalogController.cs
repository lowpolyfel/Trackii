using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services;

namespace Trackii.Controllers;

[Authorize]
[Route("ViewCatalog")]
public class ViewCatalogController : Controller
{
    private readonly ViewCatalogService _svc;

    public ViewCatalogController(ViewCatalogService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var vm = _svc.GetCatalog();
        return View(vm);
    }
}
