using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SistemaNutri.Controllers
{
    public class AccountController : Controller
    {
        [AllowAnonymous]
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            ViewBag.RutaSolicitada = returnUrl;
            return View();
        }
    }
}
