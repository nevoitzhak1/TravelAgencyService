using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        // GET: /Admin/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var viewModel = new AdminDashboardViewModel
            {
                TotalTrips = await _context.Trips.CountAsync(),
                TotalUsers = await _userManager.Users.CountAsync(),
                TotalBookings = await _context.Bookings.CountAsync(),
                PendingBookings = await _context.Bookings.CountAsync(b => b.Status == BookingStatus.Pending),
                TotalWaitingList = await _context.WaitingListEntries.CountAsync(w => w.Status == WaitingListStatus.Waiting),
                TotalRevenue = await _context.Bookings
                    .Where(b => b.IsPaid)
                    .SumAsync(b => b.TotalPrice),
                RecentBookings = await _context.Bookings
                    .Include(b => b.User)
                    .Include(b => b.Trip)
                    .OrderByDescending(b => b.BookingDate)
                    .Take(5)
                    .Select(b => new RecentBookingViewModel
                    {
                        BookingId = b.BookingId,
                        UserName = b.User != null ? $"{b.User.FirstName} {b.User.LastName}" : "Unknown",
                        TripName = b.Trip != null ? b.Trip.PackageName : "Unknown",
                        BookingDate = b.BookingDate,
                        TotalPrice = b.TotalPrice,
                        Status = b.Status
                    })
                    .ToListAsync(),
                PopularTrips = await _context.Trips
                    .OrderByDescending(t => t.TimesBooked)
                    .Take(5)
                    .Select(t => new TripViewModel
                    {
                        TripId = t.TripId,
                        PackageName = t.PackageName,
                        Destination = t.Destination,
                        TimesBooked = t.TimesBooked,
                        AvailableRooms = t.AvailableRooms
                    })
                    .ToListAsync()
            };

            return View(viewModel);
        }

        #region Trip Management


        #region Trip Clone & Autocomplete

        // API: Autocomplete search for existing trips
        [HttpGet]
        public async Task<IActionResult> SearchTripsAutocomplete(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
            {
                return Json(new List<TripAutocompleteDto>());
            }

            var trips = await _context.Trips
                .Where(t => t.PackageName.Contains(term) ||
                            t.Destination.Contains(term) ||
                            t.Country.Contains(term))
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new TripAutocompleteDto
                {
                    TripId = t.TripId,
                    PackageName = t.PackageName,
                    Destination = t.Destination,
                    Country = t.Country,
                    Year = t.StartDate.Year,
                    DisplayText = $"{t.PackageName} - {t.Destination}, {t.Country} ({t.StartDate.Year})"
                })
                .ToListAsync();

            return Json(trips);
        }

        // API: Load trip template data by ID
        [HttpGet]
        public async Task<IActionResult> GetTripTemplate(int id)
        {
            var trip = await _context.Trips
                .Include(t => t.ReminderRules)
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            var template = new TripTemplateDto
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                Price = trip.Price,
                TotalRooms = trip.TotalRooms,
                PackageType = trip.PackageType,
                MinimumAge = trip.MinimumAge,
                MaximumAge = trip.MaximumAge,
                Description = trip.Description,
                MainImageUrl = trip.MainImageUrl,
                IsVisible = trip.IsVisible,
                LastBookingDate = trip.LastBookingDate,
                CancellationDaysLimit = trip.CancellationDaysLimit,
                TripDurationDays = (trip.EndDate - trip.StartDate).Days,
                ReminderRules = trip.ReminderRules?.Select(r => new ReminderRuleInput
                {
                    OffsetAmount = r.OffsetAmount,
                    OffsetUnit = r.OffsetUnit,
                    IsActive = r.IsActive
                }).ToList() ?? new List<ReminderRuleInput>()
            };

            return Json(template);
        }

        #endregion
        // GET: /Admin/ManageTrips
        public async Task<IActionResult> ManageTrips(string? searchQuery, PackageType? packageType, int page = 1)
        {
            var query = _context.Trips.AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(t =>
                    t.PackageName.Contains(searchQuery) ||
                    t.Destination.Contains(searchQuery) ||
                    t.Country.Contains(searchQuery));
            }

            if (packageType.HasValue)
            {
                query = query.Where(t => t.PackageType == packageType.Value);
            }

            var totalTrips = await query.CountAsync();
            var pageSize = 10;

            var trips = await query
                .OrderByDescending(t => t.CreatedAt)
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
                    MainImageUrl = t.MainImageUrl,
                    IsOnSale = t.OriginalPrice != null && t.DiscountEndDate != null && t.DiscountEndDate > DateTime.Now,
                    TimesBooked = t.TimesBooked
                })
                .ToListAsync();

            ViewBag.SearchQuery = searchQuery;
            ViewBag.PackageType = packageType;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalTrips / pageSize);
            ViewBag.TotalTrips = totalTrips;

            return View(trips);
        }

        // GET: /Admin/CreateTrip
        public IActionResult CreateTrip()
        {
            return View(new TripCreateViewModel());
        }

        // POST: /Admin/CreateTrip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTrip(TripCreateViewModel model)
        {
            if (model.EndDate <= model.StartDate)
            {
                ModelState.AddModelError("EndDate", "End date must be after start date");
            }

            if (ModelState.IsValid)
            {
                string? recurringGroupKey = null;
                Trip? sourceTrip = null;

                // If cloning from an existing trip, get or generate the RecurringGroupKey
                if (model.SourceTripId.HasValue)
                {
                    sourceTrip = await _context.Trips
                        .FirstOrDefaultAsync(t => t.TripId == model.SourceTripId.Value);

                    if (sourceTrip != null)
                    {
                        // Use existing RecurringGroupKey or generate new one
                        recurringGroupKey = sourceTrip.RecurringGroupKey ?? Guid.NewGuid().ToString();

                        // Update source trip if it doesn't have a key yet (first time cloning)
                        if (string.IsNullOrEmpty(sourceTrip.RecurringGroupKey))
                        {
                            sourceTrip.RecurringGroupKey = recurringGroupKey;
                            _context.Trips.Update(sourceTrip);
                            await _context.SaveChangesAsync();
                        }
                    }
                }

                // CHECK FOR DUPLICATE: Same RecurringGroupKey + Same Year
                if (!string.IsNullOrEmpty(recurringGroupKey))
                {
                    var targetYear = model.StartDate.Year;

                    var existingTrip = await _context.Trips
                        .Where(t => t.RecurringGroupKey == recurringGroupKey &&
                                   t.StartDate.Year == targetYear)
                        .Select(t => new {
                            t.TripId,
                            t.PackageName,
                            t.Destination,
                            t.Country,
                            t.StartDate
                        })
                        .FirstOrDefaultAsync();

                    if (existingTrip != null)
                    {
                        // Show clear, user-friendly error message
                        var existingDate = existingTrip.StartDate.ToString("MMM dd, yyyy");

                        ModelState.AddModelError("StartDate",
                            $"⚠️ A trip from this series already exists for year {targetYear}!");

                        ModelState.AddModelError("",
                            $"Existing Trip: '{existingTrip.PackageName}' " +
                            $"({existingTrip.Destination}, {existingTrip.Country}) " +
                            $"starting on {existingDate}. " +
                            $"Please choose a different year or edit the existing trip.");

                        // Add suggestion for available years
                        var existingYears = await _context.Trips
                            .Where(t => t.RecurringGroupKey == recurringGroupKey)
                            .Select(t => t.StartDate.Year)
                            .OrderBy(y => y)
                            .ToListAsync();

                        var suggestedYear = targetYear + 1;
                        while (existingYears.Contains(suggestedYear))
                        {
                            suggestedYear++;
                        }

                        TempData["Warning"] = $"💡 Tip: This trip already exists for years: {string.Join(", ", existingYears)}. " +
                                             $"Try using year {suggestedYear} instead.";

                        return View(model);
                    }
                }

                // Create the new trip
                var trip = new Trip
                {
                    PackageName = model.PackageName,
                    Destination = model.Destination,
                    Country = model.Country,
                    StartDate = model.StartDate,
                    EndDate = model.EndDate,
                    Price = model.Price,
                    TotalRooms = model.TotalRooms,
                    AvailableRooms = model.TotalRooms,
                    PackageType = model.PackageType,
                    MinimumAge = model.MinimumAge,
                    MaximumAge = model.MaximumAge,
                    Description = model.Description,
                    MainImageUrl = model.MainImageUrl,
                    IsVisible = model.IsVisible,
                    LastBookingDate = model.LastBookingDate,
                    CancellationDaysLimit = model.CancellationDaysLimit,
                    RecurringGroupKey = recurringGroupKey,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                try
                {
                    // Geocoding for map location
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "TravelAgencyApp/1.0");
                        var query = Uri.EscapeDataString($"{model.Destination}, {model.Country}");
                        var url = $"https://nominatim.openstreetmap.org/search?format=json&q={query}&limit=1";
                        var response = await httpClient.GetStringAsync(url);
                        var json = System.Text.Json.JsonDocument.Parse(response);

                        if (json.RootElement.GetArrayLength() > 0)
                        {
                            var result = json.RootElement[0];
                            trip.Latitude = result.GetProperty("lat").GetDouble();
                            trip.Longitude = result.GetProperty("lon").GetDouble();
                        }
                    }
                }
                catch
                {
                    // Geocoding failed, continue without coordinates
                }

                try
                {
                    _context.Trips.Add(trip);
                    await _context.SaveChangesAsync();

                    // Add reminder rules
                    if (model.ReminderRules != null && model.ReminderRules.Any())
                    {
                        foreach (var rule in model.ReminderRules)
                        {
                            var reminderRule = new TripReminderRule
                            {
                                TripId = trip.TripId,
                                OffsetAmount = rule.OffsetAmount,
                                OffsetUnit = rule.OffsetUnit,
                                IsActive = rule.IsActive
                            };
                            _context.TripReminderRules.Add(reminderRule);
                        }
                        await _context.SaveChangesAsync();
                    }

                    TempData["Success"] = $"✓ Trip '{trip.PackageName}' created successfully for {trip.StartDate.Year}!";

                    if (model.SourceTripId.HasValue)
                    {
                        TempData["Success"] += $" (Cloned from Trip ID: {model.SourceTripId.Value})";
                    }

                    return RedirectToAction(nameof(ManageTrips));
                }
                catch (DbUpdateException ex)
                {
                    // Handle database-level constraint violations (as a safety net)
                    if (ex.InnerException?.Message.Contains("IX_Trips_RecurringGroupKey_StartYear") == true)
                    {
                        ModelState.AddModelError("StartDate",
                            $"⚠️ A trip with this recurring group already exists for year {model.StartDate.Year}.");

                        ModelState.AddModelError("",
                            "This error occurred at the database level. " +
                            "Please choose a different year and try again.");

                        TempData["Error"] = "Database constraint prevented duplicate trip. Please choose a different year.";
                    }
                    else
                    {
                        ModelState.AddModelError("", $"Database error: {ex.InnerException?.Message ?? ex.Message}");
                    }

                    return View(model);
                }
            }

            return View(model);
        }

        // GET: /Admin/EditTrip/5
        public async Task<IActionResult> EditTrip(int id)
        {
            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            var model = new TripEditViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                Price = trip.Price,
                OriginalPrice = trip.OriginalPrice,
                DiscountEndDate = trip.DiscountEndDate,
                TotalRooms = trip.TotalRooms,
                AvailableRooms = trip.AvailableRooms,
                PackageType = trip.PackageType,
                MinimumAge = trip.MinimumAge,
                MaximumAge = trip.MaximumAge,
                Description = trip.Description,
                MainImageUrl = trip.MainImageUrl,
                IsVisible = trip.IsVisible,
                LastBookingDate = trip.LastBookingDate,
                CancellationDaysLimit = trip.CancellationDaysLimit,
                TimesBooked = trip.TimesBooked,
                CreatedAt = trip.CreatedAt
            };
            model.ReminderRules = await _context.TripReminderRules
                 .Where(r => r.TripId == id && r.IsActive)
                 .OrderBy(r => r.OffsetUnit).ThenBy(r => r.OffsetAmount)
                 .Select(r => new ReminderRuleInput
                 {
                     TripReminderRuleId = r.TripReminderRuleId,
                     OffsetAmount = r.OffsetAmount,
                     OffsetUnit = r.OffsetUnit,
                     IsActive = r.IsActive
                 })
                 .ToListAsync();

            return View(model);
        }

        // POST: /Admin/EditTrip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTrip(int id, TripEditViewModel model)
        {
            if (id != model.TripId)
            {
                return NotFound();
            }

            if (model.EndDate <= model.StartDate)
            {
                ModelState.AddModelError("EndDate", "End date must be after start date");
            }

            if (ModelState.IsValid)
            {
                var trip = await _context.Trips.FindAsync(id);

                if (trip == null)
                {
                    return NotFound();
                }

                // Save old available rooms to check if rooms were added
                var oldAvailableRooms = trip.AvailableRooms;

                trip.PackageName = model.PackageName;
                trip.Destination = model.Destination;
                trip.Country = model.Country;
                trip.StartDate = model.StartDate;
                trip.EndDate = model.EndDate;
                trip.Price = model.Price;
                trip.TotalRooms = model.TotalRooms;
                trip.AvailableRooms = model.AvailableRooms;
                trip.PackageType = model.PackageType;
                trip.MinimumAge = model.MinimumAge;
                trip.MaximumAge = model.MaximumAge;
                trip.Description = model.Description;
                trip.MainImageUrl = model.MainImageUrl;
                trip.IsVisible = model.IsVisible;
                trip.LastBookingDate = model.LastBookingDate;
                trip.CancellationDaysLimit = model.CancellationDaysLimit;
                trip.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // If rooms were added, check waiting list
                if (model.AvailableRooms > oldAvailableRooms)
                {
                    await ProcessWaitingListAfterRoomsAdded(id, model.AvailableRooms);
                }

                TempData["Success"] = $"Trip '{trip.PackageName}' updated successfully!";

                // Deactivate all existing rules for this trip (keeps send logs intact)
                var existing = await _context.TripReminderRules.Where(r => r.TripId == id).ToListAsync();
                foreach (var r in existing) r.IsActive = false;

                // Re-activate/update or add new ones
                if (model.ReminderRules != null)
                {
                    foreach (var input in model.ReminderRules.Where(x => x.OffsetAmount > 0 && x.IsActive))
                    {
                        if (input.TripReminderRuleId.HasValue)
                        {
                            var rule = existing.FirstOrDefault(x => x.TripReminderRuleId == input.TripReminderRuleId.Value);
                            if (rule != null)
                            {
                                rule.OffsetAmount = input.OffsetAmount;
                                rule.OffsetUnit = input.OffsetUnit;
                                rule.IsActive = true;
                            }
                            else
                            {
                                _context.TripReminderRules.Add(new TripReminderRule
                                {
                                    TripId = id,
                                    OffsetAmount = input.OffsetAmount,
                                    OffsetUnit = input.OffsetUnit,
                                    IsActive = true
                                });
                            }
                        }
                        else
                        {
                            _context.TripReminderRules.Add(new TripReminderRule
                            {
                                TripId = id,
                                OffsetAmount = input.OffsetAmount,
                                OffsetUnit = input.OffsetUnit,
                                IsActive = true
                            });
                        }
                    }
                }

                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(ManageTrips));
            }

            return View(model);
        }

        // GET: /Admin/DeleteTrip
        public async Task<IActionResult> DeleteTrip(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var trip = await _context.Trips
                .Include(t => t.Bookings)
                .Include(t => t.WaitingList)
                .Include(t => t.Reviews)
                .Include(t => t.CartItems)
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            // Check if trip has any bookings
            var bookingsCount = trip.Bookings?.Count ?? 0;
            var waitingListCount = trip.WaitingList?.Count ?? 0;
            var reviewsCount = trip.Reviews?.Count ?? 0;
            var cartItemsCount = trip.CartItems?.Count ?? 0;

            // Calculate total dependencies
            var totalDependencies = bookingsCount + waitingListCount + reviewsCount + cartItemsCount;

            ViewBag.BookingsCount = bookingsCount;
            ViewBag.WaitingListCount = waitingListCount;
            ViewBag.ReviewsCount = reviewsCount;
            ViewBag.CartItemsCount = cartItemsCount;
            ViewBag.TotalDependencies = totalDependencies;
            ViewBag.CanDelete = bookingsCount == 0; // Can only delete if no bookings

            return View(trip);
        }


        // POST: /Admin/DeleteTripConfirmed
        [HttpPost, ActionName("DeleteTrip")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTripConfirmed(int id)
        {
            var trip = await _context.Trips
                .Include(t => t.Bookings)
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            // CRITICAL CHECK: Prevent deletion if trip has bookings
            var bookingsCount = trip.Bookings?.Count ?? 0;
            if (bookingsCount > 0)
            {
                TempData["Error"] = $"❌ Cannot delete trip '{trip.PackageName}' because it has {bookingsCount} booking(s). " +
                                   "Trips with bookings cannot be deleted to maintain data integrity.";
                return RedirectToAction(nameof(ManageTrips));
            }

            try
            {
                // Safe to delete - no bookings exist
                // Cascade will handle: Images, WaitingList, Reviews, CartItems, ReminderRules
                _context.Trips.Remove(trip);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"✓ Trip '{trip.PackageName}' deleted successfully!";
                return RedirectToAction(nameof(ManageTrips));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"❌ Error deleting trip: {ex.Message}";
                return RedirectToAction(nameof(ManageTrips));
            }
        }

        // GET: /Admin/SetDiscount
        public async Task<IActionResult> SetDiscount(int id)
        {
            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            var model = new TripDiscountViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                CurrentPrice = trip.OriginalPrice ?? trip.Price,
                DiscountedPrice = trip.Price,
                DiscountEndDate = trip.DiscountEndDate ?? DateTime.Now.AddDays(7)
            };

            return View(model);
        }

        // POST: /Admin/SetDiscount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDiscount(int id, TripDiscountViewModel model)
        {
            if (id != model.TripId)
            {
                return NotFound();
            }

            if (model.DiscountedPrice >= model.CurrentPrice)
            {
                ModelState.AddModelError("DiscountedPrice", "Discounted price must be less than current price");
            }

            if (model.DiscountEndDate <= DateTime.Now)
            {
                ModelState.AddModelError("DiscountEndDate", "Discount end date must be in the future");
            }

            if (model.DiscountEndDate > DateTime.Now.AddDays(7))
            {
                ModelState.AddModelError("DiscountEndDate", "Discount can only be active for up to 7 days");
            }

            if (ModelState.IsValid)
            {
                var trip = await _context.Trips.FindAsync(id);

                if (trip == null)
                {
                    return NotFound();
                }

                if (!trip.OriginalPrice.HasValue)
                {
                    trip.OriginalPrice = trip.Price;
                }

                trip.Price = model.DiscountedPrice;
                trip.DiscountEndDate = model.DiscountEndDate;
                trip.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                TempData["Success"] = $"Discount applied to '{trip.PackageName}'!";
                return RedirectToAction(nameof(ManageTrips));
            }

            return View(model);
        }

        // POST: /Admin/RemoveDiscount
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDiscount(int id)
        {
            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (trip.OriginalPrice.HasValue)
            {
                trip.Price = trip.OriginalPrice.Value;
                trip.OriginalPrice = null;
                trip.DiscountEndDate = null;
                trip.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                TempData["Success"] = $"Discount removed from '{trip.PackageName}'!";
            }

            return RedirectToAction(nameof(ManageTrips));
        }

        #endregion

        #region User Management

        // GET: /Admin/ManageUsers
        public async Task<IActionResult> ManageUsers(string? searchQuery, string? roleFilter, int page = 1)
        {
            var query = _userManager.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(u =>
                    u.Email!.Contains(searchQuery) ||
                    u.FirstName.Contains(searchQuery) ||
                    u.LastName.Contains(searchQuery));
            }

            var pageSize = 20;
            var totalUsers = await query.CountAsync();

            var users = await query
                .OrderByDescending(u => u.RegistrationDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userViewModels = new List<UserViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                if (!string.IsNullOrEmpty(roleFilter) && !roles.Contains(roleFilter))
                {
                    continue;
                }

                var bookingsCount = await _context.Bookings.CountAsync(b => b.UserId == user.Id);

                userViewModels.Add(new UserViewModel
                {
                    Id = user.Id,
                    Email = user.Email ?? "",
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    RegistrationDate = user.RegistrationDate,
                    IsActive = user.IsActive,
                    Roles = roles.ToList(),
                    TotalBookings = bookingsCount
                });
            }

            var viewModel = new UserListViewModel
            {
                Users = userViewModels,
                TotalUsers = totalUsers,
                CurrentPage = page,
                PageSize = pageSize,
                SearchQuery = searchQuery,
                RoleFilter = roleFilter
            };

            return View(viewModel);
        }

        // GET: /Admin/UserDetails/id
        public async Task<IActionResult> UserDetails(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var bookings = await _context.Bookings
                .Include(b => b.Trip)
                .Where(b => b.UserId == id)
                .OrderByDescending(b => b.BookingDate)
                .Select(b => new BookingViewModel
                {
                    BookingId = b.BookingId,
                    TripName = b.Trip != null ? b.Trip.PackageName : "Unknown",
                    BookingDate = b.BookingDate,
                    TripStartDate = b.Trip != null ? b.Trip.StartDate : DateTime.MinValue,
                    TripEndDate = b.Trip != null ? b.Trip.EndDate : DateTime.MinValue,
                    NumberOfRooms = b.NumberOfRooms,
                    TotalPrice = b.TotalPrice,
                    Status = b.Status,
                    IsPaid = b.IsPaid
                })
                .ToListAsync();

            var viewModel = new UserDetailsViewModel
            {
                Id = user.Id,
                Email = user.Email ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                DateOfBirth = user.DateOfBirth,
                Address = user.Address,
                RegistrationDate = user.RegistrationDate,
                IsActive = user.IsActive,
                Roles = roles.ToList(),
                Bookings = bookings
            };

            return View(viewModel);
        }

        // POST: /Admin/DeleteUser/id
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                TempData["Error"] = "Cannot delete admin users!";
                return RedirectToAction(nameof(ManageUsers));
            }

            var bookings = await _context.Bookings.Where(b => b.UserId == id).ToListAsync();
            _context.Bookings.RemoveRange(bookings);

            var cartItems = await _context.CartItems.Where(c => c.UserId == id).ToListAsync();
            _context.CartItems.RemoveRange(cartItems);

            var waitingListEntries = await _context.WaitingListEntries.Where(w => w.UserId == id).ToListAsync();
            _context.WaitingListEntries.RemoveRange(waitingListEntries);

            var reviews = await _context.Reviews.Where(r => r.UserId == id).ToListAsync();
            _context.Reviews.RemoveRange(reviews);

            await _context.SaveChangesAsync();

            var result = await _userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                TempData["Success"] = $"User '{user.Email}' and all related data have been deleted.";
            }
            else
            {
                TempData["Error"] = "Failed to delete user: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        // POST: /Admin/ToggleUserStatus/id
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            TempData["Success"] = $"User '{user.Email}' status changed to {(user.IsActive ? "Active" : "Inactive")}";
            return RedirectToAction(nameof(ManageUsers));
        }

        // POST: /Admin/ChangeUserRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeUserRole(string userId, string newRole)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return NotFound();
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, newRole);

            TempData["Success"] = $"User '{user.Email}' role changed to {newRole}";
            return RedirectToAction(nameof(UserDetails), new { id = userId });
        }

        #endregion

        #region Booking Management

        // GET: /Admin/ManageBookings
        public async Task<IActionResult> ManageBookings(BookingStatus? status, string? searchQuery, int page = 1)
        {
            var query = _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Trip)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(b => b.Status == status.Value);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(b =>
                    (b.User != null && (b.User.Email!.Contains(searchQuery) ||
                                        b.User.FirstName.Contains(searchQuery) ||
                                        b.User.LastName.Contains(searchQuery))) ||
                    (b.Trip != null && b.Trip.PackageName.Contains(searchQuery)));
            }

            var pageSize = 20;
            var totalBookings = await query.CountAsync();

            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new AdminBookingViewModel
                {
                    BookingId = b.BookingId,
                    UserId = b.UserId,
                    UserName = b.User != null ? $"{b.User.FirstName} {b.User.LastName}" : "Unknown",
                    UserEmail = b.User != null ? b.User.Email! : "Unknown",
                    TripId = b.TripId,
                    TripName = b.Trip != null ? b.Trip.PackageName : "Unknown",
                    BookingDate = b.BookingDate,
                    TripStartDate = b.Trip != null ? b.Trip.StartDate : DateTime.MinValue,
                    NumberOfRooms = b.NumberOfRooms,
                    TotalPrice = b.TotalPrice,
                    Status = b.Status,
                    IsPaid = b.IsPaid,
                    PaymentDate = b.PaymentDate
                })
                .ToListAsync();

            var viewModel = new BookingListViewModel
            {
                Bookings = bookings,
                TotalBookings = totalBookings,
                CurrentPage = page,
                PageSize = pageSize,
                StatusFilter = status,
                SearchQuery = searchQuery
            };

            return View(viewModel);
        }

        #endregion

        #region Waiting List Management

        // GET: /Admin/WaitingList
        public async Task<IActionResult> WaitingList()
        {
            var entries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified)
                .OrderBy(w => w.TripId)
                .ThenBy(w => w.Position)
                .Select(w => new AdminWaitingListItemViewModel
                {
                    WaitingListEntryId = w.WaitingListEntryId,
                    UserName = w.User != null ? $"{w.User.FirstName} {w.User.LastName}" : "Unknown",
                    UserEmail = w.User != null ? w.User.Email! : "Unknown",
                    TripName = w.Trip != null ? w.Trip.PackageName : "Unknown",
                    TripId = w.TripId,
                    Position = w.Position,
                    JoinedDate = w.JoinedDate,
                    RoomsRequested = w.RoomsRequested,
                    Status = w.Status,
                    IsNotified = w.IsNotified,
                    NotificationDate = w.NotificationDate,
                    NotificationExpiresAt = w.NotificationExpiresAt
                })
                .ToListAsync();

            var viewModel = new AdminWaitingListViewModel
            {
                Entries = entries,
                TotalEntries = entries.Count
            };

            return View(viewModel);
        }

        // POST: /Admin/NotifyWaitingUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyWaitingUser(int id)
        {
            var entry = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .FirstOrDefaultAsync(w => w.WaitingListEntryId == id);

            if (entry == null)
            {
                return NotFound();
            }

            entry.IsNotified = true;
            entry.NotificationDate = DateTime.Now;
            entry.NotificationExpiresAt = DateTime.Now.AddHours(24);
            entry.Status = WaitingListStatus.Notified;

            await _context.SaveChangesAsync();

            // Send notification email
            await SendWaitingListNotificationEmail(entry);

            TempData["Success"] = $"User '{entry.User?.Email}' has been notified!";
            return RedirectToAction(nameof(WaitingList));
        }

        #endregion

        #region Helper Methods

        private async Task ProcessWaitingListAfterRoomsAdded(int tripId, int availableRooms)
        {
            var eligibleEntry = await FindEligibleWaitingListEntry(tripId, availableRooms);

            if (eligibleEntry != null)
            {
                eligibleEntry.Status = WaitingListStatus.Notified;
                eligibleEntry.IsNotified = true;
                eligibleEntry.NotificationDate = DateTime.Now;
                eligibleEntry.NotificationExpiresAt = DateTime.Now.AddHours(24);

                await _context.SaveChangesAsync();

                await SendWaitingListNotificationEmail(eligibleEntry);
                await SendPositionUpdateEmails(tripId, eligibleEntry.WaitingListEntryId);
            }
        }

        private async Task<WaitingListEntry?> FindEligibleWaitingListEntry(int tripId, int availableRooms)
        {
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                if (entry.RoomsRequested > availableRooms)
                {
                    continue;
                }

                var activeBookingsCount = await _context.Bookings
                    .CountAsync(b => b.UserId == entry.UserId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now);

                if (activeBookingsCount >= 3)
                {
                    continue;
                }

                return entry;
            }

            return null;
        }

        private async Task SendWaitingListNotificationEmail(WaitingListEntry entry)
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

                var subject = $"Good News! A spot opened for {tripName}";

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

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send waiting list notification email: {ex.Message}");
            }
        }

        private async Task SendPositionUpdateEmails(int tripId, int excludeEntryId)
        {
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                await SendPositionUpdateEmail(entry);
            }
        }

        private async Task SendPositionUpdateEmail(WaitingListEntry entry)
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

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send position update email: {ex.Message}");
            }
        }

        #endregion
    }
}