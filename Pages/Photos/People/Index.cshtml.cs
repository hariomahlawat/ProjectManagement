using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int DefaultPageSize = 36;
    private readonly IMediaPeopleQueryService _people;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IMediaPeopleQueryService people,
        IOptions<MediaLibraryOptions> options,
        ILogger<IndexModel> logger)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "name";

    [BindProperty(SupportsGet = true)]
    public bool IncludeHidden { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public Guid[] SelectedIds { get; set; } = Array.Empty<Guid>();

    [BindProperty(SupportsGet = true)]
    public string PeopleMatch { get; set; } = "all";

    public MediaPeopleIndexResult Result { get; private set; } = new(
        Array.Empty<MediaPersonCard>(), 0, 0, 0, 1, DefaultPageSize, false, false);

    public bool FeatureEnabled => _options.People.Enabled;
    public bool CanManageIdentities => User.IsInRole("Admin") || User.IsInRole("HoD");
    public bool DirectoryAvailable { get; private set; } = true;
    public string ReviewMode => Result.KnownPersonSuggestionCount > 0
        ? "matches"
        : Result.UnidentifiedFaceCount > 0
            ? "unidentified"
            : "groups";
    public int ReviewWorkCount => Result.PendingReviewCount;

    public string BuildPageUrl(int pageNumber)
    {
        var values = new RouteValueDictionary
        {
            ["Q"] = Q,
            ["Sort"] = Sort,
            ["IncludeHidden"] = CanManageIdentities && IncludeHidden ? true : null,
            ["PageNumber"] = pageNumber > 1 ? pageNumber : null,
            ["PeopleMatch"] = SelectedIds.Length > 1 ? PeopleMatch : null
        };
        for (var index = 0; index < SelectedIds.Length; index++)
        {
            values[$"SelectedIds[{index}]"] = SelectedIds[index];
        }

        return Url.Page("/Photos/People/Index", values) ?? "/Photos/People";
    }

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
        SelectedIds = (SelectedIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(10)
            .ToArray();
        PeopleMatch = SelectedIds.Length > 1
                      && string.Equals(PeopleMatch?.Trim(), "any", StringComparison.OrdinalIgnoreCase)
            ? "any"
            : "all";

        if (!CanManageIdentities)
        {
            IncludeHidden = false;
        }

        if (!FeatureEnabled)
        {
            return;
        }

        try
        {
            Result = await _people.GetIndexAsync(
                new MediaPeopleIndexQuery(
                    Q,
                    Sort,
                    CanManageIdentities && IncludeHidden,
                    PageNumber,
                    SelectedIds.Length > 0 ? 120 : DefaultPageSize),
                cancellationToken);
            PageNumber = Result.PageNumber;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DirectoryAvailable = false;
            _logger.LogError(exception,
                "The confirmed-people directory could not be loaded.");
        }
    }
}
