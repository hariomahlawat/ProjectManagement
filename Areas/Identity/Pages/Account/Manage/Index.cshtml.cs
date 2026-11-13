using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services.Accounts;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Celebrations;

namespace ProjectManagement.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMediaPersonUserLinkService _mediaPersonUserLinks;
        private readonly IAccountSignInHistoryService _signInHistory;
        private readonly IMyCelebrationService? _myCelebrations;

        /// <summary>How many recent sign-ins the page lists.</summary>
        public const int RecentSignInCount = 5;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            IMediaPersonUserLinkService mediaPersonUserLinks,
            IAccountSignInHistoryService signInHistory,
            IMyCelebrationService? myCelebrations = null)
        {
            _myCelebrations = myCelebrations;
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _mediaPersonUserLinks = mediaPersonUserLinks ?? throw new ArgumentNullException(nameof(mediaPersonUserLinks));
            _signInHistory = signInHistory ?? throw new ArgumentNullException(nameof(signInHistory));
        }

        /// <summary>One row of the Recent sign-ins list.</summary>
        /// <param name="When">Date and time in IST.</param>
        /// <param name="Client">Browser and system in words.</param>
        /// <param name="Ip">The address, when one was recorded.</param>
        /// <param name="FromThisAddress">True when it came from the address this request came from.</param>
        public sealed record SignInRow(string When, string Client, string? Ip, bool FromThisAddress);

        /// <summary>The signed-in user's own latest sign-ins, newest first.</summary>
        public IReadOnlyList<SignInRow> RecentSignIns { get; private set; } = Array.Empty<SignInRow>();

        public int SignInLookbackDays => AccountSignInHistoryService.LookbackDays;

        /// <summary>
        /// The person's own birthday and anniversary: the Celebrations entries linked to this
        /// account (docs/ux/celebration-wish.md). Empty for an account that is not a person's.
        /// </summary>
        public IReadOnlyList<MyCelebrationLine> MyCelebrations { get; private set; } = Array.Empty<MyCelebrationLine>();

        public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
        public MediaUserPhotoIdentityLink? PhotoIdentity { get; private set; }
        public string UserInitials { get; private set; } = "U";

        /// <summary>Rank and full name when the account has them, otherwise the user name.</summary>
        public string DisplayName { get; private set; } = "User";

        public string UserName { get; private set; } = string.Empty;

        /// <summary>The latest recorded sign-in in IST, or null when none is recorded.</summary>
        public string? LastSignInText { get; private set; }

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

            if (_myCelebrations is not null)
            {
                MyCelebrations = await _myCelebrations.GetAsync(user.Id, HttpContext.RequestAborted);
            }

            // Only ever the signed-in user's own events: the id comes from the session.
            var currentAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var signIns = await _signInHistory.GetRecentAsync(user.Id, RecentSignInCount, HttpContext.RequestAborted);
            RecentSignIns = signIns
                .Select(signIn => new SignInRow(
                    IstClock.ToIst(signIn.WhenUtc).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture),
                    signIn.Client,
                    signIn.Ip,
                    signIn.Ip is not null && string.Equals(signIn.Ip, currentAddress, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

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

        // Birthday and anniversary: always the signed-in person's own entry; the id comes from the session.
        public Task<IActionResult> OnPostSaveCelebrationAsync(CelebrationType type, byte day, byte month, short? year, string? spouseName)
            => ChangeMyCelebrationAsync((service, userId) =>
                Enum.IsDefined(type)
                    ? service.SaveAsync(userId, type, day, month, year, spouseName, HttpContext.RequestAborted)
                    : Task.FromResult(AdminOperationResult.Failure("Choose Birthday or Anniversary.")));

        public Task<IActionResult> OnPostClaimCelebrationAsync(Guid id)
            => ChangeMyCelebrationAsync((service, userId) => service.ClaimAsync(userId, id, HttpContext.RequestAborted));

        public Task<IActionResult> OnPostUnlinkCelebrationAsync(CelebrationType type)
            => ChangeMyCelebrationAsync((service, userId) => service.UnlinkAsync(userId, type, HttpContext.RequestAborted));

        private async Task<IActionResult> ChangeMyCelebrationAsync(Func<IMyCelebrationService, string, Task<AdminOperationResult>> change)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (_myCelebrations is null) return RedirectToPage();

            var result = await change(_myCelebrations, user.Id);
            if (result.Succeeded)
            {
                StatusMessage = result.UserMessage;
            }
            else
            {
                ErrorMessage = result.UserMessage ?? "It could not be saved.";
            }

            return RedirectToPage();
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
                StatusMessage = "Your Photos identity-link report has been sent for identity-manager review. The linked portrait is no longer being used as your PRISM profile image while the report is open.";
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

            UserName = headerName;
            var fullName = user.FullName?.Trim() ?? string.Empty;
            var rank = user.Rank?.Trim() ?? string.Empty;
            DisplayName = fullName.Length == 0
                ? headerName
                : rank.Length == 0 ? fullName : $"{rank} {fullName}";
            LastSignInText = user.LastLoginUtc.HasValue
                ? IstClock.ToIst(user.LastLoginUtc.Value).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
                : null;
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
