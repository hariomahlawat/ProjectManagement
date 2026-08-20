using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Features.MediaLibrary.Services;

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
    }
}
