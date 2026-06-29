using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private const int DefaultPageSize = 36;
    private readonly IMediaPeopleQueryService _people;
    private readonly IFaceIdentityGroupingService _groups;
    private readonly MediaLibraryOptions _options;

    public IndexModel(
        IMediaPeopleQueryService people,
        IFaceIdentityGroupingService groups,
        IOptions<MediaLibraryOptions> options)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _groups = groups ?? throw new ArgumentNullException(nameof(groups));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "name";

    [BindProperty(SupportsGet = true)]
    public bool IncludeHidden { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public MediaPeopleIndexResult Result { get; private set; } = new(
        Array.Empty<MediaPersonCard>(), 0, 0, 0, 1, DefaultPageSize, false, false);

    public bool FeatureEnabled => _options.People.Enabled;
    public int SuggestedGroupCount { get; private set; }
    public int GroupedFaceCount { get; private set; }
    public int RemainingIndividualFaceCount { get; private set; }
    public int ReviewWorkCount => SuggestedGroupCount + RemainingIndividualFaceCount;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        Sort = Sort?.Trim().ToLowerInvariant() switch
        {
            "photos" => "photos",
            "recent" => "recent",
            _ => "name"
        };
        PageNumber = Math.Max(1, PageNumber);

        if (!FeatureEnabled)
        {
            return;
        }

        Result = await _people.GetIndexAsync(
            new MediaPeopleIndexQuery(Q, Sort, IncludeHidden, PageNumber, DefaultPageSize),
            cancellationToken);
        var groups = await _groups.GetGroupsAsync(cancellationToken);
        SuggestedGroupCount = groups.TotalGroups;
        GroupedFaceCount = groups.GroupedFaceCount;
        RemainingIndividualFaceCount = groups.RemainingIndividualFaceCount;
        PageNumber = Result.PageNumber;
    }
}
