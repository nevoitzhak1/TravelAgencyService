using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class TripReminderSendLog
    {
        [Key]
        public int TripReminderSendLogId { get; set; }

        [Required]
        public int TripReminderRuleId { get; set; }

        [Required]
        public int BookingId { get; set; }

        [StringLength(256)]
        public string? ToEmail { get; set; }

        public DateTime SentAt { get; set; } = DateTime.Now;

        [ForeignKey(nameof(TripReminderRuleId))]
        public virtual TripReminderRule? Rule { get; set; }

        [ForeignKey(nameof(BookingId))]
        public virtual Booking? Booking { get; set; }
    }
}
