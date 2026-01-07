using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class WaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public WaitingListController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: /WaitingList/Index
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var myWaitingLists = await _context.WaitingListEntries
                .Include(w => w.Trip)
                .Where(w => w.UserId == user.Id && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.JoinedDate)
                .Select(w => new
                {
                    w.WaitingListEntryId,
                    w.TripId,
                    TripName = w.Trip != null ? w.Trip.PackageName : "Unknown",
                    w.Position,
                    w.JoinedDate,
                    w.RoomsRequested
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

            // Count waiting
            var waitingQuery = _context.WaitingListEntries
                .Where(e => e.TripId == tripId && e.Status == WaitingListStatus.Waiting);

            var totalWaiting = await waitingQuery.CountAsync();

            // My entry?
            var myEntry = await waitingQuery
                .Where(e => e.UserId == user.Id)
                .OrderBy(e => e.JoinedDate)
                .FirstOrDefaultAsync();

            int? myPosition = null;
            if (myEntry != null)
            {
                var countBefore = await waitingQuery.CountAsync(e => e.JoinedDate < myEntry.JoinedDate);
                myPosition = countBefore + 1;
            }

            // ETA
            string etaText = "";
            if (trip.AvailableRooms > 0)
                etaText = "Rooms are available now — no need for waiting list.";
            else if (totalWaiting == 0)
                etaText = "Estimated: If you join now, you'll be first in line.";
            else
            {
                var pos = myPosition ?? (totalWaiting + 1);
                var avgDaysPerRoom = 2;
                etaText = $"Estimated: approximately {pos * avgDaysPerRoom} days (general estimate).";
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
                    : "Rooms are available — no need for waiting list."
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

            // Can only join when no rooms
            if (trip.AvailableRooms > 0)
            {
                TempData["Error"] = "Cannot join waiting list when rooms are available.";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            // Already in queue?
            var already = await _context.WaitingListEntries.AnyAsync(e =>
                e.TripId == tripId &&
                e.UserId == user.Id &&
                e.Status == WaitingListStatus.Waiting);

            if (already)
            {
                TempData["Error"] = "You're already in the waiting list.";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            // Email required
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                TempData["Error"] = "Email is required to join the waiting list.";
                return RedirectToAction(nameof(Status), new { tripId });
            }

            // Calculate position (FIFO)
            var maxPosition = await _context.WaitingListEntries
                .Where(e => e.TripId == tripId && e.Status == WaitingListStatus.Waiting)
                .MaxAsync(e => (int?)e.Position) ?? 0;

            _context.WaitingListEntries.Add(new WaitingListEntry
            {
                TripId = tripId,
                UserId = user.Id,
                JoinedDate = DateTime.Now,
                Position = maxPosition + 1,
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

            var entry = await _context.WaitingListEntries.FirstOrDefaultAsync(e =>
                e.TripId == tripId &&
                e.UserId == user.Id &&
                e.Status == WaitingListStatus.Waiting);

            if (entry != null)
            {
                entry.Status = WaitingListStatus.Cancelled;
                await _context.SaveChangesAsync();
                TempData["Success"] = "You've been removed from the waiting list.";
            }

            return RedirectToAction(nameof(Status), new { tripId });
        }
    }
}