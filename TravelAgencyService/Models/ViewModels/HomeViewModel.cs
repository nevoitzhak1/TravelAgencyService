namespace TravelAgencyService.Models.ViewModels
{
    public class HomeViewModel
    {
        public int TotalTrips { get; set; }
        public int TotalCountries { get; set; }
        public int TotalBookings { get; set; }
        public List<TripViewModel> FeaturedTrips { get; set; } = new List<TripViewModel>();
        public List<TripViewModel> AllTrips { get; set; } = new List<TripViewModel>();  // ← הוסיפי!
        public List<ReviewViewModel> RecentReviews { get; set; } = new List<ReviewViewModel>();
    }

}