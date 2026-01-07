using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

public class CreateReviewViewModel
{
    public int? TripId { get; set; }
    public string? TripName { get; set; }

    [Required]
    [Range(1, 5, ErrorMessage = "Please select a rating")]
    public int Rating { get; set; }

    [StringLength(200)]
    [Display(Name = "Review Title")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Please write your review")]
    [StringLength(2000)]
    [Display(Name = "Your Review")]
    public string Comment { get; set; } = string.Empty;

    public ReviewType ReviewType { get; set; }
}