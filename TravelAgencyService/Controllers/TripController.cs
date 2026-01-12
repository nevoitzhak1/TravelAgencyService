using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using System.Security.Claims;

namespace TravelAgencyService.Controllers
{
    public class TripController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TripController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Trip
        public async Task<IActionResult> Index(
            string? searchQuery,
            string? country,
            string? destination,
            PackageType? packageType,
            decimal? minPrice,
            decimal? maxPrice,
            DateTime? startDateFrom,
            DateTime? startDateTo,
            bool onlyDiscounted = false,
            string sortBy = "date",
            int page = 1)
        {
            var query = _context.Trips
                .Include(t => t.Reviews)
                .Where(t => t.IsVisible && t.StartDate > DateTime.Now)
                .AsQueryable();

            // Search filter
            if (!string.IsNullOrEmpty(searchQuery))
            {
                searchQuery = searchQuery.ToLower();
                query = query.Where(t =>
                    t.PackageName.ToLower().Contains(searchQuery) ||
                    t.Destination.ToLower().Contains(searchQuery) ||
                    t.Country.ToLower().Contains(searchQuery) ||
                    t.Description.ToLower().Contains(searchQuery));
            }

            // Country filter
            if (!string.IsNullOrEmpty(country))
            {
                query = query.Where(t => t.Country == country);
            }

            // Destination filter
            if (!string.IsNullOrEmpty(destination))
            {
                query = query.Where(t => t.Destination == destination);
            }

            // Package type filter
            if (packageType.HasValue)
            {
                query = query.Where(t => t.PackageType == packageType.Value);
            }

            // Price range filter
            if (minPrice.HasValue)
            {
                query = query.Where(t => t.Price >= minPrice.Value);
            }
            if (maxPrice.HasValue)
            {
                query = query.Where(t => t.Price <= maxPrice.Value);
            }

            // Date range filter
            if (startDateFrom.HasValue)
            {
                query = query.Where(t => t.StartDate >= startDateFrom.Value);
            }
            if (startDateTo.HasValue)
            {
                query = query.Where(t => t.StartDate <= startDateTo.Value);
            }

            // Only discounted filter
            if (onlyDiscounted)
            {
                query = query.Where(t => t.OriginalPrice != null &&
                                         t.DiscountEndDate != null &&
                                         t.DiscountEndDate > DateTime.Now);
            }

            // Sorting
            query = sortBy switch
            {
                "price_asc" => query.OrderBy(t => t.Price),
                "price_desc" => query.OrderByDescending(t => t.Price),
                "popular" => query.OrderByDescending(t => t.TimesBooked),
                "category" => query.OrderBy(t => t.PackageType).ThenBy(t => t.StartDate),
                "date" => query.OrderBy(t => t.StartDate),
                _ => query.OrderBy(t => t.StartDate)
            };

            // Get total count before pagination
            var totalTrips = await query.CountAsync();

            // Pagination
            var pageSize = 9;
            var trips = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
                    MinimumAge = t.MinimumAge,
                    MaximumAge = t.MaximumAge,
                    Description = t.Description,
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

            // Get filter options
            var countries = await _context.Trips
                .Where(t => t.IsVisible)
                .Select(t => t.Country)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var destinations = await _context.Trips
                .Where(t => t.IsVisible)
                .Select(t => t.Destination)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            var viewModel = new TripListViewModel
            {
                Trips = trips,
                TotalTrips = totalTrips,
                CurrentPage = page,
                PageSize = pageSize,
                SearchQuery = searchQuery,
                SelectedCountry = country,
                SelectedDestination = destination,
                SelectedPackageType = packageType,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                StartDateFrom = startDateFrom,
                StartDateTo = startDateTo,
                OnlyDiscounted = onlyDiscounted,
                SortBy = sortBy,
                Countries = countries,
                Destinations = destinations
            };

            return View(viewModel);
        }

        // GET: /Trip/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var trip = await _context.Trips
                .Include(t => t.Images)
                .Include(t => t.Reviews!)
                    .ThenInclude(r => r.User)
                .Include(t => t.WaitingList)
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            var tripViewModel = new TripViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                Price = trip.Price,
                OriginalPrice = trip.OriginalPrice,
                AvailableRooms = trip.AvailableRooms,
                PackageType = trip.PackageType,
                MinimumAge = trip.MinimumAge,
                MaximumAge = trip.MaximumAge,
                Description = trip.Description,
                MainImageUrl = trip.MainImageUrl,
                IsOnSale = trip.IsOnSale,
                IsFullyBooked = trip.IsFullyBooked,
                TripDurationDays = trip.TripDurationDays,
                DiscountPercentage = trip.DiscountPercentage,
                AverageRating = trip.Reviews != null && trip.Reviews.Any()
                    ? Math.Round(trip.Reviews.Average(r => r.Rating), 1)
                    : 0,
                ReviewCount = trip.Reviews?.Count ?? 0,
                TimesBooked = trip.TimesBooked
            };

            var images = trip.Images?.Select(i => new TripImageViewModel
            {
                ImageId = i.ImageId,
                ImageUrl = i.ImageUrl,
                Caption = i.Caption
            }).ToList() ?? new List<TripImageViewModel>();

            var reviews = trip.Reviews?
                .Where(r => r.IsApproved)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReviewViewModel
                {
                    ReviewId = r.ReviewId,
                    UserName = r.User != null ? $"{r.User.FirstName} {r.User.LastName}" : "Anonymous",
                    Rating = r.Rating,
                    Title = r.Title,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                }).ToList() ?? new List<ReviewViewModel>();

            // Check user-specific data
            bool canBook = true;
            bool isInCart = false;
            bool isInWaitingList = false;
            int waitingListPosition = 0;
            int waitingListCount = trip.WaitingList?.Count(w => w.Status == WaitingListStatus.Waiting) ?? 0;

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Check if in cart
                isInCart = await _context.CartItems
                    .AnyAsync(c => c.UserId == userId && c.TripId == id);

                // Check if in waiting list
                var waitingEntry = await _context.WaitingListEntries
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.TripId == id &&
                                              (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                if (waitingEntry != null)
                {
                    isInWaitingList = true;
                    waitingListPosition = waitingEntry.Position;
                }

                // Check if user can book (max 3 active bookings)
                var activeBookings = await _context.Bookings
                    .CountAsync(b => b.UserId == userId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now);

                canBook = activeBookings < 3;
            }

            // Check if someone else has waiting list priority (24h window)
            var hasWaitingListPriority = false;
            var isMyTurnToBook = false;

            if (User.Identity?.IsAuthenticated == true)
            {
                var notifiedEntry = await _context.WaitingListEntries
                    .FirstOrDefaultAsync(w => w.TripId == id &&
                                              w.Status == WaitingListStatus.Notified &&
                                              w.NotificationExpiresAt > DateTime.Now);

                if (notifiedEntry != null)
                {
                    var userId2 = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (notifiedEntry.UserId == userId2)
                    {
                        isMyTurnToBook = true;
                    }
                    else
                    {
                        hasWaitingListPriority = true;
                    }
                }
            }

            var viewModel = new TripDetailsViewModel
            {
                Trip = tripViewModel,
                Images = images,
                Reviews = reviews,
                CanBook = canBook,
                IsInCart = isInCart,
                IsInWaitingList = isInWaitingList,
                WaitingListPosition = waitingListPosition,
                WaitingListCount = waitingListCount,
                HasWaitingListPriority = hasWaitingListPriority,
                IsMyTurnToBook = isMyTurnToBook
            };

            return View(viewModel);
        }

        // GET: /Trip/Search (AJAX endpoint for partial search)
        [HttpGet]
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return Json(new List<object>());
            }

            query = query.ToLower();

            var results = await _context.Trips
                .Where(t => t.IsVisible &&
                           t.StartDate > DateTime.Now &&
                           (t.PackageName.ToLower().Contains(query) ||
                            t.Destination.ToLower().Contains(query) ||
                            t.Country.ToLower().Contains(query)))
                .Take(10)
                .Select(t => new
                {
                    t.TripId,
                    t.PackageName,
                    t.Destination,
                    t.Country,
                    t.Price,
                    t.MainImageUrl
                })
                .ToListAsync();

            return Json(results);
        }
    }
}