using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class DeactivateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminMasterDataCommandService _commands;

    public DeactivateModel(ApplicationDbContext db, IAdminMasterDataCommandService commands)
    {
        _db = db;
        _commands = commands;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Restore { get; set; }

    [BindProperty]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int ProjectCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var entity = await LoadAsync(cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        RowVersion = entity.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await _commands.SetProjectTypeActiveAsync(Id, Restore, RowVersion, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorCode == "NotFound")
            {
                return NotFound();
            }

            var entity = await LoadAsync(cancellationToken);
            if (entity is null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.UserMessage ?? "The project type status could not be changed.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("./Index");
    }

    private async Task<ProjectManagement.Models.ProjectType?> LoadAsync(CancellationToken cancellationToken)
    {
        var entity = await _db.ProjectTypes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == Id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        Name = entity.Name;
        IsActive = entity.IsActive;
        ProjectCount = await _db.Projects.CountAsync(project => project.ProjectTypeId == Id, cancellationToken);
        return entity;
    }
}
