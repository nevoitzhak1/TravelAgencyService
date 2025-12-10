using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class WaitingListEntry
    {
        [Key]
        public int WaitingListEntryId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int TripId { get; set; }

        [Required]
        [Display(Name = "Position in Queue")]
        public int Position { get; set; }

        [Required]
        [Display(Name = "Joined Date")]
        public DateTime JoinedDate { get; set; } = DateTime.Now;

        [Display(Name = "Number of Rooms Requested")]
        [Range(1, 10)]
        public int RoomsRequested { get; set; } = 1;

        [Display(Name = "Notified")]
        public bool IsNotified { get; set; } = false;

        [Display(Name = "Notification Date")]
        public DateTime? NotificationDate { get; set; }

        // When notified, user has limited time to book
        [Display(Name = "Notification Expires")]
        public DateTime? NotificationExpiresAt { get; set; }

        [Display(Name = "Status")]
        public WaitingListStatus Status { get; set; } = WaitingListStatus.Waiting;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }

        // Computed properties
        [NotMapped]
        public bool CanBook => Status == WaitingListStatus.Notified &&
                               NotificationExpiresAt.HasValue &&
                               NotificationExpiresAt > DateTime.Now;
    }

    public enum WaitingListStatus
    {
        Waiting,
        Notified,
        Booked,
        Expired,
        Cancelled
    }
}