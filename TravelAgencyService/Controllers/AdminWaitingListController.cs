using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminWaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AdminWaitingListController> _logger;

        public AdminWaitingListController(
            ApplicationDbContext context,
            IEmailSender emailSender,
            ILogger<AdminWaitingListController> logger)
        {
            _context = context;
            _emailSender = emailSender;
            _logger = logger;
        }

        // GET: /AdminWaitingList/Index
        public async Task<IActionResult> Index()
        {
            var trips = await _context.Trips
                .Select(t => new
                {
                    t.TripId,
                    t.PackageName,
                    t.AvailableRooms,
                    // Count both Waiting AND Notified entries
                    WaitingCount = _context.WaitingListEntries
                        .Count(e => e.TripId == t.TripId &&
                                   (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified))
                })
                .Where(x => x.WaitingCount > 0)
                .OrderByDescending(x => x.WaitingCount)
                .ToListAsync();

            return View(trips);
        }

        // GET: /AdminWaitingList/Details/5
        public async Task<IActionResult> Details(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null) return NotFound();

            // Include both Waiting AND Notified entries
            var entries = await _context.WaitingListEntries
                .Include(e => e.User)
                .Where(e => e.TripId == tripId &&
                           (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified))
                .OrderBy(e => e.JoinedDate)
                .ToListAsync();

            ViewBag.TripName = trip.PackageName;
            ViewBag.AvailableRooms = trip.AvailableRooms;
            ViewBag.TripStartDate = trip.StartDate;

            return View(entries);
        }

        // POST: /AdminWaitingList/NotifyNext/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyNext(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null) return NotFound();

            if (trip.AvailableRooms <= 0)
            {
                TempData["Error"] = "No rooms available to notify.";
                return RedirectToAction(nameof(Details), new { tripId });
            }

            // Check if someone is already notified (still in booking window)
            var alreadyNotified = await _context.WaitingListEntries
                .AnyAsync(e => e.TripId == tripId &&
                              e.Status == WaitingListStatus.Notified &&
                              e.NotificationExpiresAt > DateTime.Now);

            if (alreadyNotified)
            {
                TempData["Error"] = "Someone is already notified and has time to book. Wait for their window to expire.";
                return RedirectToAction(nameof(Details), new { tripId });
            }

            // Get first in FIFO queue
            var next = await _context.WaitingListEntries
                .Include(e => e.User)
                .Where(e => e.TripId == tripId && e.Status == WaitingListStatus.Waiting)
                .OrderBy(e => e.JoinedDate)
                .FirstOrDefaultAsync();

            if (next == null)
            {
                TempData["Error"] = "No one is waiting.";
                return RedirectToAction(nameof(Details), new { tripId });
            }

            // Check if user can actually book
            if (next.RoomsRequested > trip.AvailableRooms)
            {
                TempData["Error"] = $"User requested {next.RoomsRequested} rooms but only {trip.AvailableRooms} available. Consider notifying manually after more rooms free up.";
                return RedirectToAction(nameof(Details), new { tripId });
            }

            // Calculate dynamic booking window
            int daysUntilTrip = (trip.StartDate.Date - DateTime.Now.Date).Days;
            if (daysUntilTrip < 0) daysUntilTrip = 0;

            var totalWaiting = await _context.WaitingListEntries
                .CountAsync(e => e.TripId == tripId &&
                                (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified));

            int bookingWindowHours = WaitingListController.CalculateBookingWindowHours(daysUntilTrip, totalWaiting);

            // Mark as notified
            next.Status = WaitingListStatus.Notified;
            next.IsNotified = true;
            next.NotificationDate = DateTime.Now;
            next.NotificationExpiresAt = DateTime.Now.AddHours(bookingWindowHours);

            await _context.SaveChangesAsync();

            // Send email notification
            await SendRoomAvailableEmail(next, trip, bookingWindowHours);

            // Send position update emails to others
            await SendPositionUpdateEmails(tripId, next.WaitingListEntryId);

            var windowText = WaitingListController.FormatBookingWindow(bookingWindowHours);
            TempData["Success"] = $"User '{next.User?.Email}' has been notified! They have {windowText} to book.";

            _logger.LogInformation(
                "Admin notified user {UserId} for trip {TripId}. Booking window: {Hours} hours.",
                next.UserId, tripId, bookingWindowHours);

            return RedirectToAction(nameof(Details), new { tripId });
        }

        // POST: /AdminWaitingList/ExpireNotification/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExpireNotification(int entryId)
        {
            var entry = await _context.WaitingListEntries
                .Include(e => e.Trip)
                .FirstOrDefaultAsync(e => e.WaitingListEntryId == entryId);

            if (entry == null) return NotFound();

            if (entry.Status != WaitingListStatus.Notified)
            {
                TempData["Error"] = "This entry is not in Notified status.";
                return RedirectToAction(nameof(Details), new { tripId = entry.TripId });
            }

            entry.Status = WaitingListStatus.Expired;
            entry.IsNotified = false;

            // Advance positions
            var entriesToAdvance = await _context.WaitingListEntries
                .Where(w => w.TripId == entry.TripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.Position > entry.Position)
                .ToListAsync();

            foreach (var e in entriesToAdvance)
            {
                e.Position--;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Notification has been expired manually.";
            return RedirectToAction(nameof(Details), new { tripId = entry.TripId });
        }

        #region Email Methods

        private async Task SendRoomAvailableEmail(WaitingListEntry entry, Trip trip, int bookingWindowHours)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var bookingWindowText = WaitingListController.FormatBookingWindow(bookingWindowHours);

                var subject = $"A spot opened for {trip.PackageName} - Book Now!";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #11998e 0%, #38ef7d 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Your Turn Has Come!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Great news! A spot has opened up for the trip you were waiting for:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #11998e;'>
                            <h2 style='color: #11998e; margin-top: 0;'>{trip.PackageName}</h2>
                            <p><strong>Destination:</strong> {trip.Destination}, {trip.Country}</p>
                            <p><strong>Dates:</strong> {trip.StartDate:MMM dd} - {trip.EndDate:MMM dd, yyyy}</p>
                            <p><strong>Rooms Requested:</strong> {entry.RoomsRequested}</p>
                        </div>
                        
                        <div style='background: #fff3cd; padding: 15px; border-radius: 10px; margin: 20px 0;'>
                            <p style='margin: 0; color: #856404;'>
                                <strong>Important:</strong> You have <strong>{bookingWindowText}</strong> to complete your booking before the spot is offered to the next person in line.
                            </p>
                        </div>
                        
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='https://localhost:7232/Booking/Book/{trip.TripId}' 
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
                        <p style='margin: 0;'>&copy; {DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
                _logger.LogInformation("Sent room available email to {Email}", userEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send room available email to {Email}", entry.User?.Email);
            }
        }

        private async Task SendPositionUpdateEmails(int tripId, int excludeEntryId)
        {
            var entries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in entries)
            {
                try
                {
                    var userEmail = entry.User?.Email;
                    if (string.IsNullOrEmpty(userEmail)) continue;

                    var userName = entry.User?.FirstName ?? "Traveler";
                    var tripName = entry.Trip?.PackageName ?? "Your Trip";

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
                                <p><strong>Your position in line:</strong> <span style='font-size: 24px; font-weight: bold; color: #667eea;'>#{entry.Position}</span></p>
                            </div>
                            
                            <p>We'll notify you as soon as a spot becomes available!</p>
                            
                            <p style='color: #888; font-size: 14px;'>
                                Best regards,<br>
                                Travel Agency Team
                            </p>
                        </div>
                    </div>";

                    await _emailSender.SendAsync(userEmail, subject, htmlBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send position update email to {Email}", entry.User?.Email);
                }
            }
        }

        #endregion
    }
}