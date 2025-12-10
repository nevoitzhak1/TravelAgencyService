using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace TravelAgencyService.Models
{
    public class CartItem
    {
        [Key]
        public int CartItemId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int TripId { get; set; }

        [Required]
        [Display(Name = "Number of Rooms")]
        [Range(1, 10, ErrorMessage = "You can add between 1 and 10 rooms")]
        public int NumberOfRooms { get; set; } = 1;

        [Display(Name = "Added Date")]
        public DateTime AddedDate { get; set; } = DateTime.Now;

        // Store price at time of adding to cart (in case price changes)
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Price When Added")]
        public decimal PriceWhenAdded { get; set; }

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [ForeignKey("TripId")]
        public virtual Trip? Trip { get; set; }

        // Computed properties
        [NotMapped]
        public decimal TotalPrice => PriceWhenAdded * NumberOfRooms;

        [NotMapped]
        public bool IsTripStillAvailable => Trip != null &&
                                             Trip.IsVisible &&
                                             Trip.AvailableRooms >= NumberOfRooms &&
                                             Trip.StartDate > DateTime.Now;
    }
}