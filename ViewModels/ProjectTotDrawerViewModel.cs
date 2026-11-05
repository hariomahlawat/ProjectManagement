namespace ProjectManagement.ViewModels;

/// <summary>
/// What a page tells the shared ToT drawer (Pages/Projects/_ProjectTotDrawer.cshtml). The drawer
/// loads and saves through the hosting page's handlers (TotDetails, Tot, TotDecide, TotRemark)
/// at <see cref="Endpoint"/>; the details it loads say what the user may do with each project.
/// </summary>
public sealed class ProjectTotDrawerViewModel
{
    /// <summary>The hosting page's path, without a query: "/Projects/Overview/218".</summary>
    public required string Endpoint { get; init; }

    /// <summary>The project shown first; null when the page picks one later.</summary>
    public int? ProjectId { get; init; }

    /// <summary>Render the editor and remark form (shown per project once its details load).</summary>
    public bool CanManage { get; init; }

    /// <summary>Saves go to the HoD for approval; the button reads "Submit for approval".</summary>
    public bool SaveNeedsApproval { get; init; }

    /// <summary>Render the Approve / Reject form (HoD and Admin).</summary>
    public bool CanDecide { get; init; }

    public bool IsApplicable { get; init; } = true;

    /// <summary>A request is waiting: Update details starts hidden.</summary>
    public bool Pending { get; init; }

    public string StatusLabel { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? LatestRemarkBody { get; init; }

    public string? LatestRemarkMeta { get; init; }

    /// <summary>The project page links to the register; the register links to each project.</summary>
    public string? RegisterUrl { get; init; }

    public bool LinkToProject { get; init; }

    public string HandlerUrl(string handler)
        => ProjectId is { } id ? $"{Endpoint}?handler={handler}&id={id}" : $"{Endpoint}?handler={handler}";
}
