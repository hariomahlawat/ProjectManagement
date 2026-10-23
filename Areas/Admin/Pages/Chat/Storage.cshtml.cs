using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Features.Chat;
using ProjectManagement.Hubs;
using ProjectManagement.Services.Chat;

namespace ProjectManagement.Areas.Admin.Pages.Chat;

/// <summary>
/// Chat storage (docs/chat-attachments.md): how much chat files use, who uses most, and clean-ups
/// of everyone's files, one person's, or those older than an age. Sizes and people only:
/// administrators never see file names or conversations. A clean-up is previewed, confirmed by
/// typing REMOVE, audited, told to the people affected, and can be undone for a day.
/// </summary>
[Authorize(Policy = AdminPolicies.RecoveryManage)]
[ResponseCache(NoStore = true)]
public sealed class StorageModel : PageModel
{
    public const string ConfirmationWord = "REMOVE";

    private readonly ChatStorageService _storage;
    private readonly ChatAttachmentService _attachments;
    private readonly IHubContext<ChatHub, IChatClient> _hub;
    private readonly IOptionsMonitor<ChatOptions> _options;

    public StorageModel(ChatStorageService storage, ChatAttachmentService attachments, IHubContext<ChatHub, IChatClient> hub, IOptionsMonitor<ChatOptions> options)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ChatAttachmentTotalsDto Totals { get; private set; } = new(0, 0, null, Array.Empty<ChatAttachmentPersonUsageDto>());

    public IReadOnlyList<ChatStoragePersonDto> People { get; private set; } = Array.Empty<ChatStoragePersonDto>();

    public IReadOnlyList<ChatStorageBatchDto> Batches { get; private set; } = Array.Empty<ChatStorageBatchDto>();

    public ChatAttachmentOptions Settings => _options.CurrentValue.Attachments;

    /// <summary>Set when a clean-up is being previewed (GET with preview=true).</summary>
    public ChatStoragePreviewDto? Preview { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? OlderThanDays { get; set; }

    [BindProperty(SupportsGet = true, Name = "preview")]
    public bool ShowPreview { get; set; }

    [BindProperty]
    public string? Confirmation { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string ScopeLabel
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(UserId)
                ? "everyone's chat files"
                : $"chat files sent by {People.FirstOrDefault(p => p.UserId == UserId)?.DisplayName ?? "this person"}";
            return OlderThanDays is > 0 ? $"{who} older than {OlderThanDays} days" : who;
        }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (ShowPreview)
        {
            Preview = await _storage.PreviewAsync(Scope, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(Confirmation?.Trim(), ConfirmationWord, StringComparison.Ordinal))
        {
            ErrorMessage = $"Nothing was removed: type {ConfirmationWord} to confirm.";
            return RedirectToPage(new { userId = UserId, olderThanDays = OlderThanDays, preview = true });
        }

        var result = await _storage.RemoveAsAdministratorAsync(CurrentUserId, User.Identity?.Name, Scope, cancellationToken);
        if (result.Change.Error is not null)
        {
            ErrorMessage = result.Change.Error;
            return RedirectToPage();
        }

        await ChatApi.PushFileChangesAsync(_hub, result.Updates);
        StatusMessage = $"Removed {result.Change.Files:N0} file(s), {Size(result.Change.Bytes)}. The people affected have been told. You can undo this until {Local(result.Change.RestorableUntil!.Value)}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUndoAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var result = await _storage.UndoBatchAsync(CurrentUserId, User.Identity?.Name, batchId, cancellationToken);
        if (result.Change.Error is not null)
        {
            ErrorMessage = result.Change.Error;
            return RedirectToPage();
        }

        await ChatApi.PushFileChangesAsync(_hub, result.Updates);
        StatusMessage = $"Restored {result.Change.Files:N0} file(s), {Size(result.Change.Bytes)}.";
        return RedirectToPage();
    }

    private ChatStorageScope Scope => new(string.IsNullOrWhiteSpace(UserId) ? null : UserId, OlderThanDays is > 0 ? OlderThanDays : null);

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Totals = await _attachments.GetTotalsAsync(cancellationToken);
        People = await _storage.ListPeopleAsync(cancellationToken);
        Batches = await _storage.ListBatchesAsync(cancellationToken);
    }

    public static string Size(long bytes) => ChatAttachmentService.FormatSize(bytes);

    public static string Local(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTime(utc, ProjectManagement.Utilities.TimeZoneHelper.GetIst()).ToString("d MMM yyyy, HH:mm");
}
