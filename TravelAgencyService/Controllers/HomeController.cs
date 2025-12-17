using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Get total counts
            var totalTrips = await _context.Trips.CountAsync(t => t.IsVisible);
            var totalCountries = await _context.Trips
                .Where(t => t.IsVisible)
                .Select(t => t.Country)
                .Distinct()
                .CountAsync();
            var totalBookings = await _context.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed);

            // Get ALL trips for map
            var allTrips = await _context.Trips
                .Include(t => t.Reviews)
                .Where(t => t.IsVisible && t.StartDate > DateTime.Now)
                .OrderByDescending(t => t.TimesBooked)
                
                .Select(t => new TripViewModel
                {
                    TripId = t.TripId,
                    PackageName = t.PackageName,
                    Destination = t.Destination,
                    Country = t.Country,
                    StartDate = t.StartDate,
                    EndDate = t.EndDate,
                    Price = t.Price,
                    OriginalPrice = t.OriginalPrice,
                    AvailableRooms = t.AvailableRooms,
                    PackageType = t.PackageType,
                    MainImageUrl = t.MainImageUrl,
                    IsOnSale = t.OriginalPrice != null && t.DiscountEndDate != null && t.DiscountEndDate > DateTime.Now,
                    IsFullyBooked = t.AvailableRooms <= 0,
                    TripDurationDays = (t.EndDate - t.StartDate).Days,
                    DiscountPercentage = t.OriginalPrice != null && t.OriginalPrice > 0
                        ? Math.Round((1 - (t.Price / t.OriginalPrice.Value)) * 100, 0)
                        : null,
                    AverageRating = t.Reviews != null && t.Reviews.Any()
                        ? Math.Round(t.Reviews.Average(r => r.Rating), 1)
                        : 0,
                    ReviewCount = t.Reviews != null ? t.Reviews.Count : 0,
                    TimesBooked = t.TimesBooked
                })
                .ToListAsync();
            Console.WriteLine($" Total trips in AllTrips: {allTrips.Count}");
            Console.WriteLine($" Total ALL trips (no filter): {await _context.Trips.CountAsync()}");

            // Get top 6 for featured cards (הכי פופולריים)
            var featuredTrips = allTrips.Take(6).ToList();

            // Get recent reviews (last 3)
            var recentReviews = await _context.Reviews
                .Include(r => r.User)
                .Include(r => r.Trip)
                .Where(r => r.IsApproved)
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
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

            var viewModel = new HomeViewModel
            {
                TotalTrips = totalTrips,
                TotalCountries = totalCountries,
                TotalBookings = totalBookings,
                FeaturedTrips = featuredTrips,
                AllTrips = allTrips,
                RecentReviews = recentReviews
            };

            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}