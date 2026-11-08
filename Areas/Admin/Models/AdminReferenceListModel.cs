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

    /// <summary>The area of the list's pages (Admin, or the module that owns them).</summary>
    public string Area { get; init; } = "Admin";

    public string IndexPage { get; init; } = string.Empty;
    public string CreatePage { get; init; } = string.Empty;
    public string EditPage { get; init; } = string.Empty;
    public string DeactivatePage { get; init; } = string.Empty;

    /// <summary>The Index page handler that moves an item up or down; null hides the arrows.</summary>
    public string? MoveHandler { get; init; } = "Move";

    /// <summary>The Index page handler that deletes an unused item; null hides Delete.</summary>
    public string? DeleteHandler { get; init; } = "Delete";

    /// <summary>False for a list with no stored order: no Order column.</summary>
    public bool ShowOrder { get; init; } = true;

    /// <summary>
    /// The Index page handler that deactivates or reactivates an item in place (it gets id,
    /// isActive and rowVersion). Null links to the Deactivate page instead.
    /// </summary>
    public string? ToggleHandler { get; init; }

    /// <summary>Offers "Add sub-category" (the Create page with parentId) on each row of a tree list.</summary>
    public bool OffersAddChild { get; init; }

    /// <summary>Edit opens this dialog instead of the Edit page (the ARPP values).</summary>
    public AdminReferenceListEditDialog? EditDialog { get; init; }

    /// <summary>A line under the table's heading, such as how the list is ordered.</summary>
    public string? Note { get; init; }

    /// <summary>Route values every link and form on the list keeps, such as which ARPP list is open.</summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// A dialog that adds and edits items in place (the ARPP values, arpp-reference-data.js). Edit
/// carries data-reference-edit with the row's values (data-id, data-value, data-description,
/// data-sort-order, data-row-version) and the list's data-kind and data-kind-label.
/// </summary>
public sealed record AdminReferenceListEditDialog(string Target, string Kind, string KindLabel);
