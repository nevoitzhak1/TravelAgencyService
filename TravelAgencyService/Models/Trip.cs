using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class Trip
    {
        [Key]
        public int TripId { get; set; }

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
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "End date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime EndDate { get; set; }

        [Required(ErrorMessage = "Price is required")]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 1000000, ErrorMessage = "Price must be positive")]
        [Display(Name = "Price ($)")]
        public decimal Price { get; set; }

        // For discounts - stores original price when discount is active
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Original Price")]
        public decimal? OriginalPrice { get; set; }

        // Discount end date (max 1 week from start)
        [DataType(DataType.DateTime)]
        [Display(Name = "Discount Ends")]
        public DateTime? DiscountEndDate { get; set; }

        [Required(ErrorMessage = "Number of rooms is required")]
        [Range(1, 1000, ErrorMessage = "Must have at least 1 room")]
        [Display(Name = "Total Rooms")]
        public int TotalRooms { get; set; }

        [Display(Name = "Available Rooms")]
        public int AvailableRooms { get; set; }

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

        // Main image for the trip
        [StringLength(500)]
        [Display(Name = "Main Image")]
        public string? MainImageUrl { get; set; }

        // Trip visibility (admin can hide trips)
        [Display(Name = "Is Visible")]
        public bool IsVisible { get; set; } = true;

        // Booking rules
        [DataType(DataType.Date)]
        [Display(Name = "Last Booking Date")]
        public DateTime? LastBookingDate { get; set; }

        [Display(Name = "Days Before Trip for Cancellation")]
        public int CancellationDaysLimit { get; set; } = 7;

        // For popularity sorting
        [Display(Name = "Times Booked")]
        public int TimesBooked { get; set; } = 0;

        // Timestamps
        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Updated At")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation properties (relationships)
        public virtual ICollection<TripImage>? Images { get; set; }
        public virtual ICollection<Booking>? Bookings { get; set; }
        public virtual ICollection<WaitingListEntry>? WaitingList { get; set; }
        public virtual ICollection<Review>? Reviews { get; set; }

        // Computed properties (not stored in database)
        [NotMapped]
        public bool IsOnSale => OriginalPrice.HasValue &&
                                DiscountEndDate.HasValue &&
                                DiscountEndDate > DateTime.Now;

        [NotMapped]
        public bool IsFullyBooked => AvailableRooms <= 0;

        [NotMapped]
        public int TripDurationDays => (EndDate - StartDate).Days;

        [NotMapped]
        public decimal? DiscountPercentage => IsOnSale && OriginalPrice > 0
            ? Math.Round((1 - (Price / OriginalPrice.Value)) * 100, 0)
            : null;
    }

    // Enum for package types
    public enum PackageType
    {
        Family,
        Honeymoon,
        Adventure,
        Cruise,
        Luxury,
        Budget,
        Solo,
        Group,
        Business
    }
}