using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize(Roles = "Admin,HoD")]
public sealed class DetailsModel : PageModel
{
    private readonly IMediaPeopleQueryService _people;
    private readonly IFaceReviewService _review;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IMediaPeopleQueryService people,
        IFaceReviewService review,
        IOptions<MediaLibraryOptions> options,
        ILogger<DetailsModel> logger)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MediaPersonDetailsResult Person { get; private set; } = null!;
    public bool FeatureEnabled => _options.People.Enabled;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!FeatureEnabled)
        {
            return NotFound();
        }

        var person = await _people.GetPersonAsync(id, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        Person = person;
        return Page();
    }

    public Task<IActionResult> OnPostRenameAsync(
        Guid id,
        string displayName,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            id,
            () => _review.RenamePersonAsync(id, displayName, UserId, cancellationToken),
            "Person renamed.");

    public Task<IActionResult> OnPostVisibilityAsync(
        Guid id,
        bool hidden,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            id,
            () => _review.SetPersonHiddenAsync(id, hidden, UserId, cancellationToken),
            hidden ? "Person hidden from normal browsing." : "Person restored to normal browsing.");

    public Task<IActionResult> OnPostRepresentativeAsync(
        Guid id,
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            id,
            () => _review.SetRepresentativeFaceAsync(id, faceId, UserId, cancellationToken),
            "Representative photograph updated.");

    public Task<IActionResult> OnPostRemoveAssignmentAsync(
        Guid id,
        Guid faceId,
        string? reason,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            id,
            () => _review.RemoveAssignmentAsync(faceId, id, UserId, reason, cancellationToken),
            "Incorrect face assignment removed.");

    public async Task<IActionResult> OnPostMergeAsync(
        Guid id,
        Guid targetPersonId,
        CancellationToken cancellationToken)
    {
        if (!FeatureEnabled)
        {
            return NotFound();
        }

        try
        {
            await _review.MergePeopleAsync(id, targetPersonId, UserId, cancellationToken);
            StatusMessage = "People merged successfully. All active assignments now belong to the selected person.";
            return RedirectToPage("./Details", new { id = targetPersonId });
        }
        catch (Exception exception) when (IsExpectedReviewException(exception))
        {
            _logger.LogWarning(exception, "Unable to merge media person {SourcePersonId} into {TargetPersonId}.", id, targetPersonId);
            ErrorMessage = exception.Message;
            return RedirectToPage("./Details", new { id });
        }
    }

    private string UserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private async Task<IActionResult> ExecuteAsync(
        Guid personId,
        Func<Task> action,
        string successMessage)
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
        catch (Exception exception) when (IsExpectedReviewException(exception))
        {
            _logger.LogWarning(exception, "People governance operation failed for person {PersonId}.", personId);
            ErrorMessage = exception.Message;
        }

        return RedirectToPage("./Details", new { id = personId });
    }

    private static bool IsExpectedReviewException(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException;
}
