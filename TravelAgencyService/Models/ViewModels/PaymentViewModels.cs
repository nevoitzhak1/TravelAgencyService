using System.ComponentModel.DataAnnotations;

namespace TravelAgencyService.Models.ViewModels
{
    public class CheckoutViewModel
    {
        public int BookingId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice { get; set; }
        public string? MainImageUrl { get; set; }
        

        [Required(ErrorMessage = "Cardholder name is required")]
        [StringLength(100)]
        [Display(Name = "Cardholder Name")]
        public string CardholderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Card number is required")]
        [StringLength(19, MinimumLength = 13)]
        [Display(Name = "Card Number")]
        public string CardNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expiry month is required")]
        [Range(1, 12, ErrorMessage = "Invalid month")]
        [Display(Name = "Expiry Month")]
        public int ExpiryMonth { get; set; }

        [Required(ErrorMessage = "Expiry year is required")]
        [Range(2024, 2040, ErrorMessage = "Invalid year")]
        [Display(Name = "Expiry Year")]
        public int ExpiryYear { get; set; }

        [Required(ErrorMessage = "CVV is required")]
        [StringLength(4, MinimumLength = 3, ErrorMessage = "CVV must be 3 or 4 digits")]
        [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits")]
        [Display(Name = "CVV")]
        public string CVV { get; set; } = string.Empty;
    }

    public class PaymentResultViewModel
    {
        public bool Success { get; set; }
        public int BookingId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public string? TransactionId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime PaymentDate { get; set; }
    }

    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
        public int TotalItems => Items.Count;
    }

    public class CartItemViewModel
    {
        public int CartItemId { get; set; }
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal PricePerPerson { get; set; }
        public decimal? OriginalPrice { get; set; }
        public bool IsOnSale { get; set; }
        public int NumberOfRooms { get; set; }
        public decimal TotalPrice => PricePerPerson * NumberOfRooms;
        public string? MainImageUrl { get; set; }
        public int AvailableRooms { get; set; }
        public bool IsStillAvailable { get; set; }
        public DateTime AddedDate { get; set; }
    }

    public class CartCheckoutViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal TotalPrice => Items.Sum(i => i.TotalPrice);
        public bool IsSingleCheckout { get; set; }
        public int? SingleTripId { get; set; }
        public int? SingleRooms { get; set; }

        [Required(ErrorMessage = "Cardholder name is required")]
        [StringLength(100)]
        [Display(Name = "Cardholder Name")]
        public string CardholderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Card number is required")]
        [StringLength(19, MinimumLength = 13)]
        [Display(Name = "Card Number")]
        public string CardNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expiry month is required")]
        [Range(1, 12, ErrorMessage = "Invalid month")]
        [Display(Name = "Expiry Month")]
        public int ExpiryMonth { get; set; }

        [Required(ErrorMessage = "Expiry year is required")]
        [Range(2024, 2040, ErrorMessage = "Invalid year")]
        [Display(Name = "Expiry Year")]
        public int ExpiryYear { get; set; }

        [Required(ErrorMessage = "CVV is required")]
        [StringLength(4, MinimumLength = 3, ErrorMessage = "CVV must be 3 or 4 digits")]
        [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits")]
        [Display(Name = "CVV")]
        public string CVV { get; set; } = string.Empty;
    }

    public class BuyNowViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal PricePerRoom { get; set; }
        public decimal? OriginalPrice { get; set; }
        public bool IsOnSale { get; set; }
        public int AvailableRooms { get; set; }
        public string? MainImageUrl { get; set; }

        [Required]
        [Range(1, 10, ErrorMessage = "You can book between 1 and 10 rooms")]
        [Display(Name = "Number of Rooms")]
        public int NumberOfRooms { get; set; } = 1;

        [Required(ErrorMessage = "Cardholder name is required")]
        [StringLength(100)]
        [Display(Name = "Cardholder Name")]
        public string CardholderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Card number is required")]
        [StringLength(19, MinimumLength = 13)]
        [Display(Name = "Card Number")]
        public string CardNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expiry month is required")]
        [Range(1, 12, ErrorMessage = "Invalid month")]
        [Display(Name = "Expiry Month")]
        public int ExpiryMonth { get; set; }

        [Required(ErrorMessage = "Expiry year is required")]
        [Range(2024, 2040, ErrorMessage = "Invalid year")]
        [Display(Name = "Expiry Year")]
        public int ExpiryYear { get; set; }

        [Required(ErrorMessage = "CVV is required")]
        [StringLength(4, MinimumLength = 3, ErrorMessage = "CVV must be 3 or 4 digits")]
        [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits")]
        [Display(Name = "CVV")]
        public string CVV { get; set; } = string.Empty;

        public decimal TotalPrice => PricePerRoom * NumberOfRooms;
        public int TripDurationDays => (EndDate - StartDate).Days;
    }
}