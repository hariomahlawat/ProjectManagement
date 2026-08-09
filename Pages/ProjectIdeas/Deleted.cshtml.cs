using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services.ProjectIdeas;

namespace ProjectManagement.Pages.ProjectIdeas;

[Authorize]
public sealed class DeletedModel : PageModel
{
    private readonly ProjectIdeaReadService _read;
    private readonly ProjectIdeaCommandService _commands;
    private readonly ProjectIdeaPermissionService _permissions;
    private readonly UserManager<ApplicationUser> _users;

    public DeletedModel(
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

    public IReadOnlyList<ProjectIdeaDeletedItem> Ideas { get; private set; } = Array.Empty<ProjectIdeaDeletedItem>();
    public IReadOnlyDictionary<string, string> DeletedByNames { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_permissions.CanViewDeletedIdeas(User)) return Forbid();

        Ideas = await _read.GetDeletedIdeasAsync(cancellationToken);
        var ids = Ideas
            .Select(idea => idea.DeletedByUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length > 0)
        {
            DeletedByNames = await _users.Users
                .AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Display = user.FullName != null && user.FullName != string.Empty
                        ? user.FullName
                        : user.UserName ?? user.Email ?? user.Id
                })
                .ToDictionaryAsync(item => item.Id, item => item.Display, cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int ideaId, string? rowVersion, CancellationToken cancellationToken)
    {
        if (!_permissions.CanRestoreDeletedIdea(User)) return Forbid();

        try
        {
            var restored = await _commands.RestoreDeletedIdeaAsync(
                ideaId,
                DecodeRowVersion(rowVersion),
                CurrentActor(),
                cancellationToken);
            if (!restored) return NotFound();

            TempData["ToastSuccess"] = "Idea restored.";
            return RedirectToPage("Details", new { id = ideaId });
        }
        catch (InvalidOperationException exception)
        {
            TempData["ToastError"] = exception.Message;
            return RedirectToPage();
        }
    }

    public string DeletedBy(ProjectIdeaDeletedItem idea)
    {
        if (string.IsNullOrWhiteSpace(idea.DeletedByUserId)) return "Unknown user";
        return DeletedByNames.GetValueOrDefault(idea.DeletedByUserId) ?? idea.DeletedByUserId;
    }

    public static string EncodeRowVersion(byte[]? value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    private ProjectIdeaActorContext CurrentActor()
        => new(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value));

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
}
