using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;

namespace ProjectManagement.Pages.Chat;

/// <summary>
/// Chat quick guide: a one-page, printable introduction linked from the What's new card and the
/// first-open tips in the contact list. The numbers come from the live chat settings.
/// </summary>
[Authorize]
public sealed class GuideModel : PageModel
{
    private readonly IOptionsMonitor<ChatOptions> _options;

    public GuideModel(IOptionsMonitor<ChatOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ChatOptions Options { get; private set; } = new();

    public IActionResult OnGet()
    {
        Options = _options.CurrentValue;
        return Options.Enabled ? Page() : NotFound();
    }
}
