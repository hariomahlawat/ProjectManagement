using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

public sealed partial class IndexModel
{
    private const int PersonDiscoveryPageSize = 120;

    public PersonPhotoDiscoverySummary? PersonProfile { get; private set; }
    public PersonPhotoDiscoveryResult? PersonDiscovery { get; private set; }
    public MediaUserPhotoIdentityLink? CurrentUserMediaLink { get; private set; }
    public bool IsCurrentUserLinkedPerson { get; private set; }

    public bool IsSinglePersonProfile
        => IsPeopleGallery && SelectedPeople.Count == 1;

    public bool IsMultiPersonGallery
        => IsPeopleGallery && SelectedPeople.Count > 1;

    public bool CanReviewPersonCandidates
        => PeopleFeatureEnabled && (CanManagePeople || IsCurrentUserLinkedPerson);

    public bool ShowPersonDiscovery
        => IsSinglePersonProfile && CanReviewPersonCandidates && FindMore;

    public string? PersonProfileDisplayName
        => PersonProfile?.DisplayName ?? SelectedPeople.FirstOrDefault()?.Name;

    public string PersonDiscoveryPrimaryLabel
        => IsCurrentUserLinkedPerson ? "Find more photos of me" : "Find more photos";

    public string PersonDiscoveryHeading
        => IsCurrentUserLinkedPerson ? "Possible photos of me" : $"Possible photos of {PersonProfileDisplayName ?? "this person"}";

    public string CandidateConfirmLabel
        => IsCurrentUserLinkedPerson ? "Yes, that's me" : $"Confirm as {PersonProfileDisplayName ?? "this person"}";

    public string CandidateRejectLabel
        => IsCurrentUserLinkedPerson ? "Not me" : $"Not {PersonProfileDisplayName ?? "this person"}";

    public string BuildPersonDiscoveryUrl(bool open)
        => BuildPhotosUrl(
            PersonIds,
            pageNumber: 1,
            findMore: open);

    public string BuildPersonDiscoveryStatusUrl(Guid personId)
        => Url.Page(
               "/Photos/Index",
               "PersonDiscoveryStatus",
               new { personId })
           ?? $"/Photos?handler=PersonDiscoveryStatus&personId={personId:D}";

    public string BuildPersonMatchingSetupUrl(Guid personId)
    {
        var page = Url.Page("/Photos/People/Details", new { id = personId })
                   ?? $"/Photos/People/Details/{personId:D}";
        return page + "#matching-reference-setup";
    }

    public static string CandidateEvidenceLabel(FaceCandidateConfidenceLevel level)
        => level switch
        {
            FaceCandidateConfidenceLevel.Strong => "Strong similarity",
            FaceCandidateConfidenceLevel.Possible => "Possible match",
            _ => "Review candidate"
        };

    public static string CandidateEvidenceCssClass(FaceCandidateConfidenceLevel level)
        => level switch
        {
            FaceCandidateConfidenceLevel.Strong => "is-strong",
            FaceCandidateConfidenceLevel.Possible => "is-possible",
            _ => "is-review"
        };

    private async Task LoadPersonPhotoProfileAsync(CancellationToken cancellationToken)
    {
        PersonProfile = null;
        PersonDiscovery = null;

        CurrentUserMediaLink = null;
        IsCurrentUserLinkedPerson = false;

        if (!IsSinglePersonProfile)
        {
            FindMore = false;
            return;
        }

        try
        {
            var personId = SelectedPeople[0].Id;
            CurrentUserMediaLink = await _personUserLinks.TryGetPhotoIdentityForUserAsync(PersonProfileUserId, cancellationToken);
            IsCurrentUserLinkedPerson = CurrentUserMediaLink?.PersonId == personId;
            if (!CanReviewPersonCandidates)
            {
                FindMore = false;
            }
            PersonProfile = await _personDiscovery.GetSummaryAsync(
                personId,
                includeDiscoveryState: CanReviewPersonCandidates,
                cancellationToken);

            if (ShowPersonDiscovery)
            {
                PersonDiscovery = await _personDiscovery.GetCandidatesAsync(
                    personId,
                    PersonDiscoveryPageSize,
                    cancellationToken);
                PersonProfile = PersonDiscovery?.Summary ?? PersonProfile;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to load the person-photo discovery state for the Photos profile.");
            FindMore = false;
        }
    }

    public async Task<IActionResult> OnGetPersonDiscoveryStatusAsync(
        Guid personId,
        CancellationToken cancellationToken)
    {
        if (!await CanReviewPersonAsync(personId, cancellationToken))
        {
            return Forbid();
        }

        var summary = await _personDiscovery.GetSummaryAsync(
            personId,
            includeDiscoveryState: true,
            cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        return new JsonResult(BuildPersonDiscoverySummaryPayload(summary));
    }

    public Task<IActionResult> OnPostConfirmPersonCandidateAsync(
        Guid personId,
        Guid faceId,
        string? returnUrl,
        CancellationToken cancellationToken)
        => ExecutePersonCandidateMutationAsync(
            personId,
            new[] { faceId },
            returnUrl,
            confirm: true,
            cancellationToken);

    public Task<IActionResult> OnPostRejectPersonCandidateAsync(
        Guid personId,
        Guid faceId,
        string? returnUrl,
        CancellationToken cancellationToken)
        => ExecutePersonCandidateMutationAsync(
            personId,
            new[] { faceId },
            returnUrl,
            confirm: false,
            cancellationToken);

    public Task<IActionResult> OnPostConfirmPersonCandidatesAsync(
        Guid personId,
        List<Guid> faceIds,
        string? returnUrl,
        CancellationToken cancellationToken)
        => ExecutePersonCandidateMutationAsync(
            personId,
            faceIds,
            returnUrl,
            confirm: true,
            cancellationToken);

    public Task<IActionResult> OnPostRejectPersonCandidatesAsync(
        Guid personId,
        List<Guid> faceIds,
        string? returnUrl,
        CancellationToken cancellationToken)
        => ExecutePersonCandidateMutationAsync(
            personId,
            faceIds,
            returnUrl,
            confirm: false,
            cancellationToken);

    private async Task<IActionResult> ExecutePersonCandidateMutationAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        string? returnUrl,
        bool confirm,
        CancellationToken cancellationToken)
    {
        if (!await CanReviewPersonAsync(personId, cancellationToken))
        {
            return Forbid();
        }

        var selectedFaces = (faceIds ?? Array.Empty<Guid>())
            .Where(faceId => faceId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (personId == Guid.Empty || selectedFaces.Length == 0)
        {
            return PersonCandidateFailure(
                "Select at least one current person-match suggestion.",
                StatusCodes.Status400BadRequest,
                personId,
                returnUrl);
        }

        try
        {
            var eligible = await _personDiscovery.GetEligibleCandidatesAsync(
                personId,
                selectedFaces,
                cancellationToken);
            if (eligible.Count != selectedFaces.Length)
            {
                throw new FaceIdentityConflictException(
                    "One or more suggestions changed while you were reviewing them. Refresh the profile and continue with the remaining matches.");
            }

            var selfReview = await IsLinkedSelfAsync(personId, cancellationToken);
            var reviewSource = selfReview ? "SelfConfirmed" : "PersonPhotoProfile";
            if (confirm)
            {
                await _faceReview.AssignManyAsync(
                    selectedFaces,
                    personId,
                    PersonProfileUserId,
                    confidence: null,
                    cancellationToken: cancellationToken,
                    reviewSource: reviewSource);
            }
            else
            {
                await _faceReview.RejectManyAsync(
                    selectedFaces,
                    personId,
                    PersonProfileUserId,
                    cancellationToken: cancellationToken,
                    reviewSource: reviewSource);
            }

            var summary = await _personDiscovery.GetSummaryAsync(
                personId,
                includeDiscoveryState: true,
                cancellationToken);
            var personName = summary?.DisplayName ?? "the selected person";
            var message = confirm
                ? selfReview
                    ? selectedFaces.Length == 1
                        ? "Photo confirmed as you."
                        : $"{selectedFaces.Length} photos confirmed as you."
                    : selectedFaces.Length == 1
                        ? $"Appearance confirmed as {personName}."
                        : $"{selectedFaces.Length} appearances confirmed as {personName}."
                : selfReview
                    ? selectedFaces.Length == 1
                        ? "Photo marked as not you."
                        : $"{selectedFaces.Length} photos marked as not you."
                    : selectedFaces.Length == 1
                        ? $"Suggestion rejected for {personName}."
                        : $"{selectedFaces.Length} suggestions rejected for {personName}.";

            if (WantsPersonProfileJson)
            {
                return new JsonResult(new
                {
                    ok = true,
                    message,
                    confirmed = confirm,
                    faceIds = selectedFaces,
                    summary = summary is null
                        ? null
                        : BuildPersonDiscoverySummaryPayload(summary)
                });
            }

            TempData["PhotosSuccess"] = message;
            return Redirect(GetSafePersonProfileReturnUrl(returnUrl, personId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FaceIdentityConflictException exception)
        {
            return PersonCandidateFailure(
                exception.Message,
                StatusCodes.Status409Conflict,
                personId,
                returnUrl);
        }
        catch (KeyNotFoundException exception)
        {
            return PersonCandidateFailure(
                exception.Message,
                StatusCodes.Status404NotFound,
                personId,
                returnUrl);
        }
        catch (ArgumentException exception)
        {
            return PersonCandidateFailure(
                exception.Message,
                StatusCodes.Status400BadRequest,
                personId,
                returnUrl);
        }
    }

    private IActionResult PersonCandidateFailure(
        string message,
        int statusCode,
        Guid personId,
        string? returnUrl)
    {
        if (WantsPersonProfileJson)
        {
            return new JsonResult(new { ok = false, message })
            {
                StatusCode = statusCode
            };
        }

        TempData["PhotosError"] = message;
        return Redirect(GetSafePersonProfileReturnUrl(returnUrl, personId));
    }

    private object BuildPersonDiscoverySummaryPayload(PersonPhotoDiscoverySummary summary)
        => new
        {
            personId = summary.PersonId,
            displayName = summary.DisplayName,
            confirmedPhotoCount = summary.ConfirmedPhotoCount,
            latestMediaDateUtc = summary.LatestMediaDateUtc,
            latestMediaDateLabel = summary.LatestMediaDateUtc?.ToLocalTime().ToString("dd MMM yyyy"),
            trustedReferenceCount = summary.TrustedReferenceCount,
            possibleMatchCount = summary.PossibleMatchCount,
            backgroundMatchingCount = summary.BackgroundMatchingCount,
            matchingFailureCount = summary.MatchingFailureCount,
            directCandidateCount = summary.DirectCandidateCount,
            groupCandidateAppearanceCount = summary.GroupCandidateAppearanceCount,
            groupCandidateCount = summary.GroupCandidateCount
        };

    private async Task<bool> CanReviewPersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        if (!PeopleFeatureEnabled || personId == Guid.Empty) return false;
        if (CanManagePeople) return true;
        return await IsLinkedSelfAsync(personId, cancellationToken);
    }

    private async Task<bool> IsLinkedSelfAsync(Guid personId, CancellationToken cancellationToken)
    {
        var link = await _personUserLinks.TryGetPhotoIdentityForUserAsync(PersonProfileUserId, cancellationToken);
        return link?.PersonId == personId;
    }

    private string GetSafePersonProfileReturnUrl(string? returnUrl, Guid personId)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return Url.Page(
                   "/Photos/Index",
                   new
                   {
                       View = "photos",
                       PersonIds = personId,
                       FindMore = true
                   })
               ?? $"/Photos?View=photos&PersonIds={personId:D}&FindMore=true";
    }

    private string PersonProfileUserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private bool WantsPersonProfileJson
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
}

public sealed record PersonPhotoCandidateCardViewModel(
    PersonPhotoCandidate Candidate,
    Guid PersonId,
    string PersonDisplayName,
    string ConfirmLabel,
    string RejectLabel,
    string ReturnUrl);
