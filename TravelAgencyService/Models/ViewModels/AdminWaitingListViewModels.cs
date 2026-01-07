using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

namespace TravelAgencyService.Models.ViewModels
{
    // For Admin: List all entries
    public class AdminWaitingListViewModel
    {
        public List<AdminWaitingListItemViewModel> Entries { get; set; } = new();
        public int TotalEntries { get; set; }
    }

    public class AdminWaitingListItemViewModel
    {
        public int WaitingListEntryId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string TripName { get; set; } = string.Empty;
        public int TripId { get; set; }
        public int Position { get; set; }
        public DateTime JoinedDate { get; set; }
        public int RoomsRequested { get; set; }
        public WaitingListStatus Status { get; set; }
        public string StatusText => Status.ToString();
        public bool IsNotified { get; set; }
        public DateTime? NotificationDate { get; set; }
        public DateTime? NotificationExpiresAt { get; set; }
        public bool CanNotify => Status == WaitingListStatus.Waiting && !IsNotified;
    }
}