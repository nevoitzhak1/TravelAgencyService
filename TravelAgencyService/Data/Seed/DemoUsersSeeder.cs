using Microsoft.AspNetCore.Identity;
using TravelAgencyService.Data;
using TravelAgencyService.Models;

public static class DemoUsersSeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext db)
    {
        const string roleName = "User";
        const string password = "Demo123!";

        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole(roleName));

        var users = new (string First, string Last, string Email)[]
        {
            ("Emma","Johnson","emma@demo.com"),
            ("Liam","Williams","liam@demo.com"),
            ("Olivia","Brown","olivia@demo.com"),
            ("Noah","Jones","noah@demo.com"),
            ("Ava","Garcia","ava@demo.com"),
            ("Ethan","Miller","ethan@demo.com"),
            ("Sophia","Davis","sophia@demo.com"),
            ("Mason","Rodriguez","mason@demo.com"),
            ("Isabella","Martinez","isabella@demo.com"),
            ("Lucas","Hernandez","lucas@demo.com"),
            ("Mia","Lopez","mia@demo.com"),
            ("James","Gonzalez","james@demo.com"),
            ("Amelia","Wilson","amelia@demo.com"),
            ("Benjamin","Anderson","ben@demo.com"),
            ("Charlotte","Taylor","charlotte@demo.com"),
            ("Henry","Thomas","henry@demo.com"),
            ("Harper","Moore","harper@demo.com"),
            ("Alexander","Jackson","alex@demo.com"),
            ("Evelyn","White","evelyn@demo.com"),
            ("Daniel","Harris","daniel@demo.com")
        };

        // Reviews templates (site reviews)
        var reviewTemplates = new (string Title, string Comment)[]
        {
            ("Smooth booking experience", "The website is very easy to use. Booking was quick and the confirmation arrived instantly."),
            ("Clean UI and fast flow", "Everything feels organized and modern. The checkout flow is simple and clear."),
            ("Great overall experience", "Navigation is clear, pages load fast, and the process is straightforward."),
            ("Professional and reliable", "Looks professional and works smoothly. I found a trip and booked in minutes."),
            ("Highly recommended", "Fast, clean, and intuitive. Great experience end-to-end."),
            ("Better than expected", "Surprisingly smooth experience. The design is clean and the steps are well explained."),
            ("Excellent usability", "The layout is clean and the booking steps are really clear. Loved the experience."),
            ("Quick and reliable", "No issues at all. The site loads fast and feels very polished.")
        };

        var rnd = new Random();

        foreach (var u in users)
        {
            var user = await userManager.FindByEmailAsync(u.Email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = u.Email,
                    Email = u.Email,
                    FirstName = u.First,
                    LastName = u.Last,
                    IsActive = true,
                    RegistrationDate = DateTime.Now,
                    EmailConfirmed = true
                };

                var createRes = await userManager.CreateAsync(user, password);
                if (!createRes.Succeeded) continue;

                await userManager.AddToRoleAsync(user, roleName);
            }

            // ✅ Add ONE site review per demo user (if not already exists)
            bool alreadyHasSiteReview = db.Reviews.Any(r =>
                r.UserId == user.Id &&
                r.ReviewType == ReviewType.WebsiteReview &&
                r.TripId == null);

            if (!alreadyHasSiteReview)
            {
                var t = reviewTemplates[rnd.Next(reviewTemplates.Length)];
                var rating = rnd.Next(0, 2) == 0 ? 5 : 4;

                db.Reviews.Add(new Review
                {
                    UserId = user.Id,
                    TripId = null,
                    Rating = rating,
                    Title = t.Title,
                    Comment = t.Comment,
                    ReviewType = ReviewType.WebsiteReview,
                    IsApproved = true,
                    CreatedAt = DateTime.Now.AddDays(-rnd.Next(1, 25)),
                    UpdatedAt = DateTime.Now
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
