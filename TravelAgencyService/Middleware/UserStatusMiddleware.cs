using Microsoft.AspNetCore.Identity;
using TravelAgencyService.Models;

namespace TravelAgencyService.Middleware
{
    public class UserStatusMiddleware
    {
        private readonly RequestDelegate _next;

        public UserStatusMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var user = await userManager.GetUserAsync(context.User);

                if (user == null || !user.IsActive)
                {
                    // User was deleted or deactivated - sign them out
                    await signInManager.SignOutAsync();

                    // Redirect to login with message
                    context.Response.Redirect("/Account/Login?deactivated=true");
                    return;
                }
            }

            await _next(context);
        }
    }

    // Extension method to make it easy to add the middleware
    public static class UserStatusMiddlewareExtensions
    {
        public static IApplicationBuilder UseUserStatusCheck(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UserStatusMiddleware>();
        }
    }
}