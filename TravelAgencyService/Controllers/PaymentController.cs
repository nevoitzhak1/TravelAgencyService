using Microsoft.AspNetCore.Mvc;

namespace TravelAgencyService.Controllers
{
    public class PaymentController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
