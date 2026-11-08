using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.Admin.Models;

/// <summary>
/// The shared list page for reference lists (_AdminReferenceList.cshtml): tiles All, In use
/// and Inactive; one table of Name, [extra columns], Order, Used by, Status and Actions.
/// </summary>
public sealed record AdminReferenceListModel
{
    public ReferenceListPage Page { get; init; } = ReferenceListPage.Empty(string.Empty);

    /// <summary>The item in lower case, for sentences: "project type".</summary>
    public string SingularLabel { get; init; } = "item";

    /// <summary>The list's name: "Project types".</summary>
    public string PluralLabel { get; init; } = "Items";

    /// <summary>What uses the items, for the In use tile: "projects".</summary>
    public string UsedByLabel { get; init; } = "records";

    public string Icon { get; init; } = "bi-tags";

    public IReadOnlyList<ReferenceListExtraColumn> ExtraColumns { get; init; } = Array.Empty<ReferenceListExtraColumn>();

    public string IndexPage { get; init; } = string.Empty;
    public string CreatePage { get; init; } = string.Empty;
    public string EditPage { get; init; } = string.Empty;
    public string DeactivatePage { get; init; } = string.Empty;

    /// <summary>The Index page handler that moves an item up or down; null hides the arrows.</summary>
    public string? MoveHandler { get; init; } = "Move";

    /// <summary>The Index page handler that deletes an unused item; null hides Delete.</summary>
    public string? DeleteHandler { get; init; } = "Delete";
}
