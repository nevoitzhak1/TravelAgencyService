using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;
using TravelAgencyService.Services;
using TravelAgencyService.Services.Email;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly PdfService _pdfService;

        public CartController(ApplicationDbContext context, IEmailSender emailSender, PdfService pdfService)
        {
            _context = context;
            _emailSender = emailSender;
            _pdfService = pdfService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.AddedDate)
                .ToListAsync();

            var viewModel = new CartViewModel
            {
                Items = cartItems.Select(c => new CartItemViewModel
                {
                    CartItemId = c.CartItemId,
                    TripId = c.TripId,
                    PackageName = c.Trip?.PackageName ?? "",
                    Destination = c.Trip?.Destination ?? "",
                    Country = c.Trip?.Country ?? "",
                    StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                    PricePerPerson = c.Trip?.Price ?? 0,
                    OriginalPrice = c.Trip?.OriginalPrice,
                    IsOnSale = c.Trip?.IsOnSale ?? false,
                    NumberOfRooms = c.NumberOfRooms,
                    MainImageUrl = c.Trip?.MainImageUrl,
                    AvailableRooms = c.Trip?.AvailableRooms ?? 0,
                    IsStillAvailable = c.Trip != null &&
                                       c.Trip.IsVisible &&
                                       c.Trip.AvailableRooms >= c.NumberOfRooms &&
                                       c.Trip.StartDate > DateTime.Now,
                    AddedDate = c.AddedDate
                }).ToList()
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Add(int id, int rooms = 1)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existingItem = await _context.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.TripId == id);

            if (existingItem != null)
            {
                TempData["Info"] = "This trip is already in your cart.";
                return RedirectToAction("Index");
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (!trip.IsVisible || trip.AvailableRooms <= 0 || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = "This trip is not available for booking.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            // ========== AGE RESTRICTION CHECK ==========
            var ageCheck = await CheckAgeRestriction(trip, userId!);
            if (!ageCheck.IsAllowed)
            {
                TempData["Error"] = ageCheck.ErrorMessage;
                return RedirectToAction("Index", "Trip");
            }
            // ===========================================

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(id, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id });
            }

            var cartItem = new CartItem
            {
                UserId = userId!,
                TripId = id,
                NumberOfRooms = Math.Min(rooms, trip.AvailableRooms),
                PriceWhenAdded = trip.Price,
                AddedDate = DateTime.Now
            };

            _context.CartItems.Add(cartItem);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{trip.PackageName}' has been added to your cart.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int numberOfRooms)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItem = await _context.CartItems
                .Include(c => c.Trip)
                .FirstOrDefaultAsync(c => c.CartItemId == cartItemId && c.UserId == userId);

            if (cartItem == null)
            {
                return NotFound();
            }

            if (cartItem.Trip != null && numberOfRooms > cartItem.Trip.AvailableRooms)
            {
                TempData["Error"] = $"Only {cartItem.Trip.AvailableRooms} rooms are available.";
                return RedirectToAction("Index");
            }

            if (numberOfRooms < 1) numberOfRooms = 1;
            else if (numberOfRooms > 10) numberOfRooms = 10;

            cartItem.NumberOfRooms = numberOfRooms;
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItem = await _context.CartItems
                .Include(c => c.Trip)
                .FirstOrDefaultAsync(c => c.CartItemId == id && c.UserId == userId);

            if (cartItem == null)
            {
                return NotFound();
            }

            var tripName = cartItem.Trip?.PackageName ?? "Trip";

            _context.CartItems.Remove(cartItem);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{tripName}' has been removed from your cart.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var cartItems = await _context.CartItems
                .Where(c => c.UserId == userId)
                .ToListAsync();

            _context.CartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your cart has been cleared.";
            return RedirectToAction("Index");
        }

        // GET: /Cart/Checkout OR /Cart/Checkout?tripId=5&rooms=1 (Single Trip)
        public async Task<IActionResult> Checkout(int? tripId, int rooms = 1)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // === SINGLE TRIP CHECKOUT (Buy Now) ===
            if (tripId.HasValue)
            {
                var trip = await _context.Trips.FindAsync(tripId.Value);
                if (trip == null || !trip.IsVisible)
                {
                    TempData["Error"] = "Trip not found.";
                    return RedirectToAction("Index", "Trip");
                }

                if (trip.AvailableRooms < rooms)
                {
                    TempData["Error"] = "Sorry, the rooms are no longer available.";
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                if (trip.StartDate <= DateTime.Now)
                {
                    TempData["Error"] = "This trip is no longer available.";
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                // ========== AGE RESTRICTION CHECK ==========
                var ageCheck = await CheckAgeRestriction(trip, userId!);
                if (!ageCheck.IsAllowed)
                {
                    TempData["Error"] = ageCheck.ErrorMessage;
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }
                // ===========================================

                // Check waiting list priority
                var priorityCheck = await CheckWaitingListPriority(tripId.Value, userId!);
                if (!priorityCheck.CanProceed)
                {
                    TempData["Error"] = priorityCheck.Message;
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                // Check max bookings
                var activeBookingsCount = await _context.Bookings
                    .CountAsync(b => b.UserId == userId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now);

                if (activeBookingsCount >= 3)
                {
                    TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
                    return RedirectToAction("Details", "Trip", new { id = tripId });
                }

                var singleViewModel = new CartCheckoutViewModel
                {
                    IsSingleCheckout = true,
                    SingleTripId = tripId.Value,
                    SingleRooms = rooms,
                    Items = new List<CartItemViewModel>
                    {
                        new CartItemViewModel
                        {
                            TripId = trip.TripId,
                            PackageName = trip.PackageName,
                            Destination = trip.Destination,
                            Country = trip.Country,
                            StartDate = trip.StartDate,
                            EndDate = trip.EndDate,
                            PricePerPerson = trip.Price,
                            OriginalPrice = trip.OriginalPrice,
                            IsOnSale = trip.IsOnSale,
                            NumberOfRooms = rooms,
                            MainImageUrl = trip.MainImageUrl,
                            AvailableRooms = trip.AvailableRooms,
                            IsStillAvailable = trip.AvailableRooms >= rooms && trip.StartDate > DateTime.Now
                        }
                    }
                };

                return View(singleViewModel);
            }

            // === CART CHECKOUT (existing logic) ===
            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            var unavailableItems = cartItems.Where(c =>
                c.Trip == null ||
                !c.Trip.IsVisible ||
                c.Trip.AvailableRooms < c.NumberOfRooms ||
                c.Trip.StartDate <= DateTime.Now).ToList();

            if (unavailableItems.Any())
            {
                TempData["Error"] = "Some items in your cart are no longer available.";
                return RedirectToAction("Index");
            }

            // ========== AGE RESTRICTION CHECK FOR CART ITEMS ==========
            foreach (var item in cartItems)
            {
                if (item.Trip != null)
                {
                    var ageCheck = await CheckAgeRestriction(item.Trip, userId!);
                    if (!ageCheck.IsAllowed)
                    {
                        TempData["Error"] = $"'{item.Trip.PackageName}': {ageCheck.ErrorMessage}";
                        return RedirectToAction("Index");
                    }
                }
            }
            // ==========================================================

            // Check waiting list priority for each item
            foreach (var item in cartItems)
            {
                var priorityCheck2 = await CheckWaitingListPriority(item.TripId, userId!);
                if (!priorityCheck2.CanProceed)
                {
                    TempData["Error"] = $"'{item.Trip?.PackageName}': {priorityCheck2.Message}";
                    return RedirectToAction("Index");
                }
            }

            var activeCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeCount + cartItems.Count > 3)
            {
                TempData["Error"] = $"You can only have 3 active bookings. You currently have {activeCount}.";
                return RedirectToAction("Index");
            }

            var viewModel = new CartCheckoutViewModel
            {
                IsSingleCheckout = false,
                Items = cartItems.Select(c => new CartItemViewModel
                {
                    CartItemId = c.CartItemId,
                    TripId = c.TripId,
                    PackageName = c.Trip?.PackageName ?? "",
                    Destination = c.Trip?.Destination ?? "",
                    Country = c.Trip?.Country ?? "",
                    StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                    EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                    PricePerPerson = c.Trip?.Price ?? 0,
                    NumberOfRooms = c.NumberOfRooms,
                    MainImageUrl = c.Trip?.MainImageUrl
                }).ToList()
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(CartCheckoutViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // === SINGLE TRIP CHECKOUT (Buy Now) ===
            if (model.IsSingleCheckout && model.SingleTripId.HasValue && model.SingleRooms.HasValue)
            {
                var trip = await _context.Trips.FindAsync(model.SingleTripId.Value);
                if (trip == null || !trip.IsVisible)
                {
                    TempData["Error"] = "Trip not found.";
                    return RedirectToAction("Index", "Trip");
                }

                if (trip.AvailableRooms < model.SingleRooms.Value)
                {
                    TempData["Error"] = "Sorry, the rooms are no longer available.";
                    return RedirectToAction("Details", "Trip", new { id = trip.TripId });
                }

                // ========== AGE RESTRICTION CHECK ==========
                var ageCheck = await CheckAgeRestriction(trip, userId!);
                if (!ageCheck.IsAllowed)
                {
                    TempData["Error"] = ageCheck.ErrorMessage;
                    return RedirectToAction("Index", "Trip");
                }
                // ===========================================

                // Validate expiry date
                var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

                if (expiryDate < currentDate)
                {
                    ModelState.AddModelError("ExpiryMonth", "Card has expired");
                }

                if (!ModelState.IsValid)
                {
                    model.Items = new List<CartItemViewModel>
                    {
                        new CartItemViewModel
                        {
                            TripId = trip.TripId,
                            PackageName = trip.PackageName,
                            Destination = trip.Destination,
                            Country = trip.Country,
                            StartDate = trip.StartDate,
                            EndDate = trip.EndDate,
                            PricePerPerson = trip.Price,
                            OriginalPrice = trip.OriginalPrice,
                            IsOnSale = trip.IsOnSale,
                            NumberOfRooms = model.SingleRooms.Value,
                            MainImageUrl = trip.MainImageUrl,
                            AvailableRooms = trip.AvailableRooms,
                            IsStillAvailable = trip.AvailableRooms >= model.SingleRooms.Value && trip.StartDate > DateTime.Now
                        }
                    };
                    return View(model);
                }

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Burn inventory + create booking paid
                    trip.AvailableRooms -= model.SingleRooms.Value;
                    trip.TimesBooked++;
                    trip.UpdatedAt = DateTime.Now;

                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    var booking = new Booking
                    {
                        UserId = userId!,
                        TripId = trip.TripId,
                        NumberOfRooms = model.SingleRooms.Value,
                        TotalPrice = trip.Price * model.SingleRooms.Value,
                        BookingDate = DateTime.Now,
                        Status = BookingStatus.Confirmed,
                        IsPaid = true,
                        PaymentDate = DateTime.Now,
                        CardLastFourDigits = cardLastFour,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    // Update waiting list if user was in it
                    var userWaitingEntry = await _context.WaitingListEntries
                        .FirstOrDefaultAsync(w => w.UserId == userId &&
                                                  w.TripId == trip.TripId &&
                                                  (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                    if (userWaitingEntry != null)
                    {
                        var removedPosition = userWaitingEntry.Position;
                        userWaitingEntry.Status = WaitingListStatus.Booked;
                        await AdvanceWaitingListPositions(trip.TripId, removedPosition);
                    }

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync();

                    // ✅ NEW: Notify next person in waiting list if rooms still available
                    await NotifyNextInWaitingList(trip.TripId);

                    await transaction.CommitAsync();

                    // Send confirmation email
                    await SendBookingConfirmationEmail(booking.BookingId);

                    TempData["Success"] = "Payment successful! Your booking is confirmed.";
                    return RedirectToAction("Confirmation", "Booking", new { id = booking.BookingId });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "Payment failed. Please try again.";
                    return RedirectToAction("Checkout", "Cart", new { tripId = model.SingleTripId, rooms = model.SingleRooms });
                }
            }

            // === CART CHECKOUT (existing logic) ===
            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            // ========== AGE RESTRICTION CHECK FOR CART ITEMS ==========
            foreach (var item in cartItems)
            {
                if (item.Trip != null)
                {
                    var ageCheck = await CheckAgeRestriction(item.Trip, userId!);
                    if (!ageCheck.IsAllowed)
                    {
                        TempData["Error"] = $"'{item.Trip.PackageName}': {ageCheck.ErrorMessage}";
                        return RedirectToAction("Index");
                    }
                }
            }
            // ==========================================================

            // Check waiting list priority for each item before processing
            foreach (var item in cartItems)
            {
                var priorityCheck = await CheckWaitingListPriority(item.TripId, userId!);
                if (!priorityCheck.CanProceed)
                {
                    TempData["Error"] = $"'{item.Trip?.PackageName}': {priorityCheck.Message}";
                    return RedirectToAction("Index");
                }
            }

            var currentDate2 = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate2 = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate2 < currentDate2)
            {
                ModelState.AddModelError("ExpiryMonth", "Card has expired");
            }

            if (!string.IsNullOrEmpty(model.CardNumber))
            {
                model.CardNumber = model.CardNumber.Replace(" ", "");
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var bookingIds = new List<int>();
                    var tripsToNotify = new List<int>(); // ✅ NEW: Track trips that need waiting list notification
                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    foreach (var item in cartItems)
                    {
                        if (item.Trip == null) continue;

                        var trip = await _context.Trips.FindAsync(item.TripId);
                        if (trip == null || trip.AvailableRooms < item.NumberOfRooms)
                        {
                            await transaction.RollbackAsync();
                            TempData["Error"] = $"'{item.Trip.PackageName}' is no longer available.";
                            return RedirectToAction("Index");
                        }

                        var booking = new Booking
                        {
                            UserId = userId!,
                            TripId = item.TripId,
                            NumberOfRooms = item.NumberOfRooms,
                            TotalPrice = trip.Price * item.NumberOfRooms,
                            BookingDate = DateTime.Now,
                            Status = BookingStatus.Confirmed,
                            IsPaid = true,
                            PaymentDate = DateTime.Now,
                            CardLastFourDigits = cardLastFour,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        trip.AvailableRooms -= item.NumberOfRooms;
                        trip.TimesBooked++;
                        trip.UpdatedAt = DateTime.Now;

                        // Update waiting list if user was in it
                        var userWaitingEntry = await _context.WaitingListEntries
                            .FirstOrDefaultAsync(w => w.UserId == userId &&
                                                      w.TripId == item.TripId &&
                                                      (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                        if (userWaitingEntry != null)
                        {
                            var removedPosition = userWaitingEntry.Position;
                            userWaitingEntry.Status = WaitingListStatus.Booked;
                            await AdvanceWaitingListPositions(item.TripId, removedPosition);
                            tripsToNotify.Add(item.TripId); // ✅ NEW: Mark for notification
                        }

                        _context.Bookings.Add(booking);
                        await _context.SaveChangesAsync();
                        bookingIds.Add(booking.BookingId);
                    }

                    _context.CartItems.RemoveRange(cartItems);
                    await _context.SaveChangesAsync();

                    // ✅ NEW: Notify next person in waiting list for each trip
                    foreach (var tripId in tripsToNotify.Distinct())
                    {
                        await NotifyNextInWaitingList(tripId);
                    }

                    await transaction.CommitAsync();

                    // Send confirmation emails
                    foreach (var bookingId in bookingIds)
                    {
                        await SendBookingConfirmationEmail(bookingId);
                    }

                    TempData["Success"] = $"Payment successful! {bookingIds.Count} booking(s) confirmed.";
                    return RedirectToAction("MyBookings", "Booking");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "An error occurred while processing your payment.";
                    return RedirectToAction("Index");
                }
            }

            model.Items = cartItems.Select(c => new CartItemViewModel
            {
                CartItemId = c.CartItemId,
                TripId = c.TripId,
                PackageName = c.Trip?.PackageName ?? "",
                Destination = c.Trip?.Destination ?? "",
                Country = c.Trip?.Country ?? "",
                StartDate = c.Trip?.StartDate ?? DateTime.MinValue,
                EndDate = c.Trip?.EndDate ?? DateTime.MinValue,
                PricePerPerson = c.Trip?.Price ?? 0,
                NumberOfRooms = c.NumberOfRooms,
                MainImageUrl = c.Trip?.MainImageUrl
            }).ToList();

            return View(model);
        }

        // GET: /Cart/BuyNow/5 - Redirects to Single Checkout
        public async Task<IActionResult> BuyNow(int id, int rooms = 1)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount >= 3)
            {
                TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var trip = await _context.Trips.FindAsync(id);

            if (trip == null)
            {
                return NotFound();
            }

            if (!trip.IsVisible || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = "This trip is not available for booking.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            if (trip.AvailableRooms <= 0)
            {
                TempData["Error"] = "This trip is fully booked. You can join the waiting list.";
                return RedirectToAction("JoinWaitingList", "Booking", new { id });
            }

            // ========== AGE RESTRICTION CHECK ==========
            var ageCheck = await CheckAgeRestriction(trip, userId!);
            if (!ageCheck.IsAllowed)
            {
                TempData["Error"] = ageCheck.ErrorMessage;
                return RedirectToAction("Index", "Trip");
            }
            // ===========================================

            // Check waiting list priority
            var priorityCheck = await CheckWaitingListPriority(id, userId!);
            if (!priorityCheck.CanProceed)
            {
                TempData["Error"] = priorityCheck.Message;
                return RedirectToAction("Details", "Trip", new { id });
            }

            // Redirect directly to Checkout with tripId (Single Checkout)
            return RedirectToAction("Checkout", "Cart", new { tripId = id, rooms = Math.Min(rooms, trip.AvailableRooms) });
        }

        // POST: BuyNow is no longer needed - we use Checkout POST for single trips
        // Keeping it for backward compatibility but it redirects
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuyNow(BuyNowViewModel model)
        {
            // Redirect to the new unified checkout
            return RedirectToAction("Checkout", "Cart", new { tripId = model.TripId, rooms = model.NumberOfRooms });
        }

        #region Helper Methods

        private async Task<(bool IsAllowed, string? ErrorMessage)> CheckAgeRestriction(Trip trip, string userId)
        {
            // If no age restrictions, allow
            if (!trip.MinimumAge.HasValue && !trip.MaximumAge.HasValue)
                return (true, null);

            var user = await _context.Users.FindAsync(userId);

            // If user has no date of birth but trip has age restrictions
            if (user?.DateOfBirth == null || user.DateOfBirth.Value.Year < 1900)
            {
                return (false, "This trip has age restrictions. Please update your date of birth in your profile to proceed.");
            }

            // Calculate exact age
            var today = DateTime.Today;
            var birthDate = user.DateOfBirth.Value;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;

            // Build age range string
            string ageRange;
            if (trip.MinimumAge.HasValue && trip.MaximumAge.HasValue)
                ageRange = $"{trip.MinimumAge}-{trip.MaximumAge}";
            else if (trip.MinimumAge.HasValue)
                ageRange = $"{trip.MinimumAge}+";
            else
                ageRange = $"up to {trip.MaximumAge}";

            // Check minimum age
            if (trip.MinimumAge.HasValue && age < trip.MinimumAge.Value)
            {
                return (false, $"This trip is for ages {ageRange}. Based on your date of birth, you do not meet the age requirement and cannot book this trip.");
            }

            // Check maximum age
            if (trip.MaximumAge.HasValue && age > trip.MaximumAge.Value)
            {
                return (false, $"This trip is for ages {ageRange}. Based on your date of birth, you do not meet the age requirement and cannot book this trip.");
            }

            return (true, null);
        }

        private async Task<(bool CanProceed, string Message)> CheckWaitingListPriority(int tripId, string currentUserId)
        {
            var notifiedEntry = await _context.WaitingListEntries
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Notified &&
                            w.NotificationExpiresAt > DateTime.Now)
                .FirstOrDefaultAsync();

            if (notifiedEntry == null)
            {
                return (true, string.Empty);
            }

            if (notifiedEntry.UserId == currentUserId)
            {
                return (true, string.Empty);
            }

            return (false, "Someone from the waiting list currently has priority to book. Please try again later.");
        }

        private async Task AdvanceWaitingListPositions(int tripId, int removedPosition)
        {
            var entriesToAdvance = await _context.WaitingListEntries
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.Position > removedPosition)
                .ToListAsync();

            foreach (var entry in entriesToAdvance)
            {
                entry.Position--;
            }
        }

        /// <summary>
        /// ✅ NEW: Notify the next eligible person in the waiting list after a booking is completed
        /// </summary>
        private async Task NotifyNextInWaitingList(int tripId)
        {
            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null || trip.AvailableRooms <= 0) return;

            // Check if someone is already notified and their window hasn't expired
            var alreadyNotified = await _context.WaitingListEntries
                .AnyAsync(w => w.TripId == tripId &&
                              w.Status == WaitingListStatus.Notified &&
                              w.NotificationExpiresAt > DateTime.Now);

            if (alreadyNotified) return; // Someone already has priority

            // Find next eligible person (FIFO order)
            var waitingEntries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Where(w => w.TripId == tripId && w.Status == WaitingListStatus.Waiting)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in waitingEntries)
            {
                // Check if user requested more rooms than available
                if (entry.RoomsRequested > trip.AvailableRooms)
                {
                    continue;
                }

                // Check if user already has 3 active bookings
                var activeBookingsCount = await _context.Bookings
                    .CountAsync(b => b.UserId == entry.UserId &&
                                     b.Status == BookingStatus.Confirmed &&
                                     b.Trip!.StartDate > DateTime.Now);

                if (activeBookingsCount >= 3)
                {
                    continue;
                }

                // Found eligible person - calculate booking window
                int daysUntilTrip = (trip.StartDate.Date - DateTime.Now.Date).Days;
                if (daysUntilTrip < 0) daysUntilTrip = 0;

                var totalWaiting = await _context.WaitingListEntries
                    .CountAsync(w => w.TripId == tripId &&
                                    (w.Status == WaitingListStatus.Waiting || w.Status == WaitingListStatus.Notified));

                int bookingWindowHours = WaitingListController.CalculateBookingWindowHours(daysUntilTrip, totalWaiting);

                // Notify the user
                entry.Status = WaitingListStatus.Notified;
                entry.IsNotified = true;
                entry.NotificationDate = DateTime.Now;
                entry.NotificationExpiresAt = DateTime.Now.AddHours(bookingWindowHours);

                await _context.SaveChangesAsync();

                // Send notification email
                await SendWaitingListNotificationEmail(entry, trip, bookingWindowHours);

                // Send position update emails to others
                await SendWaitingListPositionUpdateEmails(tripId, entry.WaitingListEntryId);

                break; // Only notify one person at a time
            }
        }

        /// <summary>
        /// ✅ NEW: Send email to user who got their turn from waiting list
        /// </summary>
        private async Task SendWaitingListNotificationEmail(WaitingListEntry entry, Trip trip, int bookingWindowHours)
        {
            try
            {
                var userEmail = entry.User?.Email;
                if (string.IsNullOrEmpty(userEmail)) return;

                var userName = entry.User?.FirstName ?? "Traveler";
                var bookingWindowText = WaitingListController.FormatBookingWindow(bookingWindowHours);

                var subject = $"A spot opened for {trip.PackageName} - Book Now!";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #11998e 0%, #38ef7d 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Your Turn Has Come!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Great news! A spot has opened up for the trip you were waiting for:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #11998e;'>
                            <h2 style='color: #11998e; margin-top: 0;'>{trip.PackageName}</h2>
                            <p><strong>Destination:</strong> {trip.Destination}, {trip.Country}</p>
                            <p><strong>Dates:</strong> {trip.StartDate:MMM dd} - {trip.EndDate:MMM dd, yyyy}</p>
                            <p><strong>Rooms Requested:</strong> {entry.RoomsRequested}</p>
                        </div>
                        
                        <div style='background: #fff3cd; padding: 15px; border-radius: 10px; margin: 20px 0;'>
                            <p style='margin: 0; color: #856404;'>
                                <strong>⏰ Important:</strong> You have <strong>{bookingWindowText}</strong> to complete your booking before the spot is offered to the next person in line.
                            </p>
                        </div>
                        
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='https://localhost:7232/Booking/Book/{trip.TripId}' 
                               style='background: #11998e; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; font-size: 18px; display: inline-block;'>
                                🎉 Book Now
                            </a>
                        </div>
                        
                        <p>Don't miss this opportunity!</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>© {DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                await _emailSender.SendAsync(userEmail, subject, htmlBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send waiting list notification email: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Send position update emails to remaining waiting list users
        /// </summary>
        private async Task SendWaitingListPositionUpdateEmails(int tripId, int excludeEntryId)
        {
            var entries = await _context.WaitingListEntries
                .Include(w => w.User)
                .Include(w => w.Trip)
                .Where(w => w.TripId == tripId &&
                            w.Status == WaitingListStatus.Waiting &&
                            w.WaitingListEntryId != excludeEntryId)
                .OrderBy(w => w.Position)
                .ToListAsync();

            foreach (var entry in entries)
            {
                try
                {
                    var userEmail = entry.User?.Email;
                    if (string.IsNullOrEmpty(userEmail)) continue;

                    var userName = entry.User?.FirstName ?? "Traveler";
                    var tripName = entry.Trip?.PackageName ?? "Your Trip";

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
                                <p><strong>Your new position:</strong> <span style='font-size: 28px; font-weight: bold; color: #667eea;'>#{entry.Position}</span></p>
                            </div>
                            
                            <p>We'll notify you as soon as a spot becomes available!</p>
                            
                            <p style='color: #888; font-size: 14px;'>
                                Best regards,<br>
                                Travel Agency Team
                            </p>
                        </div>
                        
                        <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                            <p style='margin: 0;'>© {DateTime.Now.Year} Travel Agency Service</p>
                        </div>
                    </div>";

                    await _emailSender.SendAsync(userEmail, subject, htmlBody);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send position update email: {ex.Message}");
                }
            }
        }

        private async Task SendBookingConfirmationEmail(int bookingId)
        {
            try
            {
                var booking = await _context.Bookings
                    .Include(b => b.Trip)
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.BookingId == bookingId);

                if (booking == null || booking.User == null || string.IsNullOrEmpty(booking.User.Email))
                    return;

                var userName = booking.User.FirstName ?? "Traveler";
                var tripName = booking.Trip?.PackageName ?? "Your Trip";
                var destination = booking.Trip?.Destination ?? "";
                var country = booking.Trip?.Country ?? "";
                var startDate = booking.Trip?.StartDate.ToString("MMMM dd, yyyy") ?? "";
                var endDate = booking.Trip?.EndDate.ToString("MMMM dd, yyyy") ?? "";

                var subject = $"Booking Confirmed - {tripName}";

                var htmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center;'>
                        <h1 style='color: white; margin: 0;'>Booking Confirmed!</h1>
                    </div>
                    
                    <div style='padding: 30px; background: #f9f9f9;'>
                        <p style='font-size: 18px;'>Hi {userName},</p>
                        
                        <p>Your booking has been confirmed! Here are the details:</p>
                        
                        <div style='background: white; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #667eea;'>
                            <h2 style='color: #667eea; margin-top: 0;'>{tripName}</h2>
                            <p><strong>Destination:</strong> {destination}, {country}</p>
                            <p><strong>Dates:</strong> {startDate} - {endDate}</p>
                            <p><strong>Rooms:</strong> {booking.NumberOfRooms}</p>
                            <p><strong>Total Paid:</strong> ${booking.TotalPrice:N2}</p>
                            <p><strong>Booking ID:</strong> #{booking.BookingId}</p>
                        </div>
                        
                        <p>Your PDF itinerary is attached to this email.</p>
                        
                        <p style='color: #888; font-size: 14px;'>
                            Best regards,<br>
                            Travel Agency Team
                        </p>
                    </div>
                    
                    <div style='background: #333; color: white; padding: 20px; text-align: center;'>
                        <p style='margin: 0;'>© {DateTime.Now.Year} Travel Agency Service</p>
                    </div>
                </div>";

                // Generate PDF
                byte[]? pdfBytes = null;
                try
                {
                    pdfBytes = _pdfService.GenerateItinerary(booking);
                }
                catch
                {
                    // Continue without PDF if generation fails
                }

                if (pdfBytes != null)
                {
                    var fileName = $"Itinerary_{tripName.Replace(" ", "_")}_{booking.BookingId}.pdf";
                    await _emailSender.SendWithAttachmentAsync(
                        booking.User.Email,
                        subject,
                        htmlBody,
                        pdfBytes,
                        fileName,
                        "application/pdf"
                    );
                }
                else
                {
                    await _emailSender.SendAsync(booking.User.Email, subject, htmlBody);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send booking confirmation email: {ex.Message}");
            }
        }

        #endregion
    }
}