using Microsoft.AspNetCore.Mvc;

namespace TravelAgencyService.Controllers
{
    public class BookingController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
