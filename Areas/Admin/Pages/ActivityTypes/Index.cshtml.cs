using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.ActivityTypes;

// On the shared list page (docs/ux/admin-review-3-fixes.md, owner decision 1, PR 11). Activity
// types have no stored order, so the list has no Order column and is read A to Z.
[Authorize(Policy = AdminPolicies.ActivityTypesManage)]
public sealed class IndexModel : AdminReferenceListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.ActivityTypes, "bi-list-task", "Add activity type", "activities", "/ActivityTypes");

    private readonly IActivityTypeService _activityTypes;
    private readonly IAuthorizationService _authorization;

    public IndexModel(
        IReferenceListService lists,
        IListOrderNoticeService order,
        IActivityTypeService activityTypes,
        IAuthorizationService authorization)
        : base(lists, order)
    {
        _activityTypes = activityTypes ?? throw new ArgumentNullException(nameof(activityTypes));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.ActivityTypes;
    protected override string? OrderCheckKey => null;
    protected override AdminReferenceListPresentation Presentation => Shown;
    protected override string? Note => "Listed A to Z, as they appear in activity forms.";
    protected override Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle => SetActiveAsync;

    // Activity type managers who cannot open Lists used in forms get no link to it.
    protected override async Task<bool> ShowListsCentreLinkAsync(CancellationToken cancellationToken) =>
        (await _authorization.AuthorizeAsync(User, AdminPolicies.MasterDataManage)).Succeeded;

    // The form sends the state asked for and the version it showed, so a resubmitted form or
    // another administrator's change cannot flip the type the wrong way. A form without them
    // (an older page) flips the type with the stored version, as before.
    private async Task<AdminOperationResult> SetActiveAsync(int id, bool? isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        try
        {
            var existing = (await _activityTypes.ListAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return AdminOperationResult.Failure("That activity type is no longer in the list.", "NotFound");
            }

            var target = isActive ?? !existing.IsActive;
            if (existing.IsActive == target)
            {
                return AdminOperationResult.Success(target ? $"'{existing.Name}' is already active." : $"'{existing.Name}' is already inactive.");
            }

            await _activityTypes.UpdateAsync(
                id,
                new ActivityTypeInput(existing.Name, existing.Description, target, rowVersion.Length > 0 ? rowVersion : existing.RowVersion),
                cancellationToken);
            return AdminOperationResult.Success(target ? $"Reactivated '{existing.Name}'." : $"Deactivated '{existing.Name}'.");
        }
        catch (ActivityAuthorizationException)
        {
            return AdminOperationResult.Failure("You are not allowed to change activity types.", "Forbidden");
        }
        catch (ActivityValidationException exception)
        {
            return AdminOperationResult.Failure(string.Join(" ", exception.Errors.SelectMany(item => item.Value)), "Validation");
        }
    }
}
