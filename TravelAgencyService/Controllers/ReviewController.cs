using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace TravelAgencyService.Controllers
{
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReviewController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Review/CreateTripReview?tripId=5
        [Authorize]
        public async Task<IActionResult> CreateTripReview(int tripId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check if user booked this trip AND trip has ended
            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.UserId == userId &&
                                         b.TripId == tripId &&
                                         (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed) &&
                                         b.Trip!.EndDate < DateTime.Now);

            if (booking == null)
            {
                TempData["Error"] = "You can only review trips you have booked and that have ended.";
                return RedirectToAction("Details", "Trip", new { id = tripId });
            }

            // Check if already reviewed this specific trip
            var alreadyReviewed = await _context.Reviews
                .AnyAsync(r => r.UserId == userId && r.TripId == tripId);

            if (alreadyReviewed)
            {
                TempData["Error"] = "You have already reviewed this trip.";
                return RedirectToAction("Details", "Trip", new { id = tripId });
            }

            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            return View("Create", new CreateReviewViewModel
            {
                TripId = tripId,
                TripName = trip.PackageName,
                ReviewType = ReviewType.TripReview
            });
        }

        // GET: /Review/CreateServiceReview
        [Authorize]
        public IActionResult CreateServiceReview()
        {
            return View("Create", new CreateReviewViewModel
            {
                ReviewType = ReviewType.WebsiteReview
            });
        }

        // POST: /Review/Create
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateReviewViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Validate Trip Review
            if (model.ReviewType == ReviewType.TripReview && model.TripId.HasValue)
            {
                var booking = await _context.Bookings
                    .Include(b => b.Trip)
                    .FirstOrDefaultAsync(b => b.UserId == userId &&
                                             b.TripId == model.TripId &&
                                             (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed) &&
                                             b.Trip!.EndDate < DateTime.Now);

                if (booking == null)
                {
                    TempData["Error"] = "You can only review trips you have booked and that have ended.";
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }

                var alreadyReviewed = await _context.Reviews
                    .AnyAsync(r => r.UserId == userId && r.TripId == model.TripId);

                if (alreadyReviewed)
                {
                    TempData["Error"] = "You have already reviewed this trip.";
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }
            }

            if (ModelState.IsValid)
            {
                var review = new Review
                {
                    UserId = userId!,
                    TripId = model.TripId,
                    Rating = model.Rating,
                    Title = model.Title,
                    Comment = model.Comment,
                    ReviewType = model.ReviewType,
                    IsApproved = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Reviews.Add(review);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Thank you for your review!";

                if (model.TripId.HasValue)
                {
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }

                return RedirectToAction("Index", "Home");
            }

            if (model.TripId.HasValue)
            {
                var trip = await _context.Trips.FindAsync(model.TripId);
                model.TripName = trip?.PackageName;
            }

            return View(model);
        }
    }
}