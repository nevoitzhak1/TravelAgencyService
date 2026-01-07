using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PaymentController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Payment/Checkout?bookingId=5
        public async Task<IActionResult> Checkout(int bookingId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

            if (booking.IsPaid)
            {
                TempData["Info"] = "This booking has already been paid.";
                return RedirectToAction("Confirmation", "Booking", new { id = bookingId });
            }

            var viewModel = new CheckoutViewModel
            {
                BookingId = booking.BookingId,
                PackageName = booking.Trip?.PackageName ?? "",
                Destination = booking.Trip?.Destination ?? "",
                Country = booking.Trip?.Country ?? "",
                StartDate = booking.Trip?.StartDate ?? DateTime.MinValue,
                EndDate = booking.Trip?.EndDate ?? DateTime.MinValue,
                NumberOfRooms = booking.NumberOfRooms,
                TotalPrice = booking.TotalPrice,
                MainImageUrl = booking.Trip?.MainImageUrl
            };

            return View(viewModel);
        }

        // POST: /Payment/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(CheckoutViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.BookingId == model.BookingId && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

            if (booking.IsPaid)
            {
                return RedirectToAction("Confirmation", "Booking", new { id = model.BookingId });
            }

            // Validate expiry date
            var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate < currentDate)
            {
                ModelState.AddModelError("ExpiryMonth", "Card has expired");
            }

            if (ModelState.IsValid)
            {
                // Simulate payment processing
                var paymentSuccess = ProcessPayment(model);

                if (paymentSuccess)
                {
                    // Update booking
                    booking.IsPaid = true;
                    booking.PaymentDate = DateTime.Now;
                    booking.Status = BookingStatus.Confirmed;
                    booking.CardLastFourDigits = model.CardNumber.Substring(model.CardNumber.Length - 4);
                    booking.UpdatedAt = DateTime.Now;

                    await _context.SaveChangesAsync();

                    // TODO: Send confirmation email

                    TempData["Success"] = "✅ Payment successful! Your booking is confirmed. You can view it in your email and under My Bookings.";
                    return RedirectToAction("Confirmation", "Booking", new { id = booking.BookingId });

                }
                else
                {
                    ModelState.AddModelError("", "Payment failed. Please check your card details and try again.");
                }
            }

            // Reload view model data
            model.PackageName = booking.Trip?.PackageName ?? "";
            model.Destination = booking.Trip?.Destination ?? "";
            model.Country = booking.Trip?.Country ?? "";
            model.StartDate = booking.Trip?.StartDate ?? DateTime.MinValue;
            model.EndDate = booking.Trip?.EndDate ?? DateTime.MinValue;
            model.NumberOfRooms = booking.NumberOfRooms;
            model.TotalPrice = booking.TotalPrice;
            model.MainImageUrl = booking.Trip?.MainImageUrl;

            return View(model);
        }

        // GET: /Payment/Failed
        public IActionResult Failed(string? message)
        {
            ViewBag.ErrorMessage = message ?? "An error occurred during payment processing.";
            return View();
        }

        // Simulate payment processing (In real app, integrate with Stripe, PayPal, etc.)
        private bool ProcessPayment(CheckoutViewModel model)
        {
            // Simulate payment validation
            // In production, this would call a real payment gateway

            // Simple validation - reject cards starting with 0000 for testing
            if (model.CardNumber.StartsWith("0000"))
            {
                return false;
            }

            // Simulate successful payment
            return true;
        }

        // Simulate payment for cart
        private bool ProcessPayment(CartCheckoutViewModel model)
        {
            if (model.CardNumber.StartsWith("0000"))
            {
                return false;
            }
            return true;
        }
    }
}