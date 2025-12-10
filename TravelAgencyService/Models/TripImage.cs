using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TravelAgencyService.Models
{
    public class TripImage
    {
        [Key]
        public int ImageId { get; set; }

        [Required]
        public int TripId { get; set; }

        [Required]
        [StringLength(500)]
        [Display(Name = "Image URL")]
        public string ImageUrl { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "Image Caption")]
        public string? Caption { get; set; }

        [Display(Name = "Display Order")]
        public int DisplayOrder { get; set; } = 0;

        // Navigation property
        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }
    }
}