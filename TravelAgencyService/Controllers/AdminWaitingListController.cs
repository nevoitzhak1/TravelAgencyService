using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminWaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminWaitingListController(ApplicationDbContext context)
        {
            _context = context;
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
                    WaitingCount = _context.WaitingListEntries
                        .Count(e => e.TripId == t.TripId && e.Status == WaitingListStatus.Waiting)
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

            var entries = await _context.WaitingListEntries
                .Include(e => e.User)
                .Where(e => e.TripId == tripId && e.Status == WaitingListStatus.Waiting)
                .OrderBy(e => e.JoinedDate)
                .ToListAsync();

            ViewBag.TripName = trip.PackageName;
            ViewBag.AvailableRooms = trip.AvailableRooms;

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

            // Mark as notified
            next.Status = WaitingListStatus.Notified;
            next.IsNotified = true;
            next.NotificationDate = DateTime.Now;
            next.NotificationExpiresAt = DateTime.Now.AddHours(24);

            await _context.SaveChangesAsync();

            // TODO: Send email
            // _emailService.SendEmail(next.User.Email, "Room Available", "...");

            TempData["Success"] = $"User '{next.User?.Email}' has been notified!";
            return RedirectToAction(nameof(Details), new { tripId });
        }
    }
}