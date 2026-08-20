using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMediaPersonUserLinkService _mediaPersonUserLinks;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            IMediaPersonUserLinkService mediaPersonUserLinks)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _mediaPersonUserLinks = mediaPersonUserLinks ?? throw new ArgumentNullException(nameof(mediaPersonUserLinks));
        }

        public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
        public MediaUserPhotoIdentityLink? PhotoIdentity { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            PhotoIdentity = await _mediaPersonUserLinks.TryGetPhotoIdentityForUserAsync(
                user.Id,
                HttpContext.RequestAborted);

            var roles = await _userManager.GetRolesAsync(user);
            Roles = roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Page();
        }

        public async Task<IActionResult> OnPostPhotoAvatarAsync(bool usePhotosPortrait)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            try
            {
                await _mediaPersonUserLinks.SetAvatarPreferenceAsync(
                    user.Id,
                    usePhotosPortrait,
                    HttpContext.RequestAborted);
                StatusMessage = usePhotosPortrait
                    ? "Your linked Photos portrait will now be used as your PRISM profile image."
                    : "Your Photos portrait is no longer being used as your PRISM profile image.";
            }
            catch (Exception exception) when (IsExpectedLinkException(exception))
            {
                ErrorMessage = exception.Message;
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostReportPhotoIdentityAsync(string? reason)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            try
            {
                await _mediaPersonUserLinks.ReportIncorrectLinkAsync(
                    user.Id,
                    reason ?? string.Empty,
                    HttpContext.RequestAborted);
                StatusMessage = "Your Photos identity-link report has been sent for identity-manager review. The linked portrait has been removed from your PRISM avatar while the report is open.";
            }
            catch (Exception exception) when (IsExpectedLinkException(exception))
            {
                ErrorMessage = exception.Message;
            }

            return RedirectToPage();
        }

        private static bool IsExpectedLinkException(Exception exception)
            => exception is ArgumentException
                or InvalidOperationException
                or KeyNotFoundException;
    }
}
