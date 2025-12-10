using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace TravelAgencyService.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        public DateTime? DateOfBirth { get; set; }

        [StringLength(500)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [Display(Name = "Account Status")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Registration Date")]
        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        // Navigation properties
        public virtual ICollection<Booking>? Bookings { get; set; }
        public virtual ICollection<WaitingListEntry>? WaitingListEntries { get; set; }
        public virtual ICollection<Review>? Reviews { get; set; }
        public virtual ICollection<CartItem>? CartItems { get; set; }

        // Computed property
        public string FullName => $"{FirstName} {LastName}";

        // Check age for trip restrictions
        public int? Age => DateOfBirth.HasValue
            ? (int)((DateTime.Now - DateOfBirth.Value).TotalDays / 365.25)
            : null;
    }
}