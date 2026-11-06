using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ProjectManagement.Infrastructure.Ui;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Server toasts with an action (Undo): docs/ux/toasts.md. The action is carried in ToastJson
/// and rendered by Views/Shared/_Toasts.cshtml as a form posting to a local page handler.
/// </summary>
public sealed class ToastActionTests
{
    [Fact]
    public void ToastWithAction_StoresTheActionInToastJson()
    {
        var tempData = NewTempData();

        tempData.ToastWithAction("success", "Idea archived.", new ToastAction(
            "Undo", "/ProjectIdeas/Details/7?handler=Restore", new Dictionary<string, string> { ["rowVersion"] = "AAEC" }));

        using var json = JsonDocument.Parse((string)tempData[TempDataToastExtensions.ToastJsonKey]!);
        var toast = json.RootElement[0];
        Assert.Equal("success", toast.GetProperty("Level").GetString());
        Assert.Equal("Idea archived.", toast.GetProperty("Text").GetString());
        var action = toast.GetProperty("Action");
        Assert.Equal("Undo", action.GetProperty("Label").GetString());
        Assert.Equal("/ProjectIdeas/Details/7?handler=Restore", action.GetProperty("PostUrl").GetString());
        Assert.Equal("AAEC", action.GetProperty("Fields").GetProperty("rowVersion").GetString());
    }

    [Fact]
    public void ToastWithAction_AddsToToastsAlreadyQueued()
    {
        var tempData = NewTempData();
        tempData.ToastMany(("info", "Plan saved."));

        tempData.ToastWithAction("success", "Idea archived.", new ToastAction("Undo", "/x", new Dictionary<string, string>()));

        using var json = JsonDocument.Parse((string)tempData[TempDataToastExtensions.ToastJsonKey]!);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("Plan saved.", json.RootElement[0].GetProperty("Text").GetString());
    }

    [Fact]
    public void ToastsPartial_PostsActionsOnlyToLocalUrlsWithTheAntiforgeryToken()
    {
        var partial = ReadRepoFile("Views", "Shared", "_Toasts.cshtml");

        Assert.Contains("Url.IsLocalUrl(candidate.PostUrl)", partial, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", partial, StringComparison.Ordinal);
        Assert.Contains("<form method=\"post\" action=\"@action.PostUrl\"", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectIdeas_OffersUndoAfterArchiveAndDelete()
    {
        var page = ReadRepoFile("Pages", "ProjectIdeas", "Details.cshtml.cs");

        Assert.Contains("Url.Page(\"/ProjectIdeas/Details\", \"Restore\", new { id })", page, StringComparison.Ordinal);
        Assert.Contains("Url.Page(\"/ProjectIdeas/Deleted\", \"Restore\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BriefingStudio_OffersUndoAfterDeckDelete()
    {
        var page = ReadRepoFile("Pages", "Workspace", "BriefingDecks", "Index.cshtml.cs");

        // Undo posts to the same Restore handler as Recently deleted, with the same field.
        Assert.Contains("Url.Page(\"/Workspace/BriefingDecks/Index\", \"Restore\", new { deckId = (long?)null })", page, StringComparison.Ordinal);
        Assert.Contains("[\"deckId\"] = deckId.ToString(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusMessage = $\"Briefing deck deleted.", page, StringComparison.Ordinal);

        // Deleting from the Studio (/Workspace/BriefingDecks/{deckId}) must land on the library,
        // not on the deleted deck's URL ("That deck was not found").
        var delete = page[page.IndexOf("OnPostDeleteAsync(", StringComparison.Ordinal)..page.IndexOf("OnPostSaveSettingsAsync(", StringComparison.Ordinal)];
        Assert.DoesNotContain("return RedirectToPage();", delete, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(new { deckId = (long?)null })", page, StringComparison.Ordinal);
    }

    private static TempDataDictionary NewTempData() => new(new DefaultHttpContext(), new NullTempDataProvider());

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
        {
            current = current.Parent;
        }

        if (current is null) throw new DirectoryNotFoundException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
    }
}
