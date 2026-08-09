using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Controllers.Api;

/// <summary>
/// Personal Notebook presentation preferences for live PRISM-owned surfaces.
/// The endpoint never mutates Conference Review or any source-domain content.
/// </summary>
[Authorize(Roles = RoleNames.Comdt + "," + RoleNames.HoD)]
[ApiController]
[AutoValidateAntiforgeryToken]
[TypeFilter(typeof(NotebookApiExceptionFilter))]
[Route("api/notebook/system-items")]
public sealed class NotebookSystemItemsController : ControllerBase
{
    private readonly INotebookSystemItemPreferenceService _preferences;
    private readonly INotebookService _notebook;
    private readonly UserManager<ApplicationUser> _users;

    public NotebookSystemItemsController(
        INotebookSystemItemPreferenceService preferences,
        INotebookService notebook,
        UserManager<ApplicationUser> users)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _notebook = notebook ?? throw new ArgumentNullException(nameof(notebook));
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        var preference = await _preferences.GetAsync(CurrentUserId(), key, ct);
        return Ok(new { preference });
    }

    [Consumes("application/json")]
    [HttpPatch("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateNotebookSystemItemRequest request, CancellationToken ct)
    {
        request ??= new UpdateNotebookSystemItemRequest();
        var preference = await _preferences.UpdateAsync(
            CurrentUserId(),
            key,
            new NotebookSystemItemPreferencePatch
            {
                ShowInHome = request.ShowInHome,
                IsPinned = request.IsPinned,
                ColorKey = request.ColorKey,
                Labels = request.Labels
            },
            ct);

        var labels = request.Labels is null
            ? null
            : await _notebook.GetLabelsAsync(CurrentUserId(), ct);

        return Ok(new { preference, labels });
    }

    [Consumes("application/json")]
    [HttpPut("{key}/placement")]
    public async Task<IActionResult> Placement(string key, [FromBody] SetNotebookSystemItemPlacementRequest request, CancellationToken ct)
    {
        if (request.Position < 0)
        {
            return BadRequest(new
            {
                code = "notebook_validation_failed",
                message = "The system note position is invalid.",
                errors = new Dictionary<string, string[]> { ["position"] = ["Position cannot be negative."] }
            });
        }

        var preference = await _preferences.SetPlacementAsync(
            CurrentUserId(), key, request.IsPinned, request.Position, ct);
        return Ok(new { preference });
    }

    private string CurrentUserId()
        => _users.GetUserId(User) ?? throw new UnauthorizedAccessException();
}

public sealed class UpdateNotebookSystemItemRequest
{
    public bool? ShowInHome { get; set; }
    public bool? IsPinned { get; set; }
    public string? ColorKey { get; set; }
    public IReadOnlyList<string>? Labels { get; set; }
}

public sealed class SetNotebookSystemItemPlacementRequest
{
    public bool IsPinned { get; set; }
    public int Position { get; set; }
}
