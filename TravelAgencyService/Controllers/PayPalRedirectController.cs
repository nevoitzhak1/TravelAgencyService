using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Services;
using TravelAgencyService.Services.Email;
using TravelAgencyService.Services.PayPal;

namespace TravelAgencyService.Controllers;

[Authorize]
public class PayPalRedirectController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly PayPalClient _pp;
    private readonly IEmailSender _emailSender;
    private readonly PdfService _pdfService;

    public PayPalRedirectController(
        ApplicationDbContext context,
        PayPalClient pp,
        IEmailSender emailSender,
        PdfService pdfService)
    {
        _context = context;
        _pp = pp;
        _emailSender = emailSender;
        _pdfService = pdfService;
    }

    // POST: Start PayPal payment from Cart
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartFromCart(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account");

        var cartItems = await _context.CartItems
            .Include(c => c.Trip)
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        if (!cartItems.Any())
        {
            TempData["Error"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }

        // ========== AGE RESTRICTION CHECK FOR CART ITEMS ==========
        foreach (var item in cartItems)
        {
            if (item.Trip != null)
            {
                var ageCheck = await CheckAgeRestriction(item.Trip, userId);
                if (!ageCheck.IsAllowed)
                {
                    TempData["Error"] = $"'{item.Trip.PackageName}': {ageCheck.ErrorMessage}";
                    return RedirectToAction("Index", "Cart");
                }
            }
        }
        // ==========================================================

        // Check waiting list priority for each item
        foreach (var item in cartItems)
        {
            var priorityCheck = await CheckWaitingListPriority(item.TripId, userId);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = $"'{item.Trip?.PackageName}': {priorityCheck.Message}";
                return RedirectToAction("Index", "Cart");
            }
        }

        // Validate availability and calculate total
        decimal total = 0m;
        foreach (var item in cartItems)
        {
            var trip = await _context.Trips.FindAsync(new object[] { item.TripId }, ct);
            if (trip == null || trip.AvailableRooms < item.NumberOfRooms || !trip.IsVisible || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = $"'{item.Trip?.PackageName ?? "Trip"}' is no longer available.";
                return RedirectToAction("Index", "Cart");
            }

            total += trip.Price * item.NumberOfRooms;
        }

        // Create Bookings with Pending status (not burning inventory yet)
        using var tx = await _context.Database.BeginTransactionAsync(ct);
        var bookingIds = new List<int>();

        try
        {
            foreach (var item in cartItems)
            {
                var trip = await _context.Trips.FindAsync(new object[] { item.TripId }, ct);
                if (trip == null || trip.AvailableRooms < item.NumberOfRooms)
                    throw new Exception("Trip availability changed.");

                var booking = new Booking
                {
                    UserId = userId!,
                    TripId = item.TripId,
                    NumberOfRooms = item.NumberOfRooms,
                    TotalPrice = trip.Price * item.NumberOfRooms,
                    BookingDate = DateTime.Now,
                    Status = BookingStatus.Pending,
                    IsPaid = false,
                    PaymentDate = null,
                    CardLastFourDigits = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync(ct);
                bookingIds.Add(booking.BookingId);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "Could not start PayPal payment.";
            return RedirectToAction("Index", "Cart");
        }

        var bookingIdsCsv = string.Join(",", bookingIds);

        // Added source = "cart" parameter
        var returnUrl = Url.Action("Return", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "cart" }, Request.Scheme)!;
        var cancelUrl = Url.Action("Cancel", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "cart" }, Request.Scheme)!;

        var currency = "USD";
        var (_, approveUrl) = await _pp.CreateOrderAndGetApproveUrlAsync(total, currency, returnUrl, cancelUrl, ct);

        return Redirect(approveUrl);
    }

    // POST: Start PayPal payment for Single Trip (Buy Now)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSingle(int tripId, int rooms, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account");

        var trip = await _context.Trips.FindAsync(new object[] { tripId }, ct);
        if (trip == null || !trip.IsVisible || trip.StartDate <= DateTime.Now || rooms < 1 || trip.AvailableRooms < rooms)
        {
            TempData["Error"] = "Trip is not available.";
            return RedirectToAction("Details", "Trip", new { id = tripId });
        }

        // ========== AGE RESTRICTION CHECK ==========
        var ageCheck = await CheckAgeRestriction(trip, userId);
        if (!ageCheck.IsAllowed)
        {
            TempData["Error"] = ageCheck.ErrorMessage;
            return RedirectToAction("Details", "Trip", new { id = tripId });
        }
        // ===========================================

        // Check waiting list priority
        var priorityCheck = await CheckWaitingListPriority(tripId, userId);
        if (!priorityCheck.CanProceed)
        {
            TempData["Error"] = priorityCheck.Message;
            return RedirectToAction("Details", "Trip", new { id = tripId });
        }

        // Check max bookings
        var activeBookingsCount = await _context.Bookings
            .CountAsync(b => b.UserId == userId &&
                             b.Status == BookingStatus.Confirmed &&
                             b.Trip!.StartDate > DateTime.Now, ct);

        if (activeBookingsCount >= 3)
        {
            TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
            return RedirectToAction("Details", "Trip", new { id = tripId });
        }

        // Create a Pending booking
        using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var booking = new Booking
            {
                UserId = userId!,
                TripId = tripId,
                NumberOfRooms = rooms,
                TotalPrice = trip.Price * rooms,
                BookingDate = DateTime.Now,
                Status = BookingStatus.Pending,
                IsPaid = false,
                PaymentDate = null,
                CardLastFourDigits = null,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var bookingIdsCsv = booking.BookingId.ToString();

            // source = "single" for single trip
            var returnUrl = Url.Action("Return", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "single" }, Request.Scheme)!;
            var cancelUrl = Url.Action("Cancel", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "single" }, Request.Scheme)!;

            var currency = "USD";
            var total = trip.Price * rooms;
            var (_, approveUrl) = await _pp.CreateOrderAndGetApproveUrlAsync(total, currency, returnUrl, cancelUrl, ct);

            return Redirect(approveUrl);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "Could not start PayPal payment.";
            return RedirectToAction("Checkout", "Cart", new { tripId, rooms });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Return(string bookingIds, string token, string? source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(bookingIds))
            return BadRequest("Missing parameters.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account", new { returnUrl = $"/PayPalRedirect/Return?bookingIds={bookingIds}&token={token}&source={source}" });

        var capture = await _pp.CaptureOrderAsync(token, ct);
        var status = capture.GetProperty("status").GetString();

        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "PayPal payment was not completed.";
            return RedirectToAction("Checkout", "Cart");
        }

        var ids = bookingIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse)
                            .ToList();

        using var tx = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            var bookings = await _context.Bookings
                .Include(b => b.Trip)
                .Include(b => b.User)
                .Where(b => ids.Contains(b.BookingId) && b.UserId == userId)
                .ToListAsync(ct);

            if (bookings.Count != ids.Count)
                throw new Exception("Some bookings not found.");

            foreach (var b in bookings)
            {
                if (b.IsPaid) continue;

                var trip = await _context.Trips.FindAsync(new object[] { b.TripId }, ct);
                if (trip == null || trip.AvailableRooms < b.NumberOfRooms)
                    throw new Exception("Trip availability changed.");

                // Burn inventory now
                trip.AvailableRooms -= b.NumberOfRooms;
                trip.TimesBooked++;
                trip.UpdatedAt = DateTime.Now;

                b.IsPaid = true;
                b.PaymentDate = DateTime.Now;
                b.Status = BookingStatus.Confirmed;
                b.UpdatedAt = DateTime.Now;

                // Update waiting list if user was in it
                var userWaitingEntry = await _context.WaitingListEntries
                    .FirstOrDefaultAsync(w => w.UserId == userId &&
                                              w.TripId == b.TripId &&
                                              (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified), ct);

                if (userWaitingEntry != null)
                {
                    var removedPosition = userWaitingEntry.Position;
                    userWaitingEntry.Status = WaitingListStatus.Booked;
                    await AdvanceWaitingListPositions(b.TripId, removedPosition, ct);
                }
            }

            // Only clear cart if source is "cart"
            if (source == "cart")
            {
                var cartItems = await _context.CartItems.Where(c => c.UserId == userId).ToListAsync(ct);
                _context.CartItems.RemoveRange(cartItems);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Send confirmation emails
            foreach (var booking in bookings)
            {
                await SendBookingConfirmationEmail(booking);
            }

            TempData["Success"] = $"PayPal payment successful! {bookings.Count} booking(s) confirmed.";

            // Redirect based on source
            if (source == "single" && bookings.Count == 1)
            {
                return RedirectToAction("Confirmation", "Booking", new { id = bookings[0].BookingId });
            }

            return RedirectToAction("MyBookings", "Booking");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "An error occurred while completing PayPal payment.";
            return RedirectToAction("Checkout", "Cart");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Cancel(string bookingIds, string? source, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Delete the Pending bookings that were created
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(bookingIds))
        {
            var ids = bookingIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var pending = await _context.Bookings
                .Where(b => ids.Contains(b.BookingId) && b.UserId == userId && !b.IsPaid && b.Status == BookingStatus.Pending)
                .ToListAsync(ct);

            _context.Bookings.RemoveRange(pending);
            await _context.SaveChangesAsync(ct);
        }

        TempData["Error"] = "PayPal payment was canceled.";

        // Redirect based on source
        if (source == "single")
        {
            return RedirectToAction("Index", "Trip");
        }

        return RedirectToAction("Index", "Cart");
    }

    #region Helper Methods

    private async Task<(bool IsAllowed, string? ErrorMessage)> CheckAgeRestriction(Trip trip, string userId)
    {
        // If no age restrictions, allow
        if (!trip.MinimumAge.HasValue && !trip.MaximumAge.HasValue)
            return (true, null);

        var user = await _context.Users.FindAsync(userId);

        // If user has no date of birth but trip has age restrictions
        if (user?.DateOfBirth == null || user.DateOfBirth.Value.Year < 1900)
        {
            return (false, "This trip has age restrictions. Please update your date of birth in your profile to proceed.");
        }

        // Calculate exact age
        var today = DateTime.Today;
        var birthDate = user.DateOfBirth.Value;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age)) age--;

        // Build age range string
        string ageRange;
        if (trip.MinimumAge.HasValue && trip.MaximumAge.HasValue)
            ageRange = $"{trip.MinimumAge}-{trip.MaximumAge}";
        else if (trip.MinimumAge.HasValue)
            ageRange = $"{trip.MinimumAge}+";
        else
            ageRange = $"up to {trip.MaximumAge}";

        // Check minimum age
        if (trip.MinimumAge.HasValue && age < trip.MinimumAge.Value)
        {
            return (false, $"This trip is for ages {ageRange}. Based on your date of birth, you do not meet the age requirement and cannot book this trip.");
        }

        // Check maximum age
        if (trip.MaximumAge.HasValue && age > trip.MaximumAge.Value)
        {
            return (false, $"This trip is for ages {ageRange}. Based on your date of birth, you do not meet the age requirement and cannot book this trip.");
        }

        return (true, null);
    }


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

    private async Task AdvanceWaitingListPositions(int tripId, int removedPosition, CancellationToken ct)
    {
        var entriesToAdvance = await _context.WaitingListEntries
            .Where(w => w.TripId == tripId &&
                        w.Status == WaitingListStatus.Waiting &&
                        w.Position > removedPosition)
            .ToListAsync(ct);

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
                    
                    <p>Your PayPal payment was successful! Here are your booking details:</p>
                    
                    <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #667eea;'>
                        <h2 style='color: #667eea; margin-top: 0;'>{tripName}</h2>
                        <p><strong>Destination:</strong> {destination}, {country}</p>
                        <p><strong>Dates:</strong> {startDate} - {endDate}</p>
                        <p><strong>Rooms:</strong> {booking.NumberOfRooms}</p>
                        <p><strong>Total Paid:</strong> ${booking.TotalPrice:N2}</p>
                        <p><strong>Booking ID:</strong> #{booking.BookingId}</p>
                        <p><strong>Payment Method:</strong> PayPal</p>
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