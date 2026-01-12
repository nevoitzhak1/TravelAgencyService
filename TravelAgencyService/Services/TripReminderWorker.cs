using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Services.Background
{
    public class TripReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TripReminderWorker> _logger;

        public TripReminderWorker(IServiceScopeFactory scopeFactory, ILogger<TripReminderWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessReminders(stoppingToken);
                    await ProcessWaitingListExpirations(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TripReminderWorker failed");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task ProcessReminders(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var today = DateTime.Now.Date;

            var rules = await db.TripReminderRules
                .Include(r => r.Trip)
                .Where(r => r.IsActive && r.Trip != null)
                .ToListAsync(ct);

            foreach (var rule in rules)
            {
                var trip = rule.Trip!;
                if (trip.StartDate.Date <= today) continue;

                DateTime dueDate = rule.OffsetUnit == ReminderOffsetUnit.Days
                    ? trip.StartDate.Date.AddDays(-rule.OffsetAmount)
                    : trip.StartDate.Date.AddMonths(-rule.OffsetAmount);

                if (dueDate != today) continue;

                var bookings = await db.Bookings
                    .Include(b => b.User)
                    .Where(b => b.TripId == trip.TripId && b.Status == BookingStatus.Confirmed)
                    .ToListAsync(ct);

                foreach (var b in bookings)
                {
                    var toEmail = b.User?.Email;
                    if (string.IsNullOrWhiteSpace(toEmail)) continue;

                    bool alreadySent = await db.TripReminderSendLogs
                        .AnyAsync(x => x.TripReminderRuleId == rule.TripReminderRuleId && x.BookingId == b.BookingId, ct);

                    if (alreadySent) continue;

                    var subject = rule.SubjectTemplate
                        ?? $"Reminder: {trip.PackageName} starts on {trip.StartDate:dd/MM/yyyy}";

                    var body = $@"
                        <div style='font-family:Arial'>
                          <h2>Trip Reminder</h2>
                          <p>Hi {b.User?.FirstName ?? "there"},</p>
                          <p>This is a reminder for your trip:</p>
                          <ul>
                            <li><b>{trip.PackageName}</b></li>
                            <li>{trip.Destination}, {trip.Country}</li>
                            <li>Start: {trip.StartDate:dd/MM/yyyy}</li>
                          </ul>
                          <p>See you soon!</p>
                        </div>";

                    await email.SendAsync(toEmail, subject, body);

                    db.TripReminderSendLogs.Add(new TripReminderSendLog
                    {
                        TripReminderRuleId = rule.TripReminderRuleId,
                        BookingId = b.BookingId,
                        ToEmail = toEmail,
                        SentAt = DateTime.Now
                    });

                    await db.SaveChangesAsync(ct);
                }
            }
        }

        private async Task ProcessWaitingListExpirations(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var expiredEntries = await db.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.Status == WaitingListStatus.Notified &&
                            w.NotificationExpiresAt != null &&
                            w.NotificationExpiresAt < DateTime.Now)
                .ToListAsync(ct);

            foreach (var expiredEntry in expiredEntries)
            {
                expiredEntry.Status = WaitingListStatus.Expired;

                var expiredPosition = expiredEntry.Position;
                var tripId = expiredEntry.TripId;

                var entriesToAdvance = await db.WaitingListEntries
                    .Where(w => w.TripId == tripId &&
                                w.Status == WaitingListStatus.Waiting &&
                                w.Position > expiredPosition)
                    .ToListAsync(ct);

                foreach (var entry in entriesToAdvance)
                {
                    entry.Position--;
                }

                await db.SaveChangesAsync(ct);

                await SendExpirationEmail(expiredEntry, email);

                var trip = await db.Trips.FindAsync(new object[] { tripId }, ct);
                if (trip != null && trip.AvailableRooms > 0)
                {
                    var nextEntry = await FindEligibleWaitingListEntry(db, tripId, trip.AvailableRooms, ct);

                    if (nextEntry != null)
                    {
                        nextEntry.Status = WaitingListStatus.Notified;
                        nextEntry.IsNotified = true;
                        nextEntry.NotificationDate = DateTime.Now;
                        nextEntry.NotificationExpiresAt = DateTime.Now.AddHours(24);

                        await db.SaveChangesAsync(ct);

                        await SendWaitingListNotificationEmail(nextEntry, email);
                        await SendPositionUpdateEmails(db, tripId, nextEntry.WaitingListEntryId, email, ct);
                    }
                }
            }
        }

        private async Task<WaitingListEntry?> FindEligibleWaitingListEntry(
            ApplicationDbContext db, int tripId, int availableRooms, CancellationToken ct)
        {
            var waitingEntries = await db.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync(ct);

            foreach (var entry in waitingEntries)
            {
                if (entry.RoomsRequested > availableRooms)
                {
                    continue;
                }

                var activeBookingsCount = await db.Bookings
                    .CountAsync(b => b.UserId == entry.UserId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now, ct);

                if (activeBookingsCount >= 3)
                {
                    continue;
                }

                return entry;
            }

            return null;
        }

        private async Task SendExpirationEmail(WaitingListEntry entry, IEmailSender email)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var tripName = entry.Trip?.PackageName ?? "Your Trip";
                var destination = entry.Trip?.Destination ?? "";

                var subject = $"Time Expired - {tripName}";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #eb3349 0%, #f45c43 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Booking Window Expired</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Unfortunately, your 24-hour booking window for the following trip has expired:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #eb3349;'>
                            <h2 style='color: #eb3349; margin-top: 0;'>{tripName}</h2>
                            <p><strong>Destination:</strong> {destination}</p>
                        </div>
                        
                        <p>The spot has been offered to the next person in line.</p>
                        
                        <p>If you're still interested in this trip, you can join the waiting list again if spots are no longer available, or book directly if rooms become available.</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>{DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                await email.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send expiration email: {ex.Message}");
            }
        }

        private async Task SendWaitingListNotificationEmail(WaitingListEntry entry, IEmailSender email)
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

                var subject = $"Your Turn Has Come! - {tripName}";

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

                await email.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send waiting list notification email: {ex.Message}");
            }
        }

        private async Task SendPositionUpdateEmails(
            ApplicationDbContext db, int tripId, int excludeEntryId, IEmailSender email, CancellationToken ct)
        {
            var waitingEntries = await db.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .OrderBy(w => w.Position)
                .ToListAsync(ct);

            foreach (var entry in waitingEntries)
            {
                await SendPositionUpdateEmail(entry, email);
            }
        }

        private async Task SendPositionUpdateEmail(WaitingListEntry entry, IEmailSender email)
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
                            <p><strong>Your new position:</strong> <span style='font-size: 24px; font-weight: bold; color: #667eea;'>#{entry.Position}</span></p>
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

                await email.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send position update email: {ex.Message}");
            }
        }
    }
}