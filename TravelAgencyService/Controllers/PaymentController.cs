using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly PdfService _pdfService;

        public PaymentController(ApplicationDbContext context, IEmailSender emailSender, PdfService pdfService)
        {
            _context = context;
            _emailSender = emailSender;
            _pdfService = pdfService;
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

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(booking.TripId, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id = booking.TripId });
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
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.BookingId == model.BookingId && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

            if (booking.IsPaid)
            {
                return RedirectToAction("Confirmation", "Booking", new { id = model.BookingId });
            }

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(booking.TripId, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id = booking.TripId });
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
                var paymentSuccess = ProcessPayment(model);

                if (paymentSuccess)
                {
                    booking.IsPaid = true;
                    booking.PaymentDate = DateTime.Now;
                    booking.Status = BookingStatus.Confirmed;
                    booking.CardLastFourDigits = model.CardNumber.Substring(model.CardNumber.Length - 4);
                    booking.UpdatedAt = DateTime.Now;

                    // Update waiting list if user was in it
                    var userWaitingEntry = await _context.WaitingListEntries
                        .FirstOrDefaultAsync(w => w.UserId == userId &&
                                                  w.TripId == booking.TripId &&
                                                  (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                    if (userWaitingEntry != null)
                    {
                        var removedPosition = userWaitingEntry.Position;
                        userWaitingEntry.Status = WaitingListStatus.Booked;
                        await AdvanceWaitingListPositions(booking.TripId, removedPosition);
                    }

                    await _context.SaveChangesAsync();

                    // Send confirmation email with PDF
                    await SendBookingConfirmationEmail(booking);

                    TempData["Success"] = "Payment successful! Your booking is confirmed. You can view it in your email and under My Bookings.";
                    return RedirectToAction("Confirmation", "Booking", new { id = booking.BookingId });
                }
                else
                {
                    ModelState.AddModelError("", "Payment failed. Please check your card details and try again.");
                }
            }

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

        private bool ProcessPayment(CheckoutViewModel model)
        {
            if (model.CardNumber.StartsWith("0000"))
            {
                return false;
            }
            return true;
        }

        private bool ProcessPayment(CartCheckoutViewModel model)
        {
            if (model.CardNumber.StartsWith("0000"))
            {
                return false;
            }
            return true;
        }

        #region Helper Methods

        private async Task<(bool CanProceed, string Message)> CheckWaitingListPriority(int tripId, string currentUserId)
        {
            var notifiedEntry = await _context.WaitingListEntries
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Notified &&
                            w.NotificationExpiresAt > DateTime.Now)
                .FirstOrDefaultAsync();

            if (notifiedEntry == null)
            {
                return (true, string.Empty);
            }

            if (notifiedEntry.UserId == currentUserId)
            {
                return (true, string.Empty);
            }

            return (false, "Someone from the waiting list currently has priority to book. Please try again later.");
        }

        private async Task AdvanceWaitingListPositions(int tripId, int removedPosition)
        {
            var entriesToAdvance = await _context.WaitingListEntries
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.Position > removedPosition)
                .ToListAsync();

            foreach (var entry in entriesToAdvance)
            {
                entry.Position--;
            }
        }

        private async Task SendBookingConfirmationEmail(Booking booking)
        {
            try
            {
                if (booking.User == null || string.IsNullOrEmpty(booking.User.Email))
                    return;

                var userName = booking.User.FirstName ?? "Traveler";
                var tripName = booking.Trip?.PackageName ?? "Your Trip";
                var destination = booking.Trip?.Destination ?? "";
                var country = booking.Trip?.Country ?? "";
                var startDate = booking.Trip?.StartDate.ToString("MMMM dd, yyyy") ?? "";
                var endDate = booking.Trip?.EndDate.ToString("MMMM dd, yyyy") ?? "";

                var subject = $"Booking Confirmed - {tripName}";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Booking Confirmed!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Your booking has been confirmed! Here are the details:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #667eea;'>
                            <h2 style='color: #667eea; margin-top: 0;'>{tripName}</h2>
                            <p><strong>Destination:</strong> {destination}, {country}</p>
                            <p><strong>Dates:</strong> {startDate} - {endDate}</p>
                            <p><strong>Rooms:</strong> {booking.NumberOfRooms}</p>
                            <p><strong>Total Paid:</strong> ${booking.TotalPrice:N2}</p>
                            <p><strong>Booking ID:</strong> #{booking.BookingId}</p>
                        </div>
                        
                        <p>Your PDF itinerary is attached to this email.</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>{DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                byte[]? pdfBytes = null;
                try
                {
                    pdfBytes = _pdfService.GenerateItinerary(booking);
                }
                catch
                {
                    // Continue without PDF if generation fails
                }

                if (pdfBytes != null)
                {
                    var fileName = $"Itinerary_{tripName.Replace(" ", "_")}_{booking.BookingId}.pdf";
                    await _emailSender.SendWithAttachmentAsync(
                        booking.User.Email,
                        subject,
                        htmlBody,
                        pdfBytes,
                        fileName,
                        "application/pdf"
                    );
                }
                else
                {
                    await _emailSender.SendAsync(booking.User.Email, subject, htmlBody);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send booking confirmation email: {ex.Message}");
            }
        }

        #endregion
    }
}