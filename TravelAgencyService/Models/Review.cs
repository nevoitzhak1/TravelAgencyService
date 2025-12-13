using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TravelAgencyService.Models.ViewModels;

namespace TravelAgencyService.Models
{
    public class Review
    {
        [Key]
        public int ReviewId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        // TripId is nullable - if null, it's a website review
        public int? TripId { get; set; }

        [Required]
        [Display(Name = "Rating")]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Please write a review")]
        [StringLength(2000)]
        [Display(Name = "Review")]
        [DataType(DataType.MultilineText)]
        public string Comment { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "Review Title")]
        public string? Title { get; set; }

        [Display(Name = "Review Type")]
        public ReviewType ReviewType { get; set; }

        [Display(Name = "Is Approved")]
        public bool IsApproved { get; set; } = true;

        [Display(Name = "Created At")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Updated At")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }
    }

    public enum ReviewType
    {
        TripReview,      // Review for a specific trip
        WebsiteReview    // Review for the booking experience / website
    }
    


    
}

