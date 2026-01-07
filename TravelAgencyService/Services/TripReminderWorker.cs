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
            // ריצה כל שעה (פשוט)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessReminders(stoppingToken);
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
                          <p>See you soon ✈️</p>
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
    }
}
