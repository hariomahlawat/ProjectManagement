using Microsoft.AspNetCore.Html;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.Admin.Models;

/// <summary>
/// The one Add and Edit form of every reference list (docs/ux/admin-review-4-fixes.md, PR 5),
/// rendered by <c>Areas/Admin/Pages/Shared/_AdminListForm.cshtml</c>. Every list adds and edits
/// on a page of its own, never in a dialog: the heading says "Add …" or "Edit …", one plain
/// sentence says what the list is for and where it is offered, and the button says "Add …" or
/// "Save". Each page passes only its fields, so its form field names, handlers and validation
/// stay its own.
/// </summary>
public sealed class AdminListFormModel
{
    /// <summary>The list's key (<see cref="ReferenceListKeys"/>).</summary>
    public required string ListKey { get; init; }

    /// <summary>The item in a sentence: "project type", "CFA".</summary>
    public required string Thing { get; init; }

    /// <summary>What the list is for and where it is offered, in one plain sentence.</summary>
    public required string Purpose { get; init; }

    public required string Icon { get; init; }

    /// <summary>"Back to project types".</summary>
    public required string BackText { get; init; }

    /// <summary>The list page, for Back and Cancel.</summary>
    public required string BackHref { get; init; }

    public bool IsEdit { get; init; }

    /// <summary>The form's fields (and hidden fields), written by the page.</summary>
    public required Func<object?, IHtmlContent> Fields { get; init; }

    public string Heading => (IsEdit ? "Edit " : "Add ") + Thing;

    public string SubmitText => IsEdit ? "Save" : "Add " + Thing;

    /// <summary>The form of one list, with that list's words.</summary>
    public static AdminListFormModel For(
        ReferenceListDefinition list,
        bool isEdit,
        string icon,
        string? backHref,
        Func<object?, IHtmlContent> fields) => new()
    {
        ListKey = list.Key,
        Thing = list.Label,
        Purpose = AdminListFormWords.Purpose(list.Key),
        Icon = icon,
        BackText = "Back to " + list.PluralInSentence,
        BackHref = backHref ?? "#",
        IsEdit = isEdit,
        Fields = fields
    };
}

/// <summary>The plain words of the list forms, one place for all 13 lists.</summary>
public static class AdminListFormWords
{
    private static readonly IReadOnlyDictionary<string, string> Purposes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ReferenceListKeys.ProjectTypes] = "The kind of project, offered in the project form.",
        [ReferenceListKeys.SponsoringUnits] = "The unit that sponsors a project, offered in the project form.",
        [ReferenceListKeys.LineDirectorates] = "The line directorate a project belongs to, offered in the project form.",
        [ReferenceListKeys.ProjectCategories] = "A group of projects for reports, offered in the project form.",
        [ReferenceListKeys.TechnicalCategories] = "The technology area of a project, offered in the project form.",
        [ReferenceListKeys.ActivityTypes] = "The kind of activity, offered in the activity form.",
        [ReferenceListKeys.ArppCfa] = "A CFA offered when people enter ARPP data.",
        [ReferenceListKeys.ArppFunds] = "A fund offered when people enter ARPP data.",
        [ReferenceListKeys.ArppDfpdsSchedules] = "A DFPDS schedule offered when people enter ARPP data.",
        [ReferenceListKeys.DocumentTypes] = "The kind of document, offered when people add a document to the repository.",
        [ReferenceListKeys.DocumentOffices] = "The office a document comes from, offered when people add a document to the repository.",
        [ReferenceListKeys.SocialMediaEventTypes] = "A kind of event a social media post can be about, offered in the social media form.",
        [ReferenceListKeys.SocialMediaPlatforms] = "Where a social media post was published, such as X or Facebook, offered in the social media form."
    };

    /// <summary>What the list is for and where it is offered.</summary>
    public static string Purpose(string listKey) =>
        Purposes.TryGetValue(listKey, out var text)
            ? text
            : throw new ArgumentOutOfRangeException(nameof(listKey), listKey, "This list has no form words.");

    /// <summary>The hint under the name on an Edit form: what a rename changes.</summary>
    public static string RenameHint(string listKey) => listKey switch
    {
        ReferenceListKeys.ArppCfa or ReferenceListKeys.ArppFunds or ReferenceListKeys.ArppDfpdsSchedules =>
            "ARPP entries already saved keep the wording they were saved with.",
        _ => "Records that already use it show the new name."
    };

    /// <summary>The hint beside Active.</summary>
    public const string ActiveHint = "Offered in new forms. Records that already use it keep it either way.";

    /// <summary>The hint under a description field.</summary>
    public const string DescriptionHint = "Optional. Helps people pick the right one.";
}
