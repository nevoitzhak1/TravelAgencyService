using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Services
{
    /// <summary>
    /// Background service that periodically checks for expired waiting list notifications
    /// and advances the queue to the next eligible person.
    /// </summary>
    public class WaitingListExpirationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WaitingListExpirationService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public WaitingListExpirationService(
            IServiceProvider serviceProvider,
            ILogger<WaitingListExpirationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WaitingListExpirationService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessExpiredNotifications(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing expired waiting list notifications.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WaitingListExpirationService stopped.");
        }

        private async Task ProcessExpiredNotifications(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            // Find all expired notifications
            var expiredEntries = await context.WaitingListEntries
                .Include(w => w.Trip)
                .Include(w => w.User)
                .Where(w => w.Status == WaitingListStatus.Notified &&
                            w.NotificationExpiresAt.HasValue &&
                            w.NotificationExpiresAt.Value < DateTime.Now)
                .ToListAsync(stoppingToken);

            if (!expiredEntries.Any())
            {
                return;
            }

            _logger.LogInformation("Found {Count} expired waiting list notifications.", expiredEntries.Count);

            foreach (var entry in expiredEntries)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // Mark as expired
                    entry.Status = WaitingListStatus.Expired;
                    entry.IsNotified = false;

                    _logger.LogInformation(
                        "Marked waiting list entry {EntryId} as Expired for user {UserId} on trip {TripId}.",
                        entry.WaitingListEntryId, entry.UserId, entry.TripId);

                    // Send "Opportunity Expired" email to the user
                    await SendExpiredNotificationEmail(entry, emailSender);

                    // Advance positions for everyone after this user
                    var entriesToAdvance = await context.WaitingListEntries
                        .Where(w => w.TripId == entry.TripId &&
                                    w.Status == WaitingListStatus.Waiting &&
                                    w.Position > entry.Position)
                        .ToListAsync(stoppingToken);

                    foreach (var e in entriesToAdvance)
                    {
                        e.Position--;
                    }

                    await context.SaveChangesAsync(stoppingToken);

                    // Notify next eligible person if rooms are available
                    if (entry.Trip != null && entry.Trip.AvailableRooms > 0)
                    {
                        await NotifyNextEligiblePerson(context, emailSender, entry.TripId, entry.Trip, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing expired entry {EntryId}.",
                        entry.WaitingListEntryId);
                }
            }
        }

        private async Task NotifyNextEligiblePerson(
            ApplicationDbContext context,
            IEmailSender emailSender,
            int tripId,
            Trip trip,
            CancellationToken stoppingToken)
        {
            // Find next eligible person (FIFO order, checking constraints)
            var waitingEntries = await context.WaitingListEntries
                .Include(w => w.User)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync(stoppingToken);

            foreach (var entry in waitingEntries)
            {
                // Check if user requested more rooms than available
                if (entry.RoomsRequested > trip.AvailableRooms)
                {
                    continue;
                }

                // Check if user already has 3 active bookings
                var activeBookingsCount = await context.Bookings
                    .CountAsync(b => b.UserId == entry.UserId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now,
                                stoppingToken);

                if (activeBookingsCount >= 3)
                {
                    continue;
                }

                // Found eligible person - notify them
                int daysUntilTrip = (trip.StartDate.Date - DateTime.Now.Date).Days;
                if (daysUntilTrip < 0) daysUntilTrip = 0;

                var totalWaiting = await context.WaitingListEntries
                    .CountAsync(w => w.TripId == tripId &&
                                    (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified),
                                stoppingToken);

                int bookingWindowHours = CalculateBookingWindowHours(daysUntilTrip, totalWaiting);

                entry.Status = WaitingListStatus.Notified;
                entry.IsNotified = true;
                entry.NotificationDate = DateTime.Now;
                entry.NotificationExpiresAt = DateTime.Now.AddHours(bookingWindowHours);

                await context.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "Notified user {UserId} for trip {TripId}. Booking window: {Hours} hours.",
                    entry.UserId, tripId, bookingWindowHours);

                // Send notification email
                await SendRoomAvailableEmail(entry, trip, bookingWindowHours, emailSender);

                // Send position update emails to remaining waiters
                await SendPositionUpdateEmails(context, emailSender, tripId, entry.WaitingListEntryId, stoppingToken);

                break; // Only notify one person
            }
        }

        private static int CalculateBookingWindowHours(int daysUntilTrip, int peopleInQueue)
        {
            const int MIN_HOURS = 2;
            const int MAX_HOURS = 48;

            if (daysUntilTrip <= 0) return MIN_HOURS;
            if (peopleInQueue < 0) peopleInQueue = 0;

            int totalHoursAvailable = daysUntilTrip * 24;
            int divisor = peopleInQueue + 1;
            int calculatedHours = totalHoursAvailable / divisor;

            return Math.Clamp(calculatedHours, MIN_HOURS, MAX_HOURS);
        }

        private async Task SendExpiredNotificationEmail(WaitingListEntry entry, IEmailSender emailSender)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var tripName = entry.Trip?.PackageName ?? "the trip";

                var subject = $"Booking Window Expired - {tripName}";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #ff6b6b 0%, #ee5a5a 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Booking Window Expired</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Unfortunately, your booking window for <strong>{tripName}</strong> has expired.</p>
                        
                        <p>The spot has been offered to the next person in the waiting list.</p>
                        
                        <p>If you're still interested in this trip, you can rejoin the waiting list from the trip page.</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                </div>";

                await emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send expired notification email.");
            }
        }

        private async Task SendRoomAvailableEmail(
            WaitingListEntry entry,
            Trip trip,
            int bookingWindowHours,
            IEmailSender emailSender)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var bookingWindowText = FormatBookingWindow(bookingWindowHours);

                var subject = $"A spot opened for {trip.PackageName} - Book Now!";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #11998e 0%, #38ef7d 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Your Turn Has Come!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Great news! A spot has opened up for:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #11998e;'>
                            <h2 style='color: #11998e; margin-top: 0;'>{trip.PackageName}</h2>
                            <p><strong>Destination:</strong> {trip.Destination}, {trip.Country}</p>
                            <p><strong>Rooms Requested:</strong> {entry.RoomsRequested}</p>
                        </div>
                        
                        <div style='background: #fff3cd; padding: 15px; border-radius: 10px; margin: 20px 0;'>
                            <p style='margin: 0; color: #856404;'>
                                <strong>Important:</strong> You have <strong>{bookingWindowText}</strong> to complete your booking.
                            </p>
                        </div>
                        
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='https://localhost:7232/Booking/Book/{trip.TripId}' 
                               style='background: #11998e; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; font-size: 18px;'>
                                Book Now
                            </a>
                        </div>
                    </div>
                </div>";

                await emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send room available email.");
            }
        }

        private async Task SendPositionUpdateEmails(
            ApplicationDbContext context,
            IEmailSender emailSender,
            int tripId,
            int excludeEntryId,
            CancellationToken stoppingToken)
        {
            var entries = await context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .ToListAsync(stoppingToken);

            foreach (var entry in entries)
            {
                try
                {
                    var userEmail = entry.User?.Email;
                    if (string.IsNullOrEmpty(userEmail)) continue;

                    var subject = $"Waiting List Update - {entry.Trip?.PackageName}";
                    var htmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center;'>
                            <h1 style='color: white; margin: 0;'>Position Update!</h1>
                        </div>
                        <div style='padding: 30px; background: #f9f9f9;'>
                            <p>Hi {entry.User?.FirstName ?? "Traveler"},</p>
                            <p>You've moved up in the waiting list for <strong>{entry.Trip?.PackageName}</strong>!</p>
                            <p>Your new position: <strong>#{entry.Position}</strong></p>
                        </div>
                    </div>";

                    await emailSender.SendAsync(userEmail, subject, htmlBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send position update email.");
                }
            }
        }

        private static string FormatBookingWindow(int hours)
        {
            if (hours < 24) return $"{hours} hours";
            int days = hours / 24;
            int remainingHours = hours % 24;
            if (remainingHours == 0) return days == 1 ? "1 day" : $"{days} days";
            return days == 1 ? $"1 day and {remainingHours} hours" : $"{days} days and {remainingHours} hours";
        }
    }
}