using System.ComponentModel.DataAnnotations;

namespace TravelAgencyService.Models.ViewModels
{
    public class TripViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }

        /// <summary>
        /// Actual rooms in database
        /// </summary>
        public int AvailableRooms { get; set; }

        /// <summary>
        /// Rooms available for public booking (0 if there are people in waiting list)
        /// </summary>
        public int AvailableRoomsForPublic { get; set; }

        public PackageType PackageType { get; set; }
        public int? MinimumAge { get; set; }
        public int? MaximumAge { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? MainImageUrl { get; set; }
        public bool IsOnSale { get; set; }

        /// <summary>
        /// True if no rooms available for public (either physically full OR rooms reserved for waiting list)
        /// </summary>
        public bool IsFullyBooked { get; set; }

        public int TripDurationDays { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int TimesBooked { get; set; }

        /// <summary>
        /// True if there are people waiting in the waiting list
        /// </summary>
        public bool HasPeopleInWaitingList { get; set; }

        /// <summary>
        /// Number of people in waiting list (Waiting or Notified status)
        /// </summary>
        public int WaitingListCount { get; set; }
    }

    public class TripListViewModel
    {
        public List<TripViewModel> Trips { get; set; } = new List<TripViewModel>();
        public int TotalTrips { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 9;
        public int TotalPages => (int)Math.Ceiling((double)TotalTrips / PageSize);

        // Filter options
        public string? SearchQuery { get; set; }
        public string? SelectedCountry { get; set; }
        public string? SelectedDestination { get; set; }
        public PackageType? SelectedPackageType { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public DateTime? StartDateFrom { get; set; }
        public DateTime? StartDateTo { get; set; }
        public bool OnlyDiscounted { get; set; }
        public string SortBy { get; set; } = "date";

        // For filter dropdowns
        public List<string> Countries { get; set; } = new List<string>();
        public List<string> Destinations { get; set; } = new List<string>();
    }

    public class TripDetailsViewModel
    {
        public TripViewModel Trip { get; set; } = null!;
        public List<TripImageViewModel> Images { get; set; } = new List<TripImageViewModel>();
        public List<ReviewViewModel> Reviews { get; set; } = new List<ReviewViewModel>();
        public bool CanBook { get; set; }
        public bool IsInCart { get; set; }
        public bool IsInWaitingList { get; set; }
        public int WaitingListPosition { get; set; }
        public int WaitingListCount { get; set; }
        public bool HasWaitingListPriority { get; set; }
        public bool IsMyTurnToBook { get; set; }
    }

    public class TripImageViewModel
    {
        public int ImageId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? Caption { get; set; }
    }

    public class ReviewViewModel
    {
        public int ReviewId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string? Title { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string TripName { get; set; } = string.Empty;
    }

    public class ReviewListViewModel
    {
        public List<ReviewViewModel> Reviews { get; set; } = new List<ReviewViewModel>();
        public int TotalReviews { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }

        // Filters
        public int? SelectedRating { get; set; }
        public int? SelectedTripId { get; set; }
        public string? SearchQuery { get; set; }

        // For dropdowns
        public List<TripSelectionViewModel> AvailableTrips { get; set; } = new List<TripSelectionViewModel>();

        // Statistics
        public double AverageRating { get; set; }
        public int FiveStarCount { get; set; }
        public int FourStarCount { get; set; }
        public int ThreeStarCount { get; set; }
        public int TwoStarCount { get; set; }
        public int OneStarCount { get; set; }
    }

    public class TripSelectionViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
    }
}