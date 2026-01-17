using System.ComponentModel.DataAnnotations;
using TravelAgencyService.Models;

namespace TravelAgencyService.Models.ViewModels
{
    /// <summary>
    /// ViewModel for the Waiting List Status page.
    /// Shows user their position in queue and booking status.
    /// </summary>
    public class WaitingListStatusViewModel
    {
        public int TripId { get; set; }
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// Physical available rooms
        /// </summary>
        public int AvailableRooms { get; set; }

        public int TotalWaiting { get; set; }
        public int? MyPosition { get; set; }
        public string EtaText { get; set; } = string.Empty;
        public bool CanJoin { get; set; }
        public bool CanLeave { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Whether this user has been notified that a room is available
        /// </summary>
        public bool IsNotified { get; set; }

        /// <summary>
        /// When the booking window expires
        /// </summary>
        public DateTime? NotificationExpiresAt { get; set; }

        /// <summary>
        /// Formatted booking window text (e.g., "24 hours", "2 days")
        /// </summary>
        public string BookingWindowText { get; set; } = string.Empty;

        /// <summary>
        /// TRUE if it's this user's turn to book (notified AND not expired)
        /// </summary>
        public bool IsMyTurn => IsNotified &&
                               NotificationExpiresAt.HasValue &&
                               NotificationExpiresAt.Value > DateTime.Now;

        /// <summary>
        /// Time remaining to book (for display)
        /// </summary>
        public string? TimeRemaining
        {
            get
            {
                if (!IsMyTurn || !NotificationExpiresAt.HasValue) return null;
                var remaining = NotificationExpiresAt.Value - DateTime.Now;
                if (remaining.TotalDays >= 1)
                    return $"{(int)remaining.TotalDays}d {remaining.Hours}h remaining";
                if (remaining.TotalHours >= 1)
                    return $"{(int)remaining.TotalHours}h {remaining.Minutes}m remaining";
                return $"{remaining.Minutes}m remaining";
            }
        }
    }

    /// <summary>
    /// ViewModel for waiting list entries in MyBookings page.
    /// </summary>
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
            WaitingListStatus.Expired => "Expired",
            WaitingListStatus.Booked => "Booked",
            WaitingListStatus.Cancelled => "Cancelled",
            _ => Status.ToString()
        };

        /// <summary>
        /// TRUE if user can currently book (notified AND not expired)
        /// </summary>
        public bool CanBook => Status == WaitingListStatus.Notified &&
                               NotificationExpiresAt.HasValue &&
                               NotificationExpiresAt.Value > DateTime.Now;

        /// <summary>
        /// Time remaining to book (for display)
        /// </summary>
        public string? TimeRemaining
        {
            get
            {
                if (!CanBook || !NotificationExpiresAt.HasValue) return null;
                var remaining = NotificationExpiresAt.Value - DateTime.Now;
                if (remaining.TotalDays >= 1)
                    return $"{(int)remaining.TotalDays}d {remaining.Hours}h remaining";
                if (remaining.TotalHours >= 1)
                    return $"{(int)remaining.TotalHours}h {remaining.Minutes}m remaining";
                return $"{remaining.Minutes}m remaining";
            }
        }
    }
}