using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Trackii.Controllers;

[Authorize]
public class ScannerController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
