using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Trackii.Controllers;

[Authorize]
public class LobbyController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
