using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private readonly SocialMediaEventService _eventService;
    private readonly IAuthorizationService _authorizationService;

    public DetailsModel(SocialMediaEventService eventService, IAuthorizationService authorizationService)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public SocialMediaEvent? Event { get; private set; }

    public SocialMediaEventType? EventType { get; private set; }

    public SocialMediaEventPhotoGalleryModel PhotoGallery { get; private set; } = new(Guid.Empty, Array.Empty<SocialMediaEventPhotoItem>(), false);

    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await _eventService.GetDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        Event = details.Event;
        EventType = details.EventType;

        var photos = details.Photos
            .OrderBy(x => x.CreatedAtUtc)
            .Select(photo => new SocialMediaEventPhotoItem(
                photo.Id,
                photo.Caption,
                photo.VersionStamp,
                photo.IsCover,
                Convert.ToBase64String(photo.RowVersion)))
            .ToList();

        var authorizationResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ManageSocialMediaEvents);
        CanManage = authorizationResult.Succeeded;

        PhotoGallery = new SocialMediaEventPhotoGalleryModel(details.Event.Id, photos, false);
        return Page();
    }
}
