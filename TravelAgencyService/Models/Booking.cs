using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class Booking
    {
        [Key]
        public int BookingId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int TripId { get; set; }

        [Required]
        [Display(Name = "Booking Date")]
        public DateTime BookingDate { get; set; } = DateTime.Now;

        [Required]
        [Display(Name = "Number of Rooms")]
        [Range(1, 10, ErrorMessage = "You can book between 1 and 10 rooms")]
        public int NumberOfRooms { get; set; } = 1;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Price")]
        public decimal TotalPrice { get; set; }

        [Required]
        [Display(Name = "Booking Status")]
        public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

        [Display(Name = "Payment Completed")]
        public bool IsPaid { get; set; } = false;

        [Display(Name = "Payment Date")]
        public DateTime? PaymentDate { get; set; }

        // Last 4 digits of card for reference (never store full card number!)
        [StringLength(4)]
        [Display(Name = "Card Last 4 Digits")]
        public string? CardLastFourDigits { get; set; }

        [Display(Name = "Cancellation Date")]
        public DateTime? CancellationDate { get; set; }

        [StringLength(500)]
        [Display(Name = "Cancellation Reason")]
        public string? CancellationReason { get; set; }

        // For itinerary download tracking
        [Display(Name = "Itinerary Downloaded")]
        public bool ItineraryDownloaded { get; set; } = false;

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }

        // Computed properties
        [NotMapped]
        public bool CanBeCancelled
        {
            get
            {
                if (Trip == null || Status != BookingStatus.Confirmed)
                    return false;

                var daysUntilTrip = (Trip.StartDate - DateTime.Now).Days;
                return daysUntilTrip >= Trip.CancellationDaysLimit;
            }
        }

        [NotMapped]
        public bool IsUpcoming => Trip != null && Trip.StartDate > DateTime.Now && Status == BookingStatus.Confirmed;

        [NotMapped]
        public bool IsPast => Trip != null && Trip.EndDate < DateTime.Now;

        [NotMapped]
        public int? DaysUntilDeparture => Trip != null && IsUpcoming
            ? (int)(Trip.StartDate - DateTime.Now).TotalDays
            : null;
    }

    public enum BookingStatus
    {
        Pending,
        Confirmed,
        Cancelled,
        Completed
    }
}