using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public class EditModel : PageModel
{
    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaCommandService _commands;
    private readonly ProjectIdeaPermissionService _permissions;
    private readonly UserManager<ApplicationUser> _users;

    public EditModel(
        ProjectIdeaReadService read,
        ProjectIdeaCommandService commands,
        ProjectIdeaPermissionService permissions,
        UserManager<ApplicationUser> users)
    {
        _read = read;
        _commands = commands;
        _permissions = permissions;
        _users = users;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public SelectList ProjectOfficerOptions { get; private set; } = default!;
    public SelectList HodOptions { get; private set; } = default!;

    public SelectList EditableStatusOptions { get; } = new(
        new[]
        {
            new { Value = ProjectIdeaStatuses.Active, Text = "Active" },
            new { Value = ProjectIdeaStatuses.OnHold, Text = "On Hold" }
        },
        "Value",
        "Text");

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class InputModel
    {
        public int Id { get; set; }
        public string RowVersion { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        public string? AssignedProjectOfficerUserId { get; set; }
        public string? AssignedHodUserId { get; set; }
        public string Status { get; set; } = ProjectIdeaStatuses.Active;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var idea = await _read.GetDetailsAsync(id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditIdea(User, idea)) return Forbid();

        Input = new InputModel
        {
            Id = idea.Id,
            RowVersion = EncodeRowVersion(idea.RowVersion),
            Title = idea.Title,
            Description = idea.Description,
            AssignedProjectOfficerUserId = idea.AssignedProjectOfficerUserId,
            AssignedHodUserId = idea.AssignedHodUserId,
            Status = idea.Status
        };

        await LoadUsersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var idea = await _read.GetDetailsAsync(Input.Id);
        if (idea is null) return NotFound();
        if (!_permissions.CanEditIdea(User, idea)) return Forbid();

        ValidateCoreFields();
        if (!ModelState.IsValid)
        {
            await LoadUsersAsync();
            return Page();
        }

        idea.Title = Input.Title.Trim();
        idea.Description = Input.Description.Trim();
        idea.AssignedProjectOfficerUserId = Input.AssignedProjectOfficerUserId;
        idea.AssignedHodUserId = Input.AssignedHodUserId;
        idea.Status = Input.Status;

        try
        {
            await _commands.UpdateAsync(
                idea,
                DecodeRowVersion(Input.RowVersion),
                CurrentActor());

            StatusMessage = "Idea updated.";
            return RedirectToPage("Details", new { id = idea.Id });
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
            await LoadUsersAsync();
            return Page();
        }
    }

    private void ValidateCoreFields()
    {
        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError("Input.Title", "Title is required.");
        }

        if (string.IsNullOrWhiteSpace(Input.Status) || !IsEditableStatus(Input.Status))
        {
            ModelState.AddModelError(
                "Input.Status",
                "Select Active or On Hold. Archive is controlled separately by Comdt, HoD or Admin.");
        }
    }

    private async Task LoadUsersAsync()
    {
        ProjectOfficerOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.ProjectOfficer));
        HodOptions = BuildSelectList(await _users.GetUsersInRoleAsync(RoleNames.HoD));
    }

    private static SelectList BuildSelectList(IEnumerable<ApplicationUser> users)
    {
        return new SelectList(
            users.OrderBy(DisplayName).Select(user => new { user.Id, Name = DisplayName(user) }),
            "Id",
            "Name");
    }

    private static string DisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.FullName;

    private ProjectIdeaActorContext CurrentActor()
        => new(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value));

    private static string EncodeRowVersion(byte[]? value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    private static byte[] DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage);
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            return decoded.Length > 0
                ? decoded
                : throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(ProjectIdeaCommandService.RowVersionRequiredMessage, exception);
        }
    }

    private static bool IsEditableStatus(string status)
        => status == ProjectIdeaStatuses.Active || status == ProjectIdeaStatuses.OnHold;
}
