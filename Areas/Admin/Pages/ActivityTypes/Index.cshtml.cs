using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Activities;

namespace ProjectManagement.Areas.Admin.Pages.ActivityTypes;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private readonly IActivityTypeService _activityTypeService;

    public IndexModel(IActivityTypeService activityTypeService)
    {
        _activityTypeService = activityTypeService;
    }

    public IReadOnlyList<ActivityTypeRow> ActivityTypes { get; private set; } = Array.Empty<ActivityTypeRow>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActivityTypes = await LoadActivityTypesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.ListAsync(cancellationToken);
        var existing = types.FirstOrDefault(t => t.Id == id);
        if (existing is null)
        {
            return NotFound();
        }

        try
        {
            await _activityTypeService.UpdateAsync(
                id,
                new ActivityTypeInput(existing.Name, existing.Description, !existing.IsActive),
                cancellationToken);

            StatusMessage = existing.IsActive
                ? $"Deactivated '{existing.Name}'."
                : $"Activated '{existing.Name}'.";
        }
        catch (ActivityAuthorizationException)
        {
            ErrorMessage = "You are not authorised to manage activity types.";
        }
        catch (ActivityValidationException ex)
        {
            ErrorMessage = string.Join(" ", ex.Errors.SelectMany(e => e.Value));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        return RedirectToPage();
    }

    private async Task<IReadOnlyList<ActivityTypeRow>> LoadActivityTypesAsync(CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.ListAsync(cancellationToken);

        return types
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new ActivityTypeRow
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                IsActive = t.IsActive,
                LastModifiedAtUtc = t.LastModifiedAtUtc,
                LastModifiedByUserId = t.LastModifiedByUserId
            })
            .ToList();
    }

    public sealed class ActivityTypeRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset? LastModifiedAtUtc { get; init; }

        public string? LastModifiedByUserId { get; init; }
    }
}
