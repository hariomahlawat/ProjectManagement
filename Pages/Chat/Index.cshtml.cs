using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;

namespace ProjectManagement.Pages.Chat;

/// <summary>
/// Full-page chat ("Messages"): every conversation, message search, and a large conversation
/// view. All data comes from /api/chat through the chat scripts (wwwroot/js/chat/); this page only renders the shell.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IOptionsMonitor<ChatOptions> _options;

    public IndexModel(IOptionsMonitor<ChatOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Conversation to open, from ?with= (a user id).</summary>
    [BindProperty(SupportsGet = true, Name = "with")]
    public string? With { get; set; }

    /// <summary>Message to open the conversation at, from ?at= (a search hit).</summary>
    [BindProperty(SupportsGet = true, Name = "at")]
    public long? At { get; set; }

    public IActionResult OnGet()
    {
        if (!_options.CurrentValue.Enabled)
        {
            return NotFound();
        }

        if (With is { Length: > 450 })
        {
            With = null;
        }

        if (At is <= 0)
        {
            At = null;
        }

        return Page();
    }
}
