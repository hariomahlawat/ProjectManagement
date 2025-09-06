using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProjectManagement.Models;

namespace ProjectManagement.Infrastructure
{
    public class EnforcePasswordChangeFilter : IAsyncPageFilter
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public EnforcePasswordChangeFilter(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/Identity/Account/Login") ||
                path.StartsWith("/Identity/Account/Logout") ||
                path.StartsWith("/Identity/Account/Manage/ChangePassword") ||
                path.StartsWith("/css") || path.StartsWith("/js"))
            {
                await next();
                return;
            }

            if (context.HttpContext.User?.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(context.HttpContext.User);
                if (user != null && user.MustChangePassword)
                {
                    context.Result = new RedirectToPageResult("/Account/Manage/ChangePassword", new { area = "Identity" });
                    return;
                }
            }

            await next();
        }
    }
}
