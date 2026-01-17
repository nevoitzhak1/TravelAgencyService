using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelAgencyService.Models;

namespace TravelAgencyService.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets - these become tables in your database
        public DbSet<TripReminderRule> TripReminderRules { get; set; }
        public DbSet<TripReminderSendLog> TripReminderSendLogs { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<TripImage> TripImages { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<WaitingListEntry> WaitingListEntries { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<CartItem> CartItems { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Trip configuration
            builder.Entity<Trip>(entity =>
            {
                entity.HasKey(t => t.TripId);
                entity.HasIndex(t => t.Destination);
                entity.HasIndex(t => t.Country);
                entity.HasIndex(t => t.PackageType);
                entity.HasIndex(t => t.StartDate);
                entity.HasIndex(t => t.RecurringGroupKey);

                // Unique constraint: same RecurringGroupKey cannot have same year
                // This prevents duplicate trips in the same year for a recurring series
                entity.HasIndex(t => new { t.RecurringGroupKey, t.StartDate })
                      .IsUnique()
                      .HasFilter("[RecurringGroupKey] IS NOT NULL");

                // One trip has many images
                entity.HasMany(t => t.Images)
                      .WithOne(i => i.Trip)
                      .HasForeignKey(i => i.TripId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One trip has many bookings
                entity.HasMany(t => t.Bookings)
                      .WithOne(b => b.Trip)
                      .HasForeignKey(b => b.TripId)
                      .OnDelete(DeleteBehavior.Restrict);

                // One trip has many waiting list entries
                entity.HasMany(t => t.WaitingList)
                      .WithOne(w => w.Trip)
                      .HasForeignKey(w => w.TripId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One trip has many reviews
                entity.HasMany(t => t.Reviews)
                      .WithOne(r => r.Trip)
                      .HasForeignKey(r => r.TripId)
                      .OnDelete(DeleteBehavior.Cascade);
               
                // One trip has many reminder rules
                entity.HasMany(t => t.ReminderRules)
                      .WithOne(r => r.Trip)
                      .HasForeignKey(r => r.TripId)
                      .OnDelete(DeleteBehavior.Cascade);


            });

            // TripImage configuration
            builder.Entity<TripImage>(entity =>
            {
                entity.HasKey(i => i.ImageId);
            });

            // ApplicationUser configuration
            builder.Entity<ApplicationUser>(entity =>
            {
                // One user has many bookings
                entity.HasMany(u => u.Bookings)
                      .WithOne(b => b.User)
                      .HasForeignKey(b => b.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                // One user has many waiting list entries
                entity.HasMany(u => u.WaitingListEntries)
                      .WithOne(w => w.User)
                      .HasForeignKey(w => w.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One user has many reviews
                entity.HasMany(u => u.Reviews)
                      .WithOne(r => r.User)
                      .HasForeignKey(r => r.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                // One user has many cart items
                entity.HasMany(u => u.CartItems)
                      .WithOne(c => c.User)
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Booking configuration
            builder.Entity<Booking>(entity =>
            {
                entity.HasKey(b => b.BookingId);
                entity.HasIndex(b => b.UserId);
                entity.HasIndex(b => b.TripId);
                entity.HasIndex(b => b.Status);
            });

            // WaitingListEntry configuration
            builder.Entity<WaitingListEntry>(entity =>
            {
                entity.HasKey(w => w.WaitingListEntryId);
                entity.HasIndex(w => new { w.TripId, w.Position });

                // Ensure user can only be on waiting list once per trip
                entity.HasIndex(w => new { w.TripId, w.UserId }).IsUnique();
            });

            // Review configuration
            builder.Entity<Review>(entity =>
            {
                entity.HasKey(r => r.ReviewId);
                entity.HasIndex(r => r.TripId);
                entity.HasIndex(r => r.ReviewType);
            });

            // CartItem configuration
            builder.Entity<CartItem>(entity =>
            {
                entity.HasKey(c => c.CartItemId);
                entity.HasIndex(c => c.UserId);

                // User can only have one cart item per trip
                entity.HasIndex(c => new { c.UserId, c.TripId }).IsUnique();
            });

            // TripReminderRule configuration
            builder.Entity<TripReminderSendLog>(entity =>
            {
                entity.HasKey(x => x.TripReminderSendLogId);
                entity.HasIndex(x => new { x.TripReminderRuleId, x.BookingId }).IsUnique();
            });

        }
    }
}