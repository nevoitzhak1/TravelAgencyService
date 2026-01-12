using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

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
        public bool IsNotified { get; set; }
        public DateTime? NotificationExpiresAt { get; set; }
    }

    // For MyBookings: Waiting list entries
    public class WaitingListItemViewModel
    {
        public int WaitingListEntryId { get; set; }
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? MainImageUrl { get; set; }
        public int Position { get; set; }
        public int RoomsRequested { get; set; }
        public DateTime JoinedDate { get; set; }
        public WaitingListStatus Status { get; set; }
        public bool IsNotified { get; set; }
        public DateTime? NotificationExpiresAt { get; set; }

        public string StatusText => Status switch
        {
            WaitingListStatus.Waiting => $"Position #{Position}",
            WaitingListStatus.Notified => "Your turn! Book now",
            _ => Status.ToString()
        };

        public bool CanBook => Status == WaitingListStatus.Notified &&
                               NotificationExpiresAt.HasValue &&
                               NotificationExpiresAt.Value > DateTime.Now;

        public string? TimeRemaining
        {
            get
            {
                if (!CanBook || !NotificationExpiresAt.HasValue) return null;
                var remaining = NotificationExpiresAt.Value - DateTime.Now;
                if (remaining.TotalHours >= 1)
                    return $"{(int)remaining.TotalHours}h {remaining.Minutes}m remaining";
                return $"{remaining.Minutes}m remaining";
            }
        }
    }
}