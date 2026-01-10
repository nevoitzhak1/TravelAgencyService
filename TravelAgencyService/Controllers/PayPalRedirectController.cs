using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using TravelAgencyService.Data;
using TravelAgencyService.Models;
using TravelAgencyService.Services.PayPal;

namespace TravelAgencyService.Controllers;

[Authorize]
public class PayPalRedirectController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly PayPalClient _pp;

    public PayPalRedirectController(ApplicationDbContext context, PayPalClient pp)
    {
        _context = context;
        _pp = pp;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartFromCart(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account");

        var cartItems = await _context.CartItems
            .Include(c => c.Trip)
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        if (!cartItems.Any())
        {
            TempData["Error"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }

        decimal total = 0m;
        foreach (var item in cartItems)
        {
            var trip = await _context.Trips.FindAsync(new object[] { item.TripId }, ct);
            if (trip == null || trip.AvailableRooms < item.NumberOfRooms || !trip.IsVisible || trip.StartDate <= DateTime.Now)
            {
                TempData["Error"] = $"'{item.Trip?.PackageName ?? "Trip"}' is no longer available.";
                return RedirectToAction("Index", "Cart");
            }

            total += trip.Price * item.NumberOfRooms;
        }

        using var tx = await _context.Database.BeginTransactionAsync(ct);
        var bookingIds = new List<int>();

        try
        {
            foreach (var item in cartItems)
            {
                var trip = await _context.Trips.FindAsync(new object[] { item.TripId }, ct);
                if (trip == null || trip.AvailableRooms < item.NumberOfRooms)
                    throw new Exception("Trip availability changed.");

                var booking = new Booking
                {
                    UserId = userId!,
                    TripId = item.TripId,
                    NumberOfRooms = item.NumberOfRooms,
                    TotalPrice = trip.Price * item.NumberOfRooms,
                    BookingDate = DateTime.Now,
                    Status = BookingStatus.Pending,
                    IsPaid = false,
                    PaymentDate = null,
                    CardLastFourDigits = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync(ct);
                bookingIds.Add(booking.BookingId);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "Could not start PayPal payment.";
            return RedirectToAction("Checkout", "Cart");
        }

        var bookingIdsCsv = string.Join(",", bookingIds);

        var returnUrl = Url.Action("Return", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "cart" }, Request.Scheme)!;
        var cancelUrl = Url.Action("Cancel", "PayPalRedirect", new { bookingIds = bookingIdsCsv, source = "cart" }, Request.Scheme)!;

        var currency = "USD";
        var (_, approveUrl) = await _pp.CreateOrderAndGetApproveUrlAsync(total, currency, returnUrl, cancelUrl, ct);

        return Redirect(approveUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSingle(int tripId, int rooms, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account");

        var trip = await _context.Trips.FindAsync(new object[] { tripId }, ct);
        if (trip == null || !trip.IsVisible || trip.StartDate <= DateTime.Now || rooms < 1 || trip.AvailableRooms < rooms)
        {
            TempData["Error"] = "Trip is not available.";
            return RedirectToAction("Details", "Trip", new { id = tripId });
        }

        using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var booking = new Booking
            {
                UserId = userId!,
                TripId = tripId,
                NumberOfRooms = rooms,
                TotalPrice = trip.Price * rooms,
                BookingDate = DateTime.Now,
                Status = BookingStatus.Pending,
                IsPaid = false,
                PaymentDate = null,
                CardLastFourDigits = null,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var bookingIdsCsv = booking.BookingId.ToString();

            var returnUrl = Url.Action("Return", "PayPalRedirect",
                new { bookingIds = bookingIdsCsv, source = "single" }, Request.Scheme)!;

            var cancelUrl = Url.Action("Cancel", "PayPalRedirect",
                new { bookingIds = bookingIdsCsv, source = "single" }, Request.Scheme)!;

            var currency = "USD";
            var (_, approveUrl) = await _pp.CreateOrderAndGetApproveUrlAsync(booking.TotalPrice, currency, returnUrl, cancelUrl, ct);

            return Redirect(approveUrl);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "Could not start PayPal payment.";
            return RedirectToAction("Checkout", "Cart", new { tripId, rooms });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Return(string bookingIds, string? token, CancellationToken ct, string source = "cart")
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(bookingIds))
            return BadRequest("Missing parameters.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction("Login", "Account", new { returnUrl = $"/PayPalRedirect/Return?bookingIds={bookingIds}&token={token}" });

        var capture = await _pp.CaptureOrderAsync(token, ct);
        var status = capture.GetProperty("status").GetString();

        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "PayPal payment was not completed.";
            return RedirectToAction("Checkout", "Cart");
        }

        var ids = bookingIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse)
                            .ToList();

        using var tx = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            var bookings = await _context.Bookings
                .Where(b => ids.Contains(b.BookingId) && b.UserId == userId)
                .ToListAsync(ct);

            if (bookings.Count != ids.Count)
                throw new Exception("Some bookings not found.");

            foreach (var b in bookings)
            {
                if (b.IsPaid) continue;

                var trip = await _context.Trips.FindAsync(new object[] { b.TripId }, ct);
                if (trip == null || trip.AvailableRooms < b.NumberOfRooms)
                    throw new Exception("Trip availability changed.");

                trip.AvailableRooms -= b.NumberOfRooms;
                trip.TimesBooked++;
                trip.UpdatedAt = DateTime.Now;

                b.IsPaid = true;
                b.PaymentDate = DateTime.Now;
                b.Status = BookingStatus.Confirmed;
                b.UpdatedAt = DateTime.Now;
            }

            if (source == "cart")
            {
                var cartItems = await _context.CartItems.Where(c => c.UserId == userId).ToListAsync(ct);
                _context.CartItems.RemoveRange(cartItems);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            TempData["Success"] = $"PayPal payment successful! {bookings.Count} booking(s) confirmed.";

            if (source == "single" && bookings.Count == 1)
            {
                return RedirectToAction("Confirmation", "Booking", new { id = bookings[0].BookingId });
            }

            return RedirectToAction("Index", "Home");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            TempData["Error"] = "An error occurred while completing PayPal payment.";
            return RedirectToAction("Checkout", "Cart");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Cancel(string bookingIds, CancellationToken ct, string source = "cart")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(bookingIds))
        {
            var ids = bookingIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var pending = await _context.Bookings
                .Where(b => ids.Contains(b.BookingId) && b.UserId == userId && !b.IsPaid && b.Status == BookingStatus.Pending)
                .ToListAsync(ct);

            _context.Bookings.RemoveRange(pending);
            await _context.SaveChangesAsync(ct);
        }

        TempData["Error"] = "PayPal payment was canceled.";
        return RedirectToAction("Checkout", "Cart");
    }
}
