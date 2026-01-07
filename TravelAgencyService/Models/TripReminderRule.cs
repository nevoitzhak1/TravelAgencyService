using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class TripReminderRule
    {
        [Key]
        public int TripReminderRuleId { get; set; }

        [Required]
        public int TripId { get; set; }

        [ForeignKey(nameof(TripId))]
        public virtual Trip? Trip { get; set; }

        [Range(1, 1200)]
        public int OffsetAmount { get; set; } // כמה ימים/חודשים לפני

        [Required]
        public ReminderOffsetUnit OffsetUnit { get; set; } // Days / Months

        public bool IsActive { get; set; } = true;

        // אופציונלי - אם תרצה בהמשך הודעה מותאמת
        [StringLength(200)]
        public string? SubjectTemplate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public enum ReminderOffsetUnit
    {
        Days = 0,
        Months = 1
    }
}
