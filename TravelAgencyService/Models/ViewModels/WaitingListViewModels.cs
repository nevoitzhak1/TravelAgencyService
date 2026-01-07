using System.ComponentModel.DataAnnotations;

namespace TravelAgencyService.Models.ViewModels
{
    // For User: Status page
    public class WaitingListStatusViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public int AvailableRooms { get; set; }
        public int TotalWaiting { get; set; }
        public int? MyPosition { get; set; }
        public string EtaText { get; set; } = string.Empty;
        public bool CanJoin { get; set; }
        public bool CanLeave { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}