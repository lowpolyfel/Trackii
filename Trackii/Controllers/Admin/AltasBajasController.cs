using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Trackii.Controllers.Admin;

[Authorize(Roles = "Admin,Engineering,Ingenieria")]
[Route("Admin/AltasBajas")]
public class AltasBajasController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View("~/Views/Management/AltasBajas/Index.cshtml");
    }
}
