using Microsoft.AspNetCore.Mvc;

namespace TravelAgencyService.Controllers
{
    public class UserDashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
