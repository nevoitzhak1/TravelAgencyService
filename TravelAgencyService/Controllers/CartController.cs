using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CartController(ApplicationDbContext context)
        {
            _context = context;
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

        public async Task<IActionResult> Checkout()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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

            var activeBookingsCount = await _context.Bookings
                .CountAsync(b => b.UserId == userId &&
                                 b.Status == BookingStatus.Confirmed &&
                                 b.Trip!.StartDate > DateTime.Now);

            if (activeBookingsCount + cartItems.Count > 3)
            {
                TempData["Error"] = $"You can only have 3 active bookings. You currently have {activeBookingsCount}.";
                return RedirectToAction("Index");
            }

            var viewModel = new CartCheckoutViewModel
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

            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate < currentDate)
            {
                ModelState.AddModelError("ExpiryMonth", "Card has expired");
            }

            if (!string.IsNullOrEmpty(model.CardNumber))
            {
                model.CardNumber = model.CardNumber.Replace(" ", "");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    var (bookingIds, _) = await CreateBookingsFromCartAsync(
                        userId!,
                        markAsPaid: true,
                        cardLastFourDigits: cardLastFour,
                        ct: HttpContext.RequestAborted);

                    // מחיקת עגלה כמו קודם
                    var cartItemsToRemove = await _context.CartItems.Where(c => c.UserId == userId).ToListAsync();
                    _context.CartItems.RemoveRange(cartItemsToRemove);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"✅ Payment successful! {bookingIds.Count} booking(s) confirmed. You can view them in your email and under My Bookings.";
                    TempData["ConfirmedBookingIds"] = string.Join(",", bookingIds);
                    TempData["ConfirmedBookingsCount"] = bookingIds.Count;
                    return RedirectToAction("Confirmation", "Booking");

                }
                catch
                {
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
                TempData["Error"] = "This trip is fully booked.";
                return RedirectToAction("Details", "Trip", new { id });
            }

            var viewModel = new BuyNowViewModel
            {
                TripId = trip.TripId,
                PackageName = trip.PackageName,
                Destination = trip.Destination,
                Country = trip.Country,
                StartDate = trip.StartDate,
                EndDate = trip.EndDate,
                PricePerRoom = trip.Price,
                OriginalPrice = trip.OriginalPrice,
                IsOnSale = trip.IsOnSale,
                AvailableRooms = trip.AvailableRooms,
                MainImageUrl = trip.MainImageUrl,
                NumberOfRooms = Math.Min(rooms, trip.AvailableRooms)
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuyNow(BuyNowViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var currentDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var expiryDate = new DateTime(model.ExpiryYear, model.ExpiryMonth, 1);

            if (expiryDate < currentDate)
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
                    var trip = await _context.Trips.FindAsync(model.TripId);

                    if (trip == null)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "Trip not found.";
                        return RedirectToAction("Index", "Trip");
                    }

                    if (trip.AvailableRooms < model.NumberOfRooms)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = $"Sorry, only {trip.AvailableRooms} room(s) available. Someone else booked while you were checking out.";
                        return RedirectToAction("Details", "Trip", new { id = model.TripId });
                    }

                    var activeBookingsCount = await _context.Bookings
                        .CountAsync(b => b.UserId == userId &&
                                         b.Status == BookingStatus.Confirmed &&
                                         b.Trip!.StartDate > DateTime.Now);

                    if (activeBookingsCount >= 3)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "You cannot have more than 3 upcoming trips booked.";
                        return RedirectToAction("Details", "Trip", new { id = model.TripId });
                    }

                    var cardLastFour = model.CardNumber.Length >= 4
                        ? model.CardNumber.Substring(model.CardNumber.Length - 4)
                        : model.CardNumber;

                    var booking = new Booking
                    {
                        UserId = userId!,
                        TripId = model.TripId,
                        NumberOfRooms = model.NumberOfRooms,
                        TotalPrice = trip.Price * model.NumberOfRooms,
                        BookingDate = DateTime.Now,
                        Status = BookingStatus.Confirmed,
                        IsPaid = true,
                        PaymentDate = DateTime.Now,
                        CardLastFourDigits = cardLastFour,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    trip.AvailableRooms -= model.NumberOfRooms;
                    trip.TimesBooked++;
                    trip.UpdatedAt = DateTime.Now;

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    TempData["Success"] = "Payment successful! Your booking is confirmed.";
                    return RedirectToAction("Confirmation", "Booking", new { id = booking.BookingId });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "An error occurred. Please try again.";
                    return RedirectToAction("Details", "Trip", new { id = model.TripId });
                }
            }

            var tripData = await _context.Trips.FindAsync(model.TripId);
            if (tripData != null)
            {
                model.PackageName = tripData.PackageName;
                model.Destination = tripData.Destination;
                model.Country = tripData.Country;
                model.StartDate = tripData.StartDate;
                model.EndDate = tripData.EndDate;
                model.PricePerRoom = tripData.Price;
                model.OriginalPrice = tripData.OriginalPrice;
                model.IsOnSale = tripData.IsOnSale;
                model.AvailableRooms = tripData.AvailableRooms;
                model.MainImageUrl = tripData.MainImageUrl;
            }

            return View(model);
        }
        private async Task<(List<int> bookingIds, decimal totalAmount)> CreateBookingsFromCartAsync(string userId,bool markAsPaid,string? cardLastFourDigits,CancellationToken ct){
            var cartItems = await _context.CartItems
                .Include(c => c.Trip)
                .Where(c => c.UserId == userId)
                .ToListAsync(ct);

            if (!cartItems.Any())
                throw new InvalidOperationException("Cart is empty.");

            using var transaction = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                var bookingIds = new List<int>();
                decimal totalAmount = 0m;

                foreach (var item in cartItems)
                {
                    if (item.Trip == null) continue;

                    var trip = await _context.Trips.FindAsync(new object[] { item.TripId }, ct);
                    if (trip == null || trip.AvailableRooms < item.NumberOfRooms)
                        throw new InvalidOperationException($"'{item.Trip?.PackageName ?? "Trip"}' is no longer available.");

                    var amount = trip.Price * item.NumberOfRooms;
                    totalAmount += amount;

                    var booking = new Booking
                    {
                        UserId = userId,
                        TripId = item.TripId,
                        NumberOfRooms = item.NumberOfRooms,
                        TotalPrice = amount,
                        BookingDate = DateTime.Now,
                        Status = markAsPaid ? BookingStatus.Confirmed : BookingStatus.Pending, // ⬅️ אם אין Pending אצלך, תגיד לי
                        IsPaid = markAsPaid,
                        PaymentDate = markAsPaid ? DateTime.Now : null,
                        CardLastFourDigits = markAsPaid ? cardLastFourDigits : null,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    // ✅ חשוב: כרגע אתה מוריד חדרים מיד עם כרטיס אשראי.
                    // עם PayPal אנחנו עדיין לא רוצים "לשרוף" מלאי לפני התשלום.
                    // אז רק אם markAsPaid=true נעדכן מלאי.
                    if (markAsPaid)
                    {
                        trip.AvailableRooms -= item.NumberOfRooms;
                        trip.TimesBooked++;
                        trip.UpdatedAt = DateTime.Now;
                    }

                    _context.Bookings.Add(booking);
                    await _context.SaveChangesAsync(ct);
                    bookingIds.Add(booking.BookingId);
                }

                // בכרטיס אשראי אתה מוחק עגלה מיד — עם PayPal נרצה למחוק רק אחרי Capture.
                // לכן כאן לא נוגעים בעגלה. (נעשה את זה בביצוע הסופי)
                await transaction.CommitAsync(ct);

                return (bookingIds, totalAmount);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

    }
}