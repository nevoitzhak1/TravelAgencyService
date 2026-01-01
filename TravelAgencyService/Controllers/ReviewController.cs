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

        // GET: /Review/Index
        public async Task<IActionResult> Index(int? rating, int? tripId, string? searchQuery, int page = 1)
        {
            var query = _context.Reviews
                .Include(r => r.User)
                .Include(r => r.Trip)
                .Where(r => r.IsApproved)
                .AsQueryable();

            // Filter by rating
            if (rating.HasValue && rating.Value > 0)
            {
                query = query.Where(r => r.Rating == rating.Value);
            }

            // Filter by trip
            if (tripId.HasValue && tripId.Value > 0)
            {
                query = query.Where(r => r.TripId == tripId.Value);
            }

            // Search in title and comment
            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(r =>
                    r.Title != null && r.Title.Contains(searchQuery) ||
                    r.Comment.Contains(searchQuery));
            }

            var totalReviews = await query.CountAsync();
            var pageSize = 12;

            var reviews = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new ReviewViewModel
                {
                    ReviewId = r.ReviewId,
                    UserName = r.User != null ? $"{r.User.FirstName} {r.User.LastName}" : "Anonymous",
                    Rating = r.Rating,
                    Title = r.Title,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt,
                    TripName = r.Trip != null ? r.Trip.PackageName : ""
                })
                .ToListAsync();

            // Get trips for filter dropdown
            var trips = await _context.Trips
                .Where(t => t.IsVisible)
                .OrderBy(t => t.PackageName)
                .Select(t => new { t.TripId, t.PackageName })
                .ToListAsync();

            // Calculate rating statistics
            var allReviews = await _context.Reviews
                .Where(r => r.IsApproved)
                .ToListAsync();

            var viewModel = new ReviewListViewModel
            {
                Reviews = reviews,
                TotalReviews = totalReviews,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalReviews / pageSize),
                SelectedRating = rating,
                SelectedTripId = tripId,
                SearchQuery = searchQuery,
                AvailableTrips = trips.Select(t => new TripSelectionViewModel
                {
                    TripId = t.TripId,
                    PackageName = t.PackageName
                }).ToList(),
                AverageRating = allReviews.Any() ? Math.Round(allReviews.Average(r => r.Rating), 1) : 0,
                FiveStarCount = allReviews.Count(r => r.Rating == 5),
                FourStarCount = allReviews.Count(r => r.Rating == 4),
                ThreeStarCount = allReviews.Count(r => r.Rating == 3),
                TwoStarCount = allReviews.Count(r => r.Rating == 2),
                OneStarCount = allReviews.Count(r => r.Rating == 1)
            };

            return View(viewModel);
        }

        // GET: /Review/Create?tripId=5 (Trip Review) or /Review/Create (Service Review)
        [Authorize]
        public async Task<IActionResult> Create(int? tripId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // If tripId provided - it's a Trip Review
            if (tripId.HasValue)
            {
                // Check if user booked this trip
                var hasBooked = await _context.Bookings
                    .AnyAsync(b => b.UserId == userId &&
                                  b.TripId == tripId &&
                                  b.Status == BookingStatus.Confirmed);

                if (!hasBooked)
                {
                    TempData["Error"] = "You can only review trips you have booked.";
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                // Check if already reviewed
                var alreadyReviewed = await _context.Reviews
                    .AnyAsync(r => r.UserId == userId && r.TripId == tripId);

                if (alreadyReviewed)
                {
                    TempData["Error"] = "You have already reviewed this trip.";
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                var trip = await _context.Trips.FindAsync(tripId);
                if (trip == null) return NotFound();

                return View(new CreateReviewViewModel
                {
                    TripId = tripId,
                    TripName = trip.PackageName,
                    ReviewType = ReviewType.TripReview
                });
            }

            // No tripId - it's a Website/Service Review
            // Check if user already left a service review
            var hasServiceReview = await _context.Reviews
                .AnyAsync(r => r.UserId == userId && r.ReviewType == ReviewType.WebsiteReview);

            if (hasServiceReview)
            {
                TempData["Error"] = "You have already submitted a service review.";
                return RedirectToAction("Index", "Home");
            }

            return View(new CreateReviewViewModel
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
                var hasBooked = await _context.Bookings
                    .AnyAsync(b => b.UserId == userId &&
                                  b.TripId == model.TripId &&
                                  b.Status == BookingStatus.Confirmed);

                if (!hasBooked)
                {
                    TempData["Error"] = "You can only review trips you have booked.";
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

            // Validate Service Review
            if (model.ReviewType == ReviewType.WebsiteReview)
            {
                var hasServiceReview = await _context.Reviews
                    .AnyAsync(r => r.UserId == userId && r.ReviewType == ReviewType.WebsiteReview);

                if (hasServiceReview)
                {
                    TempData["Error"] = "You have already submitted a service review.";
                    return RedirectToAction("Index", "Home");
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

            // If validation failed, reload trip name if needed
            if (model.TripId.HasValue)
            {
                var trip = await _context.Trips.FindAsync(model.TripId);
                model.TripName = trip?.PackageName;
            }

            return View(model);
        }
    }
}