using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfService _pdfService;
        private readonly IEmailSender _emailSender;

        public BookingController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PdfService pdfService,
            IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _pdfService = pdfService;
            _emailSender = emailSender;
        }

        // GET: /Booking/MyBookings
        public async Task<IActionResult> MyBookings(bool showPast = false, bool showCancelled = false, bool showWaitingList = false)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var bookings = await _context.Bookings
                .Include(b => b.Trip)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            var upcomingBookings = new List<UserBookingViewModel>();
            var pastBookings = new List<UserBookingViewModel>();

            foreach (var booking in bookings)
            {
                var hasReviewed = await _context.Reviews
                    .AnyAsync(r => r.UserId == userId && r.TripId == booking.TripId);

                var viewModel = new UserBookingViewModel
                {
                    BookingId = booking.BookingId,
                    TripId = booking.TripId,
                    PackageName = booking.Trip?.PackageName ?? "Unknown",
                    Destination = booking.Trip?.Destination ?? "",
                    Country = booking.Trip?.Country ?? "",
                    StartDate = booking.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = booking.Trip?.EndDate ?? DateTime.MinValue,
                    NumberOfRooms = booking.NumberOfRooms,
                    TotalPrice = booking.TotalPrice,
                    BookingDate = booking.BookingDate,
                    Status = booking.Status,
                    IsPaid = booking.IsPaid,
                    MainImageUrl = booking.Trip?.MainImageUrl,
                    PackageType = booking.Trip?.PackageType ?? PackageType.Family,
                    CancellationDaysLimit = booking.Trip?.CancellationDaysLimit ?? 7,
                    HasReviewed = hasReviewed
                };

                if (booking.Trip != null && booking.Status == BookingStatus.Confirmed)
                {
                    var daysUntilTrip = (booking.Trip.StartDate - DateTime.Now).Days;
                    viewModel.DaysUntilDeparture = daysUntilTrip > 0 ? daysUntilTrip : null;
                    viewModel.CanBeCancelled = daysUntilTrip >= booking.Trip.CancellationDaysLimit;
                }

                if (booking.Trip?.EndDate >= DateTime.Now && booking.Status == BookingStatus.Confirmed)
                {
                    upcomingBookings.Add(viewModel);
                }
                else
                {
                    pastBookings.Add(viewModel);
                }
            }

            // Get waiting list entries for this user
            var waitingListEntries = await _context.WaitingListEntries
                .Include(w => w.Trip)
                .Where(w => w.UserId == userId &&
           (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified) &&
           w.Trip != null && w.Trip.StartDate > DateTime.Now)
                .OrderBy(w => w.JoinedDate)
                .Select(w => new WaitingListItemViewModel
                {
                    WaitingListEntryId = w.WaitingListEntryId,
                    TripId = w.TripId,
                    PackageName = w.Trip != null ? w.Trip.PackageName : "Unknown",
                    Destination = w.Trip != null ? w.Trip.Destination : "",
                    Country = w.Trip != null ? w.Trip.Country : "",
                    StartDate = w.Trip != null ? w.Trip.StartDate : DateTime.MinValue,
                    EndDate = w.Trip != null ? w.Trip.EndDate : DateTime.MinValue,
                    MainImageUrl = w.Trip != null ? w.Trip.MainImageUrl : null,
                    Position = w.Position,
                    RoomsRequested = w.RoomsRequested,
                    JoinedDate = w.JoinedDate,
                    Status = w.Status,
                    IsNotified = w.Status == WaitingListStatus.Notified,
                    NotificationExpiresAt = w.NotificationExpiresAt
                })
                .ToListAsync();

            var model = new MyBookingsViewModel
            {
                UpcomingBookings = upcomingBookings,
                PastBookings = pastBookings,
                WaitingListEntries = waitingListEntries,
                TotalBookings = bookings.Count,
                ShowPastBookings = showPast,
                ShowCancelledBookings = showCancelled,
                ShowWaitingList = showWaitingList
            };

            return View(model);
        }

        // GET: /Booking/Book/5
        public async Task<IActionResult> Book(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount >= 3)
            {
                TempData["Error"] = "You cannot have more than 3 upcoming trips booked at the same time.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (!trip.IsVisible)
            {
                TempData["Error"] = "This trip is not available for booking.";
                return RedirectToAction("Index", "Trip");
            }

            if (trip.AvailableRooms <= 0)
            {
                TempData["Error"] = "This trip is fully booked. You can join the waiting list.";
                return RedirectToAction("JoinWaitingList", new { id });
            }

            // TURN ENFORCEMENT - Check if someone has priority (24 hour window)
            var priorityCheckResult = await CheckWaitingListPriority(id, userId!);
            if (!priorityCheckResult.CanProceed)
            {
                TempData["Error"] = priorityCheckResult.Message;
                return RedirectToAction("Details", "Trip", new { id });
            }

            if (trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = "This trip has already started or ended.";
                return RedirectToAction("Index", "Trip");
            }

            if (trip.LastBookingDate.HasValue && DateTime.Now > trip.LastBookingDate.Value)
            {
                TempData["Error"] = "The booking period for this trip has ended.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user?.DateOfBirth != null)
            {
                var userAge = (int)((DateTime.Now - user.DateOfBirth.Value).TotalDays / 365.25);
                if (trip.MinimumAge.HasValue && userAge < trip.MinimumAge.Value)
                {
                    TempData["Error"] = $"You must be at least {trip.MinimumAge} years old to book this trip.";
                    return RedirectToAction("Details", "Trip", new { id });
                }
                if (trip.MaximumAge.HasValue && userAge > trip.MaximumAge.Value)
                {
                    TempData["Error"] = $"This trip is only available for guests up to {trip.MaximumAge} years old.";
                    return RedirectToAction("Details", "Trip", new { id });
                }
            }

            var viewModel = new CreateBookingViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                PricePerPerson = trip.Price,
                OriginalPrice = trip.OriginalPrice,
                IsOnSale = trip.IsOnSale,
                AvailableRooms = trip.AvailableRooms,
                MainImageUrl = trip.MainImageUrl,
                PackageType = trip.PackageType,
                MinimumAge = trip.MinimumAge,
                MaximumAge = trip.MaximumAge,
                NumberOfRooms = 1
            };

            return View(viewModel);
        }

        // POST: /Booking/Book
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Book(CreateBookingViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount >= 3)
            {
                TempData["Error"] = "You cannot have more than 3 upcoming trips booked at the same time.";
                return RedirectToAction("Details", "Trip", new { id = model.TripId });
            }

            var trip = await _context.Trips.FindAsync(model.TripId);

            if (trip == null)
            {
                return NotFound();
            }

            // TURN ENFORCEMENT - Check again in POST
            var priorityCheckResult = await CheckWaitingListPriority(model.TripId, userId!);
            if (!priorityCheckResult.CanProceed)
            {
                TempData["Error"] = priorityCheckResult.Message;
                return RedirectToAction("Details", "Trip", new { id = model.TripId });
            }

            if (trip.AvailableRooms < model.NumberOfRooms)
            {
                TempData["Error"] = $"Only {trip.AvailableRooms} rooms are available. Please try again with fewer rooms.";
                return RedirectToAction("Book", new { id = model.TripId });
            }

            if (!ModelState.IsValid)
            {
                model.PackageName = trip.PackageName;
                model.Destination = trip.Destination;
                model.Country = trip.Country;
                model.StartDate = trip.StartDate;
                model.EndDate = trip.EndDate;
                model.PricePerPerson = trip.Price;
                model.AvailableRooms = trip.AvailableRooms;
                model.MainImageUrl = trip.MainImageUrl;
                return View(model);
            }

            // Simply redirect to Cart/Checkout - no booking created here
            // The booking will be created during payment in CartController
            return RedirectToAction("Checkout", "Cart", new { tripId = model.TripId, rooms = model.NumberOfRooms });
        }

        // GET: /Booking/Confirmation
        public async Task<IActionResult> Confirmation(int? id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            TempData.Keep("Success");
            TempData.Keep("Error");
            TempData.Keep("Info");
            TempData.Keep("ConfirmedBookingIds");
            TempData.Keep("ConfirmedBookingsCount");

            if (id.HasValue)
            {
                var booking = await _context.Bookings
                    .Include(b => b.Trip)
                    .FirstOrDefaultAsync(b => b.BookingId == id.Value && b.UserId == userId);

                if (booking == null)
                    return NotFound();

                var viewModel = new BookingConfirmationViewModel
                {
                    BookingId = booking.BookingId,
                    PackageName = booking.Trip?.PackageName ?? "",
                    Destination = booking.Trip?.Destination ?? "",
                    Country = booking.Trip?.Country ?? "",
                    StartDate = booking.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = booking.Trip?.EndDate ?? DateTime.MinValue,
                    NumberOfRooms = booking.NumberOfRooms,
                    TotalPrice = booking.TotalPrice,
                    BookingDate = booking.BookingDate,
                    IsPaid = booking.IsPaid,
                    MainImageUrl = booking.Trip?.MainImageUrl
                };

                ViewBag.IsCartConfirmation = false;
                return View(viewModel);
            }

            var idsCsv = TempData.Peek("ConfirmedBookingIds") as string;
            var countObj = TempData.Peek("ConfirmedBookingsCount");

            if (string.IsNullOrWhiteSpace(idsCsv) && countObj == null)
                return RedirectToAction("Index", "Home");

            ViewBag.IsCartConfirmation = true;
            ViewBag.ConfirmedBookingsCount = countObj ?? idsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;

            return View(model: null);
        }

        // GET: /Booking/Cancel/5
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking cannot be cancelled.";
                return RedirectToAction("MyBookings");
            }

            if (booking.Trip != null)
            {
                var daysUntilTrip = (booking.Trip.StartDate - DateTime.Now).Days;
                if (daysUntilTrip < booking.Trip.CancellationDaysLimit)
                {
                    TempData["Error"] = $"Cancellation is only allowed up to {booking.Trip.CancellationDaysLimit} days before the trip.";
                    return RedirectToAction("MyBookings");
                }
            }

            var viewModel = new CancelBookingViewModel
            {
                BookingId = booking.BookingId,
                PackageName = booking.Trip?.PackageName ?? "",
                Destination = booking.Trip?.Destination ?? "",
                StartDate = booking.Trip?.StartDate ?? DateTime.MinValue,
                EndDate = booking.Trip?.EndDate ?? DateTime.MinValue,
                NumberOfRooms = booking.NumberOfRooms,
                TotalPrice = booking.TotalPrice,
                IsPaid = booking.IsPaid
            };

            return View(viewModel);
        }

        // POST: /Booking/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, CancelBookingViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking cannot be cancelled.";
                return RedirectToAction("MyBookings");
            }

            if (booking.Trip != null)
            {
                var daysUntilTrip = (booking.Trip.StartDate - DateTime.Now).Days;
                if (daysUntilTrip < booking.Trip.CancellationDaysLimit)
                {
                    TempData["Error"] = $"Cancellation is only allowed up to {booking.Trip.CancellationDaysLimit} days before the trip.";
                    return RedirectToAction("MyBookings");
                }
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                booking.Status = BookingStatus.Cancelled;
                booking.CancellationDate = DateTime.Now;
                booking.CancellationReason = model.CancellationReason;
                booking.UpdatedAt = DateTime.Now;

                if (booking.Trip != null)
                {
                    booking.Trip.AvailableRooms += booking.NumberOfRooms;
                    booking.Trip.UpdatedAt = DateTime.Now;

                    // Handle waiting list - notify first eligible person
                    await ProcessWaitingListAfterCancellation(booking.TripId, booking.Trip.AvailableRooms);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = "Your booking has been cancelled successfully.";

                if (booking.IsPaid)
                {
                    TempData["Success"] += " A refund will be processed within 5-7 business days.";
                }

                return RedirectToAction("MyBookings");
            }
            catch
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "An error occurred while cancelling your booking. Please try again.";
                return RedirectToAction("MyBookings");
            }
        }

        // GET: /Booking/JoinWaitingList/5
        public async Task<IActionResult> JoinWaitingList(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existingEntry = await _context.WaitingListEntries
                .FirstOrDefaultAsync(w => w.UserId == userId && w.TripId == id &&
                                          (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

            if (existingEntry != null)
            {
                TempData["Info"] = "You are already on the waiting list for this trip.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (trip.AvailableRooms > 0)
            {
                return RedirectToAction("Book", new { id });
            }

            var currentWaitingCount = await _context.WaitingListEntries
                .CountAsync(w => w.TripId == id && w.Status == WaitingListStatus.Waiting);

            var viewModel = new JoinWaitingListViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                Price = trip.Price,
                MainImageUrl = trip.MainImageUrl,
                CurrentWaitingCount = currentWaitingCount,
                RoomsRequested = 1
            };

            return View(viewModel);
        }

        // POST: /Booking/JoinWaitingList
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinWaitingList(JoinWaitingListViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check ALL statuses - not just Waiting/Notified
            var existingEntry = await _context.WaitingListEntries
                .FirstOrDefaultAsync(w => w.UserId == userId && w.TripId == model.TripId);

            if (existingEntry != null)
            {
                // If cancelled or expired - allow rejoining by updating the existing entry
                if (existingEntry.Status == WaitingListStatus.Cancelled ||
    existingEntry.Status == WaitingListStatus.Expired ||
    existingEntry.Status == WaitingListStatus.Booked)
                {
                    var maxPosition = await _context.WaitingListEntries
                        .Where(w => w.TripId == model.TripId &&
                                   (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified))
                        .MaxAsync(w => (int?)w.Position) ?? 0;

                    existingEntry.Position = maxPosition + 1;
                    existingEntry.RoomsRequested = model.RoomsRequested;
                    existingEntry.JoinedDate = DateTime.Now;
                    existingEntry.Status = WaitingListStatus.Waiting;
                    existingEntry.IsNotified = false;
                    existingEntry.NotificationDate = null;
                    existingEntry.NotificationExpiresAt = null;

                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"You have been added to the waiting list at position #{existingEntry.Position}.";
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }

                // Already in waiting list or booked
                TempData["Info"] = "You are already on the waiting list for this trip.";
                return RedirectToAction("Details", "Trip", new { id = model.TripId });
            }

            if (ModelState.IsValid)
            {
                var maxPosition = await _context.WaitingListEntries
                    .Where(w => w.TripId == model.TripId &&
                               (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified))
                    .MaxAsync(w => (int?)w.Position) ?? 0;

                var entry = new WaitingListEntry
                {
                    UserId = userId!,
                    TripId = model.TripId,
                    Position = maxPosition + 1,
                    RoomsRequested = model.RoomsRequested,
                    JoinedDate = DateTime.Now,
                    Status = WaitingListStatus.Waiting,
                    IsNotified = false
                };

                _context.WaitingListEntries.Add(entry);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"You have been added to the waiting list at position #{entry.Position}. We will notify you when a room becomes available.";
                return RedirectToAction("Details", "Trip", new { id = model.TripId });
            }

            return View(model);
        }

        // GET: /Booking/DownloadItinerary/5
        public async Task<IActionResult> DownloadItinerary(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null || booking.Trip == null)
            {
                return NotFound();
            }

            if (!booking.IsPaid)
            {
                TempData["Error"] = "Please complete payment to download the itinerary.";
                return RedirectToAction("MyBookings");
            }

            booking.ItineraryDownloaded = true;
            await _context.SaveChangesAsync();

            var pdfBytes = _pdfService.GenerateItinerary(booking);
            var fileName = $"Itinerary_{booking.Trip.PackageName.Replace(" ", "_")}_{booking.BookingId}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        // POST: /Booking/LeaveWaitingList/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveWaitingList(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var entry = await _context.WaitingListEntries
                .Include(e => e.Trip)
                .FirstOrDefaultAsync(e => e.WaitingListEntryId == id && e.UserId == userId &&
                    (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified));

            if (entry == null)
            {
                TempData["Error"] = "Waiting list entry not found.";
                return RedirectToAction("MyBookings");
            }

            var tripId = entry.TripId;
            var removedPosition = entry.Position;
            var wasNotified = entry.Status == WaitingListStatus.Notified;

            entry.Status = WaitingListStatus.Cancelled;

            // Advance positions for everyone after this user
            await AdvanceWaitingListPositions(tripId, removedPosition);

            await _context.SaveChangesAsync();

            // If the user who left was Notified, notify the next eligible person
            if (wasNotified)
            {
                var trip = await _context.Trips.FindAsync(tripId);
                if (trip != null && trip.AvailableRooms > 0)
                {
                    await ProcessWaitingListAfterCancellation(tripId, trip.AvailableRooms);
                }
            }

            TempData["Success"] = "You have been removed from the waiting list.";
            return RedirectToAction("MyBookings", new { showWaitingList = true });
        }

        #region Waiting List Helper Methods

        // Check if someone has priority to book (24-hour window)
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

        // Advance positions for everyone after removed position
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

        // Process waiting list after cancellation - notify first eligible person
        private async Task ProcessWaitingListAfterCancellation(int tripId, int availableRooms)
        {
            var eligibleEntry = await FindEligibleWaitingListEntry(tripId, availableRooms);

            if (eligibleEntry != null)
            {
                eligibleEntry.Status = WaitingListStatus.Notified;
                eligibleEntry.IsNotified = true;
                eligibleEntry.NotificationDate = DateTime.Now;
                eligibleEntry.NotificationExpiresAt = DateTime.Now.AddHours(24);

                await _context.SaveChangesAsync();

                // Send "Room Available" email to the first person
                await SendRoomAvailableEmail(eligibleEntry);

                // Send position update emails to everyone else
                await SendPositionUpdateEmails(tripId, eligibleEntry.WaitingListEntryId);
            }
        }

        // Find the first eligible person in waiting list
        private async Task<WaitingListEntry?> FindEligibleWaitingListEntry(int tripId, int availableRooms)
        {
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                if (entry.RoomsRequested > availableRooms)
                {
                    continue;
                }

                var activeBookingsCount = await _context.Bookings
                    .CountAsync(b => b.UserId == entry.UserId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now);

                if (activeBookingsCount >= 3)
                {
                    continue;
                }

                return entry;
            }

            return null;
        }

        // Send "Room Available" email - for the first person who can book
        private async Task SendRoomAvailableEmail(WaitingListEntry entry)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var tripName = entry.Trip?.PackageName ?? "Your Trip";
                var destination = entry.Trip?.Destination ?? "";
                var country = entry.Trip?.Country ?? "";
                var tripId = entry.TripId;

                var subject = $"A spot opened for {tripName} - Book Now!";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #11998e 0%, #38ef7d 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Your Turn Has Come!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Great news! A spot has opened up for the trip you were waiting for:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #11998e;'>
                            <h2 style='color: #11998e; margin-top: 0;'>{tripName}</h2>
                            <p><strong>Destination:</strong> {destination}, {country}</p>
                            <p><strong>Rooms Requested:</strong> {entry.RoomsRequested}</p>
                        </div>
                        
                        <div style='background: #fff3cd; padding: 15px; border-radius: 10px; margin: 20px 0;'>
                            <p style='margin: 0; color: #856404;'>
                                <strong>Important:</strong> You have <strong>24 hours</strong> to complete your booking before the spot is offered to the next person in line.
                            </p>
                        </div>
                        
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='https://localhost:7232/Booking/Book/{tripId}' 
                               style='background: #11998e; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; font-size: 18px; display: inline-block;'>
                                Book Now
                            </a>
                        </div>
                        
                        <p>Don't miss this opportunity!</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>{DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send room available email: {ex.Message}");
            }
        }

        // Send position update emails to everyone except the notified person
        private async Task SendPositionUpdateEmails(int tripId, int excludeEntryId)
        {
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                await SendPositionUpdateEmail(entry);
            }
        }

        // Send position update email
        private async Task SendPositionUpdateEmail(WaitingListEntry entry)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var tripName = entry.Trip?.PackageName ?? "Your Trip";
                var destination = entry.Trip?.Destination ?? "";

                var subject = $"Waiting List Update - {tripName}";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Position Update!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Good news! You've moved up in the waiting list for:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #667eea;'>
                            <h2 style='color: #667eea; margin-top: 0;'>{tripName}</h2>
                            <p><strong>Destination:</strong> {destination}</p>
                            <p><strong>Your position in line:</strong> <span style='font-size: 24px; font-weight: bold; color: #667eea;'>#{entry.Position}</span></p>
                        </div>
                        
                        <p>We'll notify you as soon as a spot becomes available!</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>{DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send position update email: {ex.Message}");
            }
        }

        #endregion
    }
}