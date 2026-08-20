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
        public string UserInitials { get; private set; } = "U";

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

            PopulateAccountPresentation(user);

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

        public Task<IActionResult> OnPostUsePhotosPortraitAsync()
            => SetPhotoAvatarAsync(usePhotosPortrait: true);

        public Task<IActionResult> OnPostUseInitialsAsync()
            => SetPhotoAvatarAsync(usePhotosPortrait: false);

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

        private async Task<IActionResult> SetPhotoAvatarAsync(bool usePhotosPortrait)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            try
            {
                var authoritative = await _mediaPersonUserLinks.SetAvatarPreferenceAsync(
                    user.Id,
                    usePhotosPortrait,
                    HttpContext.RequestAborted);

                if (usePhotosPortrait && !authoritative.ShouldUsePortraitAsAvatar)
                {
                    throw new InvalidOperationException(
                        "PRISM could not verify that the Photos portrait is active as your profile image. Refresh the page and try again.");
                }

                if (!usePhotosPortrait && authoritative.UsePortraitAsAvatar)
                {
                    throw new InvalidOperationException(
                        "PRISM could not verify that the Photos portrait was disabled. Refresh the page and try again.");
                }

                StatusMessage = usePhotosPortrait
                    ? "Your Photos portrait is now being used as your PRISM profile image."
                    : "Your PRISM profile image is now using your initials.";
            }
            catch (Exception exception) when (IsExpectedLinkException(exception))
            {
                ErrorMessage = exception.Message;
            }

            return RedirectToPage();
        }

        private void PopulateAccountPresentation(ApplicationUser user)
        {
            // Keep the preview consistent with _LoginPartial, which is based on the
            // authenticated account name shown in the PRISM header.
            var headerName = !string.IsNullOrWhiteSpace(user.UserName)
                ? user.UserName.Trim()
                : !string.IsNullOrWhiteSpace(user.FullName)
                    ? user.FullName.Trim()
                    : "User";
            UserInitials = BuildInitials(headerName);
        }

        private static string BuildInitials(string value)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();
            }

            var source = parts.Length == 1 ? parts[0] : value.Trim();
            return source.Length == 0 ? "U" : char.ToUpperInvariant(source[0]).ToString();
        }

        private static bool IsExpectedLinkException(Exception exception)
            => exception is ArgumentException
                or InvalidOperationException
                or KeyNotFoundException;
    }
}
