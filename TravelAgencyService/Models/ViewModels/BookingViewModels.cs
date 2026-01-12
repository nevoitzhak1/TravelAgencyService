using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

namespace TravelAgencyService.Models.ViewModels
{
    public class CreateBookingViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal PricePerPerson { get; set; }
        public decimal? OriginalPrice { get; set; }
        public bool IsOnSale { get; set; }
        public int AvailableRooms { get; set; }
        public string? MainImageUrl { get; set; }
        public PackageType PackageType { get; set; }
        public int? MinimumAge { get; set; }
        public int? MaximumAge { get; set; }

        [Required(ErrorMessage = "Please select number of rooms")]
        [Range(1, 10, ErrorMessage = "You can book between 1 and 10 rooms")]
        [Display(Name = "Number of Rooms")]
        public int NumberOfRooms { get; set; } = 1;

        public decimal TotalPrice => PricePerPerson * NumberOfRooms;
        public int TripDurationDays => (EndDate - StartDate).Days;
    }

    public class BookingConfirmationViewModel
    {
        public int BookingId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public DateTime BookingDate { get; set; }
        public bool IsPaid { get; set; }
        public string? MainImageUrl { get; set; }
    }

    public class MyBookingsViewModel
    {
        public List<UserBookingViewModel> UpcomingBookings { get; set; } = new();
        public List<UserBookingViewModel> PastBookings { get; set; } = new();
        public List<WaitingListItemViewModel> WaitingListEntries { get; set; } = new();
        public int TotalBookings { get; set; }
        public bool ShowPastBookings { get; set; }
        public bool ShowCancelledBookings { get; set; }
        public bool ShowWaitingList { get; set; }
    }

    public class UserBookingViewModel
    {
        public int BookingId { get; set; }
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public DateTime BookingDate { get; set; }
        public BookingStatus Status { get; set; }
        public bool IsPaid { get; set; }
        public string? MainImageUrl { get; set; }
        public PackageType PackageType { get; set; }
        public int CancellationDaysLimit { get; set; }
        public bool CanBeCancelled { get; set; }
        public int? DaysUntilDeparture { get; set; }
        public bool HasReviewed { get; set; }
    }

    public class CancelBookingViewModel
    {
        public int BookingId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public bool IsPaid { get; set; }

        [StringLength(500)]
        [Display(Name = "Reason for Cancellation (Optional)")]
        public string? CancellationReason { get; set; }
    }

    public class JoinWaitingListViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Price { get; set; }
        public string? MainImageUrl { get; set; }
        public int CurrentWaitingCount { get; set; }

        [Required]
        [Range(1, 10, ErrorMessage = "You can request between 1 and 10 rooms")]
        [Display(Name = "Number of Rooms Needed")]
        public int RoomsRequested { get; set; } = 1;
    }
}