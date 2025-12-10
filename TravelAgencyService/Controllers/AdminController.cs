using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Trips.Add(trip);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Trip '{trip.PackageName}' created successfully!";
                return RedirectToAction(nameof(ManageTrips));
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

            return View(model);
        }

        // POST: /Admin/EditTrip/5
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

                trip.PackageName = model.PackageName;
                trip.Destination = model.Destination;
                trip.Country = model.Country;
                trip.StartDate = model.StartDate;
                trip.EndDate = model.EndDate;
                trip.Price = model.Price;
                trip.TotalRooms = model.TotalRooms;
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

                TempData["Success"] = $"Trip '{trip.PackageName}' updated successfully!";
                return RedirectToAction(nameof(ManageTrips));
            }

            return View(model);
        }

        // GET: /Admin/DeleteTrip/5
        public async Task<IActionResult> DeleteTrip(int id)
        {
            var trip = await _context.Trips
                .Include(t => t.Bookings)
                .FirstOrDefaultAsync(t => t.TripId == id);

            if (trip == null)
            {
                return NotFound();
            }

            return View(trip);
        }

        // POST: /Admin/DeleteTrip/5
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

            // Check if there are active bookings
            if (trip.Bookings != null && trip.Bookings.Any(b => b.Status == BookingStatus.Confirmed))
            {
                TempData["Error"] = "Cannot delete trip with active bookings. Cancel all bookings first.";
                return RedirectToAction(nameof(ManageTrips));
            }

            _context.Trips.Remove(trip);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Trip '{trip.PackageName}' deleted successfully!";
            return RedirectToAction(nameof(ManageTrips));
        }

        // GET: /Admin/SetDiscount/5
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

        // POST: /Admin/SetDiscount/5
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

                // Store original price if not already stored
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

        // POST: /Admin/RemoveDiscount/5
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
                .Where(w => w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.TripId)
                .ThenBy(w => w.Position)
                .Select(w => new WaitingListItemViewModel
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
                    IsNotified = w.IsNotified
                })
                .ToListAsync();

            var viewModel = new WaitingListViewModel
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

            // TODO: Send email notification here

            TempData["Success"] = $"User '{entry.User?.Email}' has been notified about availability for '{entry.Trip?.PackageName}'";
            return RedirectToAction(nameof(WaitingList));
        }

        #endregion
    }
}