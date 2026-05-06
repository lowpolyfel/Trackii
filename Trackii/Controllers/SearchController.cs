using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Services.Search;

namespace Trackii.Controllers;

[Authorize(Roles = "Admin")]
[Route("Buscador")]
public class SearchController : Controller
{
    private readonly SearchService _svc;
    private const string ViewBase = "~/Views/Search/";

    public SearchController(SearchService svc)
    {
        _svc = svc;
    }

    [HttpGet("")]
    public IActionResult Index(string? q)
    {
        var vm = _svc.Search(q);
        return View($"{ViewBase}Index.cshtml", vm);
    }

    [HttpGet("Detalle/{productId:long}")]
    public IActionResult Detail(uint productId, uint? workOrderId)
    {
        var vm = _svc.GetDetail(productId, workOrderId);
        if (vm == null)
            return NotFound();

        return View($"{ViewBase}Detail.cshtml", vm);
    }
}
