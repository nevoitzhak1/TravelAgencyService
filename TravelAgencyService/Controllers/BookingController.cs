using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PdfService _pdfService;

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, PdfService pdfService)
        {
            _context = context;
            _userManager = userManager;
            _pdfService = pdfService;
        }

        // GET: /Booking/MyBookings
        public async Task<IActionResult> MyBookings(bool showPast = false, bool showCancelled = false)
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

                if (booking.Trip?.EndDate >= DateTime.Now && booking.Status != BookingStatus.Cancelled)
                {
                    upcomingBookings.Add(viewModel);
                }
                else
                {
                    pastBookings.Add(viewModel);
                }
            }

            var model = new MyBookingsViewModel
            {
                UpcomingBookings = upcomingBookings,
                PastBookings = pastBookings,
                TotalBookings = bookings.Count,
                ShowPastBookings = showPast,
                ShowCancelledBookings = showCancelled
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

            if (trip.AvailableRooms < model.NumberOfRooms)
            {
                TempData["Error"] = $"Only {trip.AvailableRooms} rooms are available. Please try again with fewer rooms.";
                return RedirectToAction("Book", new { id = model.TripId });
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var tripToUpdate = await _context.Trips
                        .FirstOrDefaultAsync(t => t.TripId == model.TripId);

                    if (tripToUpdate == null || tripToUpdate.AvailableRooms < model.NumberOfRooms)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "Sorry, the rooms are no longer available. Someone else booked them.";
                        return RedirectToAction("Details", "Trip", new { id = model.TripId });
                    }

                    var booking = new Booking
                    {
                        UserId = userId!,
                        TripId = model.TripId,
                        NumberOfRooms = model.NumberOfRooms,
                        TotalPrice = trip.Price * model.NumberOfRooms,
                        BookingDate = DateTime.Now,
                        Status = BookingStatus.Pending,
                        IsPaid = false,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    tripToUpdate.AvailableRooms -= model.NumberOfRooms;
                    tripToUpdate.TimesBooked++;
                    tripToUpdate.UpdatedAt = DateTime.Now;

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return RedirectToAction("Checkout", "Payment", new { bookingId = booking.BookingId });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "An error occurred while processing your booking. Please try again.";
                    return RedirectToAction("Book", new { id = model.TripId });
                }
            }

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

        // GET: /Booking/Confirmation/5
        public async Task<IActionResult> Confirmation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null)
            {
                return NotFound();
            }

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

            return View(viewModel);
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

                    var firstInWaitingList = await _context.WaitingListEntries
                        .Include(w => w.User)
                        .Where(w => w.TripId == booking.TripId && w.Status == WaitingListStatus.Waiting)
                        .OrderBy(w => w.Position)
                        .FirstOrDefaultAsync();

                    if (firstInWaitingList != null && firstInWaitingList.RoomsRequested <= booking.Trip.AvailableRooms)
                    {
                        firstInWaitingList.Status = WaitingListStatus.Notified;
                        firstInWaitingList.IsNotified = true;
                        firstInWaitingList.NotificationDate = DateTime.Now;
                        firstInWaitingList.NotificationExpiresAt = DateTime.Now.AddHours(24);
                    }
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

            if (ModelState.IsValid)
            {
                var maxPosition = await _context.WaitingListEntries
                    .Where(w => w.TripId == model.TripId)
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

        // GET: /Booking/DownloadItinerary/5 - PDF VERSION
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

            // Mark as downloaded
            booking.ItineraryDownloaded = true;
            await _context.SaveChangesAsync();

            // Generate PDF
            var pdfBytes = _pdfService.GenerateItinerary(booking);
            var fileName = $"Itinerary_{booking.Trip.PackageName.Replace(" ", "_")}_{booking.BookingId}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
