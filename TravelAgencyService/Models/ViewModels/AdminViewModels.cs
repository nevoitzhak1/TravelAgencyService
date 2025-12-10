using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

namespace TravelAgencyService.Models.ViewModels
{
    // Dashboard ViewModel
    public class AdminDashboardViewModel
    {
        public int TotalTrips { get; set; }
        public int TotalUsers { get; set; }
        public int TotalBookings { get; set; }
        public int PendingBookings { get; set; }
        public int TotalWaitingList { get; set; }
        public decimal TotalRevenue { get; set; }
        public List<RecentBookingViewModel> RecentBookings { get; set; } = new();
        public List<TripViewModel> PopularTrips { get; set; } = new();
    }

    public class RecentBookingViewModel
    {
        public int BookingId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string TripName { get; set; } = string.Empty;
        public DateTime BookingDate { get; set; }
        public decimal TotalPrice { get; set; }
        public BookingStatus Status { get; set; }
    }

    // Trip Management ViewModels
    public class TripCreateViewModel
    {
        [Required(ErrorMessage = "Package name is required")]
        [StringLength(200)]
        [Display(Name = "Package Name")]
        public string PackageName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Destination is required")]
        [StringLength(100)]
        public string Destination { get; set; } = string.Empty;

        [Required(ErrorMessage = "Country is required")]
        [StringLength(100)]
        public string Country { get; set; } = string.Empty;

        [Required(ErrorMessage = "Start date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime StartDate { get; set; } = DateTime.Now.AddDays(30);

        [Required(ErrorMessage = "End date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; } = DateTime.Now.AddDays(37);

        [Required(ErrorMessage = "Price is required")]
        [Range(1, 1000000, ErrorMessage = "Price must be positive")]
        [Display(Name = "Price ($)")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Number of rooms is required")]
        [Range(1, 1000, ErrorMessage = "Must have at least 1 room")]
        [Display(Name = "Total Rooms")]
        public int TotalRooms { get; set; } = 10;

        [Required(ErrorMessage = "Package type is required")]
        [Display(Name = "Package Type")]
        public PackageType PackageType { get; set; }

        [Display(Name = "Minimum Age")]
        [Range(0, 120)]
        public int? MinimumAge { get; set; }

        [Display(Name = "Maximum Age")]
        [Range(0, 120)]
        public int? MaximumAge { get; set; }

        [Required(ErrorMessage = "Description is required")]
        [StringLength(5000)]
        [DataType(DataType.MultilineText)]
        public string Description { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Main Image URL")]
        [Url(ErrorMessage = "Please enter a valid URL")]
        public string? MainImageUrl { get; set; }

        [Display(Name = "Is Visible")]
        public bool IsVisible { get; set; } = true;

        [DataType(DataType.Date)]
        [Display(Name = "Last Booking Date")]
        public DateTime? LastBookingDate { get; set; }

        [Display(Name = "Cancellation Days Limit")]
        [Range(0, 365)]
        public int CancellationDaysLimit { get; set; } = 7;
    }

    public class TripEditViewModel : TripCreateViewModel
    {
        public int TripId { get; set; }

        [Display(Name = "Available Rooms")]
        public int AvailableRooms { get; set; }

        [Display(Name = "Times Booked")]
        public int TimesBooked { get; set; }

        // Discount fields
        [Display(Name = "Original Price (for discount)")]
        [Range(0, 1000000)]
        public decimal? OriginalPrice { get; set; }

        [DataType(DataType.DateTime)]
        [Display(Name = "Discount End Date")]
        public DateTime? DiscountEndDate { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class TripDiscountViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;

        [Display(Name = "Current Price")]
        public decimal CurrentPrice { get; set; }

        [Required(ErrorMessage = "Discounted price is required")]
        [Display(Name = "New Discounted Price ($)")]
        [Range(0.01, 1000000, ErrorMessage = "Price must be positive")]
        public decimal DiscountedPrice { get; set; }

        [Required(ErrorMessage = "Discount end date is required")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Discount Ends")]
        public DateTime DiscountEndDate { get; set; } = DateTime.Now.AddDays(7);
    }

    // User Management ViewModels
    public class UserListViewModel
    {
        public List<UserViewModel> Users { get; set; } = new();
        public int TotalUsers { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SearchQuery { get; set; }
        public string? RoleFilter { get; set; }
    }

    public class UserViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public DateTime RegistrationDate { get; set; }
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
        public int TotalBookings { get; set; }
    }

    public class UserDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string? Address { get; set; }
        public DateTime RegistrationDate { get; set; }
        public bool IsActive { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<BookingViewModel> Bookings { get; set; } = new();
    }

    public class BookingViewModel
    {
        public int BookingId { get; set; }
        public string TripName { get; set; } = string.Empty;
        public DateTime BookingDate { get; set; }
        public DateTime TripStartDate { get; set; }
        public DateTime TripEndDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public BookingStatus Status { get; set; }
        public bool IsPaid { get; set; }
    }

    // Booking Management
    public class BookingListViewModel
    {
        public List<AdminBookingViewModel> Bookings { get; set; } = new();
        public int TotalBookings { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public BookingStatus? StatusFilter { get; set; }
        public string? SearchQuery { get; set; }
    }

    public class AdminBookingViewModel
    {
        public int BookingId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int TripId { get; set; }
        public string TripName { get; set; } = string.Empty;
        public DateTime BookingDate { get; set; }
        public DateTime TripStartDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public BookingStatus Status { get; set; }
        public bool IsPaid { get; set; }
        public DateTime? PaymentDate { get; set; }
    }

    // Waiting List Management
    public class WaitingListViewModel
    {
        public List<WaitingListItemViewModel> Entries { get; set; } = new();
        public int TotalEntries { get; set; }
    }

    public class WaitingListItemViewModel
    {
        public int WaitingListEntryId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string TripName { get; set; } = string.Empty;
        public int TripId { get; set; }
        public int Position { get; set; }
        public DateTime JoinedDate { get; set; }
        public int RoomsRequested { get; set; }
        public WaitingListStatus Status { get; set; }
        public bool IsNotified { get; set; }
    }
}