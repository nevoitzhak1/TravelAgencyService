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
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly PdfService _pdfService;

        public CartController(ApplicationDbContext context, IEmailSender emailSender, PdfService pdfService)
        {
            _context = context;
            _emailSender = emailSender;
            _pdfService = pdfService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.AddedDate)
                .ToListAsync();

            var viewModel = new CartViewModel
            {
                Items = cartItems.Select(c => new CartItemViewModel
                {
                    CartItemId = c.CartItemId,
                    TripId = c.TripId,
                    PackageName = c.Trip?.PackageName ?? "",
                    Destination = c.Trip?.Destination ?? "",
                    Country = c.Trip?.Country ?? "",
                    StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                    PricePerPerson = c.Trip?.Price ?? 0,
                    OriginalPrice = c.Trip?.OriginalPrice,
                    IsOnSale = c.Trip?.IsOnSale ?? false,
                    NumberOfRooms = c.NumberOfRooms,
                    MainImageUrl = c.Trip?.MainImageUrl,
                    AvailableRooms = c.Trip?.AvailableRooms ?? 0,
                    IsStillAvailable = c.Trip != null &&
                                       c.Trip.IsVisible &&
                                       c.Trip.AvailableRooms >= c.NumberOfRooms &&
                                       c.Trip.StartDate > DateTime.Now,
                    AddedDate = c.AddedDate
                }).ToList()
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Add(int id, int rooms = 1)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existingItem = await _context.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.TripId == id);

            if (existingItem != null)
            {
                TempData["Info"] = "This trip is already in your cart.";
                return RedirectToAction("Index");
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (!trip.IsVisible || trip.AvailableRooms <= 0 || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = "This trip is not available for booking.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(id, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id });
            }

            var cartItem = new CartItem
            {
                UserId = userId!,
                TripId = id,
                NumberOfRooms = Math.Min(rooms, trip.AvailableRooms),
                PriceWhenAdded = trip.Price,
                AddedDate = DateTime.Now
            };

            _context.CartItems.Add(cartItem);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{trip.PackageName}' has been added to your cart.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int numberOfRooms)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItem = await _context.CartItems
                .Include(c => c.Trip)
                .FirstOrDefaultAsync(c => c.CartItemId == cartItemId && c.UserId == userId);

            if (cartItem == null)
            {
                return NotFound();
            }

            if (cartItem.Trip != null && numberOfRooms > cartItem.Trip.AvailableRooms)
            {
                TempData["Error"] = $"Only {cartItem.Trip.AvailableRooms} rooms are available.";
                return RedirectToAction("Index");
            }

            if (numberOfRooms < 1) numberOfRooms = 1;
            else if (numberOfRooms > 10) numberOfRooms = 10;

            cartItem.NumberOfRooms = numberOfRooms;
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItem = await _context.CartItems
                .Include(c => c.Trip)
                .FirstOrDefaultAsync(c => c.CartItemId == id && c.UserId == userId);

            if (cartItem == null)
            {
                return NotFound();
            }

            var tripName = cartItem.Trip?.PackageName ?? "Trip";

            _context.CartItems.Remove(cartItem);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{tripName}' has been removed from your cart.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Where(c => c.UserId == userId)
                .ToListAsync();

            _context.CartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your cart has been cleared.";
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Checkout()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            var unavailableItems = cartItems.Where(c =>
                c.Trip == null ||
                !c.Trip.IsVisible ||
                c.Trip.AvailableRooms < c.NumberOfRooms ||
                c.Trip.StartDate <= DateTime.Now).ToList();

            if (unavailableItems.Any())
            {
                TempData["Error"] = "Some items in your cart are no longer available.";
                return RedirectToAction("Index");
            }

            // Check waiting list priority for each item
            foreach (var item in cartItems)
            {
                var priorityCheck = await CheckWaitingListPriority(item.TripId, userId!);
                if (!priorityCheck.CanProceed)
                {
                    TempData["Error"] = $"'{item.Trip?.PackageName}': {priorityCheck.Message}";
                    return RedirectToAction("Index");
                }
            }

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount + cartItems.Count > 3)
            {
                TempData["Error"] = $"You can only have 3 active bookings. You currently have {activeBookingsCount}.";
                return RedirectToAction("Index");
            }

            var viewModel = new CartCheckoutViewModel
            {
                Items = cartItems.Select(c => new CartItemViewModel
                {
                    CartItemId = c.CartItemId,
                    TripId = c.TripId,
                    PackageName = c.Trip?.PackageName ?? "",
                    Destination = c.Trip?.Destination ?? "",
                    Country = c.Trip?.Country ?? "",
                    StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                    PricePerPerson = c.Trip?.Price ?? 0,
                    NumberOfRooms = c.NumberOfRooms,
                    MainImageUrl = c.Trip?.MainImageUrl
                }).ToList()
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(CartCheckoutViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            // Check waiting list priority for each item before processing
            foreach (var item in cartItems)
            {
                var priorityCheck = await CheckWaitingListPriority(item.TripId, userId!);
                if (!priorityCheck.CanProceed)
                {
                    TempData["Error"] = $"'{item.Trip?.PackageName}': {priorityCheck.Message}";
                    return RedirectToAction("Index");
                }
            }

            var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate < currentDate)
            {
                ModelState.AddModelError("ExpiryMonth", "Card has expired");
            }

            if (!string.IsNullOrEmpty(model.CardNumber))
            {
                model.CardNumber = model.CardNumber.Replace(" ", "");
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var bookingIds = new List<int>();
                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    foreach (var item in cartItems)
                    {
                        if (item.Trip == null) continue;

                        var trip = await _context.Trips.FindAsync(item.TripId);
                        if (trip == null || trip.AvailableRooms < item.NumberOfRooms)
                        {
                            await transaction.RollbackAsync();
                            TempData["Error"] = $"'{item.Trip.PackageName}' is no longer available.";
                            return RedirectToAction("Index");
                        }

                        var booking = new Booking
                        {
                            UserId = userId!,
                            TripId = item.TripId,
                            NumberOfRooms = item.NumberOfRooms,
                            TotalPrice = trip.Price * item.NumberOfRooms,
                            BookingDate = DateTime.Now,
                            Status = BookingStatus.Confirmed,
                            IsPaid = true,
                            PaymentDate = DateTime.Now,
                            CardLastFourDigits = cardLastFour,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        trip.AvailableRooms -= item.NumberOfRooms;
                        trip.TimesBooked++;
                        trip.UpdatedAt = DateTime.Now;

                        // Update waiting list if user was in it
                        var userWaitingEntry = await _context.WaitingListEntries
                            .FirstOrDefaultAsync(w => w.UserId == userId &&
                                                      w.TripId == item.TripId &&
                                                      (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                        if (userWaitingEntry != null)
                        {
                            var removedPosition = userWaitingEntry.Position;
                            userWaitingEntry.Status = WaitingListStatus.Booked;
                            await AdvanceWaitingListPositions(item.TripId, removedPosition);
                        }

                        _context.Bookings.Add(booking);
                        await _context.SaveChangesAsync();
                        bookingIds.Add(booking.BookingId);
                    }

                    _context.CartItems.RemoveRange(cartItems);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Send confirmation emails
                    foreach (var bookingId in bookingIds)
                    {
                        await SendBookingConfirmationEmail(bookingId);
                    }

                    TempData["Success"] = $"Payment successful! {bookingIds.Count} booking(s) confirmed.";
                    return RedirectToAction("MyBookings", "Booking");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "An error occurred while processing your payment.";
                    return RedirectToAction("Index");
                }

            }

            model.Items = cartItems.Select(c => new CartItemViewModel
            {
                CartItemId = c.CartItemId,
                TripId = c.TripId,
                PackageName = c.Trip?.PackageName ?? "",
                Destination = c.Trip?.Destination ?? "",
                Country = c.Trip?.Country ?? "",
                StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                PricePerPerson = c.Trip?.Price ?? 0,
                NumberOfRooms = c.NumberOfRooms,
                MainImageUrl = c.Trip?.MainImageUrl
            }).ToList();

            return View(model);
        }

        public async Task<IActionResult> BuyNow(int id, int rooms = 1)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount >= 3)
            {
                TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (!trip.IsVisible || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = "This trip is not available for booking.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            if (trip.AvailableRooms <= 0)
            {
                TempData["Error"] = "This trip is fully booked. You can join the waiting list.";
                return RedirectToAction("JoinWaitingList", "Booking", new { id });
            }

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(id, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id });
            }

            var viewModel = new BuyNowViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                PricePerRoom = trip.Price,
                OriginalPrice = trip.OriginalPrice,
                IsOnSale = trip.IsOnSale,
                AvailableRooms = trip.AvailableRooms,
                MainImageUrl = trip.MainImageUrl,
                NumberOfRooms = Math.Min(rooms, trip.AvailableRooms)
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuyNow(BuyNowViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check waiting list priority before processing
            var priorityCheck = await CheckWaitingListPriority(model.TripId, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id = model.TripId });
            }

            var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate < currentDate)
            {
                ModelState.AddModelError("ExpiryMonth", "Card has expired");
            }

            if (!string.IsNullOrEmpty(model.CardNumber))
            {
                model.CardNumber = model.CardNumber.Replace(" ", "");
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var trip = await _context.Trips.FindAsync(model.TripId);

                    if (trip == null)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "Trip not found.";
                        return RedirectToAction("Index", "Trip");
                    }

                    if (trip.AvailableRooms < model.NumberOfRooms)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = $"Sorry, only {trip.AvailableRooms} room(s) available. Someone else booked while you were checking out.";
                        return RedirectToAction("Details", "Trip", new { id = model.TripId });
                    }

                    var activeBookingsCount = await _context.Bookings
                        .CountAsync(b => b.UserId == userId &&
                                         b.Status == BookingStatus.Confirmed &&
                                         b.Trip!.StartDate > DateTime.Now);

                    if (activeBookingsCount >= 3)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
                        return RedirectToAction("Details", "Trip", new { id = model.TripId });
                    }

                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    var booking = new Booking
                    {
                        UserId = userId!,
                        TripId = model.TripId,
                        NumberOfRooms = model.NumberOfRooms,
                        TotalPrice = trip.Price * model.NumberOfRooms,
                        BookingDate = DateTime.Now,
                        Status = BookingStatus.Confirmed,
                        IsPaid = true,
                        PaymentDate = DateTime.Now,
                        CardLastFourDigits = cardLastFour,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    trip.AvailableRooms -= model.NumberOfRooms;
                    trip.TimesBooked++;
                    trip.UpdatedAt = DateTime.Now;

                    // Update waiting list if user was in it
                    var userWaitingEntry = await _context.WaitingListEntries
                        .FirstOrDefaultAsync(w => w.UserId == userId &&
                                                  w.TripId == model.TripId &&
                                                  (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                    if (userWaitingEntry != null)
                    {
                        var removedPosition = userWaitingEntry.Position;
                        userWaitingEntry.Status = WaitingListStatus.Booked;
                        await AdvanceWaitingListPositions(model.TripId, removedPosition);
                    }

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Send confirmation email with PDF
                    await SendBookingConfirmationEmail(booking.BookingId);

                    TempData["Success"] = "Payment successful! Your booking is confirmed.";
                    return RedirectToAction("Confirmation", "Booking", new { id = booking.BookingId });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "An error occurred. Please try again.";
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }
            }

            var tripData = await _context.Trips.FindAsync(model.TripId);
            if (tripData != null)
            {
                model.PackageName = tripData.PackageName;
                model.Destination = tripData.Destination;
                model.Country = tripData.Country;
                model.StartDate = tripData.StartDate;
                model.EndDate = tripData.EndDate;
                model.PricePerRoom = tripData.Price;
                model.OriginalPrice = tripData.OriginalPrice;
                model.IsOnSale = tripData.IsOnSale;
                model.AvailableRooms = tripData.AvailableRooms;
                model.MainImageUrl = tripData.MainImageUrl;
            }

            return View(model);
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

        private async Task SendBookingConfirmationEmail(int bookingId)
        {
            try
            {
                var booking = await _context.Bookings
                    .Include(b => b.Trip)
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.BookingId == bookingId);

                if (booking == null || booking.User == null || string.IsNullOrEmpty(booking.User.Email))
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

                // Generate PDF
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