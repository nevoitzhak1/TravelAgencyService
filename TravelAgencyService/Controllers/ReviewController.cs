using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

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
    }
}