using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class WaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        // Constants for booking window calculation
        private const int MIN_BOOKING_HOURS = 2;
        private const int MAX_BOOKING_HOURS = 48;

        public WaitingListController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        #region Booking Window & ETA Calculation

        /// <summary>
        /// Calculates how many hours a user has to complete their booking.
        /// Based on days until trip and number of people in queue.
        /// Formula: (daysUntilTrip * 24) / (peopleInQueue + 1)
        /// Min: 2 hours, Max: 48 hours
        /// </summary>
        public static int CalculateBookingWindowHours(int daysUntilTrip, int peopleInQueue)
        {
            if (daysUntilTrip <= 0) return MIN_BOOKING_HOURS;
            if (peopleInQueue < 0) peopleInQueue = 0;

            int totalHoursAvailable = daysUntilTrip * 24;
            int divisor = peopleInQueue + 1;

            int calculatedHours = totalHoursAvailable / divisor;

            // Apply min/max bounds
            if (calculatedHours < MIN_BOOKING_HOURS) return MIN_BOOKING_HOURS;
            if (calculatedHours > MAX_BOOKING_HOURS) return MAX_BOOKING_HOURS;

            return calculatedHours;
        }

        /// <summary>
        /// Calculates estimated time until a room becomes available for a specific position in queue.
        /// Returns a user-friendly string.
        /// </summary>
        public static string CalculateEtaText(int position, int daysUntilTrip, int peopleInQueue)
        {
            if (position <= 0)
            {
                return "You're not currently in the waiting list.";
            }

            if (position == 1)
            {
                return "You're next in line! You'll be notified immediately when a room becomes available.";
            }

            int bookingWindowHours = CalculateBookingWindowHours(daysUntilTrip, peopleInQueue);
            int maxWaitHours = (position - 1) * bookingWindowHours;
            int hoursUntilTrip = daysUntilTrip * 24;

            // If there's not enough time for everyone ahead in queue
            if (maxWaitHours >= hoursUntilTrip)
            {
                return $"Position #{position} in queue. Note: Limited time remaining before trip departure ({daysUntilTrip} days).";
            }

            // Convert to friendly format
            if (maxWaitHours < 24)
            {
                return $"Position #{position} in queue. Estimated wait: up to {maxWaitHours} hours.";
            }
            else
            {
                int days = maxWaitHours / 24;
                if (days == 1)
                {
                    return $"Position #{position} in queue. Estimated wait: up to 1 day.";
                }
                else if (days < 7)
                {
                    return $"Position #{position} in queue. Estimated wait: up to {days} days.";
                }
                else
                {
                    int weeks = days / 7;
                    return $"Position #{position} in queue. Estimated wait: up to {days} days (~{weeks} week{(weeks > 1 ? "s" : "")}).";
                }
            }
        }

        /// <summary>
        /// Formats booking window hours into a user-friendly string.
        /// </summary>
        public static string FormatBookingWindow(int hours)
        {
            if (hours < 24)
            {
                return $"{hours} hours";
            }
            else
            {
                int days = hours / 24;
                int remainingHours = hours % 24;
                if (remainingHours == 0)
                {
                    return days == 1 ? "1 day" : $"{days} days";
                }
                else
                {
                    return days == 1 ? $"1 day and {remainingHours} hours" : $"{days} days and {remainingHours} hours";
                }
            }
        }

        #endregion

        // GET: /WaitingList/Index
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var myWaitingLists = await _context.WaitingListEntries
                .Include(w => w.Trip)
                .Where(w => w.UserId == user.Id &&
                           (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified))
                .OrderBy(w => w.JoinedDate)
                .Select(w => new
                {
                    w.WaitingListEntryId,
                    w.TripId,
                    TripName = w.Trip != null ? w.Trip.PackageName : "Unknown",
                    w.Position,
                    w.JoinedDate,
                    w.RoomsRequested,
                    w.Status,
                    w.NotificationExpiresAt
                })
                .ToListAsync();

            return View(myWaitingLists);
        }

        // GET: /WaitingList/Status/5
        public async Task<IActionResult> Status(int tripId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null) return NotFound();

            var waitingQuery = _context.WaitingListEntries
                .Where(e => e.TripId == tripId &&
                           (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified));

            var totalWaiting = await waitingQuery.CountAsync();

            var myEntry = await _context.WaitingListEntries
                .Where(e => e.UserId == user.Id &&
                           e.TripId == tripId &&
                           (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified))
                .FirstOrDefaultAsync();

            int? myPosition = null;
            bool isNotified = false;
            DateTime? expiresAt = null;

            if (myEntry != null)
            {
                myPosition = myEntry.Position;
                isNotified = myEntry.Status == WaitingListStatus.Notified;
                expiresAt = myEntry.NotificationExpiresAt;
            }

            // Calculate days until trip
            int daysUntilTrip = (trip.StartDate.Date - DateTime.Now.Date).Days;
            if (daysUntilTrip < 0) daysUntilTrip = 0;

            // Calculate booking window for display
            int bookingWindowHours = CalculateBookingWindowHours(daysUntilTrip, totalWaiting);

            string etaText;
            if (trip.AvailableRooms > 0)
            {
                etaText = "Rooms are available now — no need for waiting list.";
            }
            else if (totalWaiting == 0)
            {
                etaText = $"If you join now, you'll be first in line. You'll have {FormatBookingWindow(bookingWindowHours)} to book when notified.";
            }
            else if (myPosition.HasValue)
            {
                etaText = CalculateEtaText(myPosition.Value, daysUntilTrip, totalWaiting);
                if (myPosition.Value > 1)
                {
                    etaText += $" You'll have {FormatBookingWindow(bookingWindowHours)} to book when it's your turn.";
                }
            }
            else
            {
                // User not in queue, show what they'd get if they join
                int potentialPosition = totalWaiting + 1;
                etaText = CalculateEtaText(potentialPosition, daysUntilTrip, totalWaiting + 1);
                etaText += $" You'll have {FormatBookingWindow(CalculateBookingWindowHours(daysUntilTrip, totalWaiting + 1))} to book when it's your turn.";
            }

            var vm = new WaitingListStatusViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                AvailableRooms = trip.AvailableRooms,
                TotalWaiting = totalWaiting,
                MyPosition = myPosition,
                EtaText = etaText,
                CanJoin = (trip.AvailableRooms == 0) && (myEntry == null),
                CanLeave = (myEntry != null),
                Message = trip.AvailableRooms == 0
                    ? "This trip is fully booked. You can join the waiting list."
                    : "Rooms are available — no need for waiting list.",
                IsNotified = isNotified,
                NotificationExpiresAt = expiresAt
            };

            return View(vm);
        }

        // POST: /WaitingList/Join
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int tripId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null) return NotFound();

            if (trip.AvailableRooms > 0)
            {
                TempData["Error"] = "Cannot join waiting list when rooms are available.";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                TempData["Error"] = "Email is required to join the waiting list.";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            // Check if user already has an entry for this trip
            var existingEntry = await _context.WaitingListEntries
                .FirstOrDefaultAsync(e => e.TripId == tripId && e.UserId == user.Id);

            if (existingEntry != null)
            {
                // If already waiting or notified - can't join again
                if (existingEntry.Status == WaitingListStatus.Waiting ||
                    existingEntry.Status == WaitingListStatus.Notified)
                {
                    TempData["Error"] = "You're already in the waiting list.";
                    return RedirectToAction(nameof(Status), new { tripId });
                }

                // If cancelled, expired, or booked - allow rejoining by updating existing entry
                var maxPosition = await _context.WaitingListEntries
                    .Where(e => e.TripId == tripId &&
                               (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified))
                    .MaxAsync(e => (int?)e.Position) ?? 0;

                existingEntry.Position = maxPosition + 1;
                existingEntry.JoinedDate = DateTime.Now;
                existingEntry.Status = WaitingListStatus.Waiting;
                existingEntry.IsNotified = false;
                existingEntry.NotificationDate = null;
                existingEntry.NotificationExpiresAt = null;
                existingEntry.RoomsRequested = 1;

                await _context.SaveChangesAsync();

                TempData["Success"] = $"You've been added to the waiting list at position #{existingEntry.Position}!";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            // No existing entry - create new one
            var newMaxPosition = await _context.WaitingListEntries
                .Where(e => e.TripId == tripId &&
                           (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified))
                .MaxAsync(e => (int?)e.Position) ?? 0;

            _context.WaitingListEntries.Add(new WaitingListEntry
            {
                TripId = tripId,
                UserId = user.Id,
                JoinedDate = DateTime.Now,
                Position = newMaxPosition + 1,
                Status = WaitingListStatus.Waiting,
                RoomsRequested = 1
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "You've been added to the waiting list!";
            return RedirectToAction(nameof(Status), new { tripId });
        }

        // POST: /WaitingList/Leave
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int tripId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var entry = await _context.WaitingListEntries
                .Include(e => e.Trip)
                .FirstOrDefaultAsync(e =>
                    e.TripId == tripId &&
                    e.UserId == user.Id &&
                    (e.Status == WaitingListStatus.Waiting || e.Status == WaitingListStatus.Notified));

            if (entry != null)
            {
                var removedPosition = entry.Position;
                var wasNotified = entry.Status == WaitingListStatus.Notified;

                entry.Status = WaitingListStatus.Cancelled;

                // Advance positions for everyone after this user
                var entriesToAdvance = await _context.WaitingListEntries
                    .Where(w => w.TripId == tripId &&
                                w.Status == WaitingListStatus.Waiting &&
                                w.Position > removedPosition)
                    .ToListAsync();

                foreach (var e in entriesToAdvance)
                {
                    e.Position--;
                }

                await _context.SaveChangesAsync();

                // If the user who left was Notified, notify the next eligible person
                if (wasNotified)
                {
                    var trip = await _context.Trips.FindAsync(tripId);
                    if (trip != null && trip.AvailableRooms > 0)
                    {
                        await ProcessWaitingListAfterLeave(tripId, trip);
                    }
                }

                // Send position update emails to remaining users
                await SendPositionUpdateEmails(tripId);

                TempData["Success"] = "You've been removed from the waiting list.";
            }

            return RedirectToAction(nameof(Status), new { tripId });
        }

        #region Helper Methods

        private async Task ProcessWaitingListAfterLeave(int tripId, Trip trip)
        {
            var eligibleEntry = await FindEligibleWaitingListEntry(tripId, trip.AvailableRooms);

            if (eligibleEntry != null)
            {
                // Calculate dynamic booking window
                int daysUntilTrip = (trip.StartDate.Date - DateTime.Now.Date).Days;
                if (daysUntilTrip < 0) daysUntilTrip = 0;

                var totalWaiting = await _context.WaitingListEntries
                    .CountAsync(w => w.TripId == tripId &&
                                    (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                int bookingWindowHours = CalculateBookingWindowHours(daysUntilTrip, totalWaiting);

                eligibleEntry.Status = WaitingListStatus.Notified;
                eligibleEntry.IsNotified = true;
                eligibleEntry.NotificationDate = DateTime.Now;
                eligibleEntry.NotificationExpiresAt = DateTime.Now.AddHours(bookingWindowHours);

                await _context.SaveChangesAsync();

                await SendWaitingListNotificationEmail(eligibleEntry, bookingWindowHours);
            }
        }

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

        private async Task SendWaitingListNotificationEmail(WaitingListEntry entry, int bookingWindowHours)
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

                string bookingWindowText = FormatBookingWindow(bookingWindowHours);

                var subject = $"Good News! A spot opened for {tripName}";

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
                                <strong>Important:</strong> You have <strong>{bookingWindowText}</strong> to complete your booking before the spot is offered to the next person in line.
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
                Console.WriteLine($"Failed to send waiting list notification email: {ex.Message}");
            }
        }

        private async Task SendPositionUpdateEmails(int tripId)
        {
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                await SendPositionUpdateEmail(entry);
            }
        }

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