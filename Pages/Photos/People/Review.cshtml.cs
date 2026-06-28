using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize(Roles = "Admin,HoD")]
public sealed class ReviewModel : PageModel
{
    private const int PageSize = 24;
    private readonly IMediaPeopleQueryService _people;
    private readonly IFaceReviewService _review;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(
        IMediaPeopleQueryService people,
        IFaceReviewService review,
        IOptions<MediaLibraryOptions> options,
        ILogger<ReviewModel> logger)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public FaceReviewQueueResult Result { get; private set; } = new(
        Array.Empty<FaceReviewQueueItem>(),
        Array.Empty<MediaPersonOption>(),
        0,
        1,
        PageSize,
        false,
        false);

    public bool FeatureEnabled => _options.People.Enabled;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!FeatureEnabled)
        {
            return NotFound();
        }

        Result = await _people.GetReviewQueueAsync(
            Math.Max(1, PageNumber),
            PageSize,
            cancellationToken);
        PageNumber = Result.PageNumber;
        return Page();
    }

    public Task<IActionResult> OnPostConfirmAsync(
        Guid faceId,
        Guid personId,
        double? confidence,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, confidence, cancellationToken),
            "Identity confirmed.");

    public Task<IActionResult> OnPostRejectAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, personId, UserId, cancellationToken),
            "Suggestion rejected. It will not be recreated for this model version.");

    public Task<IActionResult> OnPostRejectAllAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, null, UserId, cancellationToken),
            "All current suggestions for this face were rejected.");

    public Task<IActionResult> OnPostAssignExistingAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, null, cancellationToken),
            "Identity assigned to the selected person.");

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreAsync(faceId, UserId, cancellationToken),
            "Face acknowledged and left unidentified.");

    public Task<IActionResult> OnPostCreateAsync(
        Guid faceId,
        string displayName,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.CreatePersonAndAssignAsync(faceId, displayName, UserId, cancellationToken),
            "Person created and identity confirmed.");

    public Task<IActionResult> OnPostSuppressAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.SuppressAsync(faceId, UserId, cancellationToken),
            "Detection marked as not a face.");

    private string UserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, string successMessage)
    {
        if (!FeatureEnabled)
        {
            return NotFound();
        }

        try
        {
            await action();
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or InvalidOperationException
                                           or KeyNotFoundException)
        {
            _logger.LogWarning(exception, "Face review operation failed.");
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { PageNumber });
    }
}
