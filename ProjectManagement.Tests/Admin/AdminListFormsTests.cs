using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Areas.Admin.Pages.MasterData.ArppReferences;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// One Add and Edit form for every list (docs/ux/admin-review-4-fixes.md, PR 5, U1 and U2): every
/// list adds and edits on a page of its own through the shared list form, never in a dialog, in
/// plain words, with "Add …" and "Save".
/// </summary>
public sealed class AdminListFormsTests
{
    private const string SharedForm = "_AdminListForm.cshtml";

    // Each list's Add and Edit page, and the list partial it goes through (if any).
    public static IEnumerable<object[]> ListPages() => new[]
    {
        new object[] { ReferenceListKeys.ProjectTypes, "Areas/Admin/Pages/Lookups/ProjectTypes/Create.cshtml", "Areas/Admin/Pages/Lookups/ProjectTypes/Edit.cshtml", "Areas/Admin/Pages/Lookups/_LookupForm.cshtml" },
        new object[] { ReferenceListKeys.SponsoringUnits, "Areas/Admin/Pages/Lookups/SponsoringUnits/Create.cshtml", "Areas/Admin/Pages/Lookups/SponsoringUnits/Edit.cshtml", "Areas/Admin/Pages/Lookups/_LookupForm.cshtml" },
        new object[] { ReferenceListKeys.LineDirectorates, "Areas/Admin/Pages/Lookups/LineDirectorates/Create.cshtml", "Areas/Admin/Pages/Lookups/LineDirectorates/Edit.cshtml", "Areas/Admin/Pages/Lookups/_LookupForm.cshtml" },
        new object[] { ReferenceListKeys.ProjectCategories, "Areas/Admin/Pages/Categories/Create.cshtml", "Areas/Admin/Pages/Categories/Edit.cshtml", "" },
        new object[] { ReferenceListKeys.TechnicalCategories, "Areas/Admin/Pages/TechnicalCategories/Create.cshtml", "Areas/Admin/Pages/TechnicalCategories/Edit.cshtml", "" },
        new object[] { ReferenceListKeys.ActivityTypes, "Areas/Admin/Pages/ActivityTypes/Create.cshtml", "Areas/Admin/Pages/ActivityTypes/Edit.cshtml", "" },
        new object[] { ReferenceListKeys.ArppCfa, "Areas/Admin/Pages/MasterData/ArppReferences/Create.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/Edit.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/_ArppReferenceForm.cshtml" },
        new object[] { ReferenceListKeys.ArppFunds, "Areas/Admin/Pages/MasterData/ArppReferences/Create.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/Edit.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/_ArppReferenceForm.cshtml" },
        new object[] { ReferenceListKeys.ArppDfpdsSchedules, "Areas/Admin/Pages/MasterData/ArppReferences/Create.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/Edit.cshtml", "Areas/Admin/Pages/MasterData/ArppReferences/_ArppReferenceForm.cshtml" },
        new object[] { ReferenceListKeys.DocumentTypes, "Areas/DocumentRepository/Pages/Admin/DocumentCategories/Create.cshtml", "Areas/DocumentRepository/Pages/Admin/DocumentCategories/Edit.cshtml", "Areas/DocumentRepository/Pages/Admin/_DocRepoListForm.cshtml" },
        new object[] { ReferenceListKeys.DocumentOffices, "Areas/DocumentRepository/Pages/Admin/OfficeCategories/Create.cshtml", "Areas/DocumentRepository/Pages/Admin/OfficeCategories/Edit.cshtml", "Areas/DocumentRepository/Pages/Admin/_DocRepoListForm.cshtml" },
        new object[] { ReferenceListKeys.SocialMediaEventTypes, "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/New.cshtml", "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Edit.cshtml", "" },
        new object[] { ReferenceListKeys.SocialMediaPlatforms, "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Platforms/New.cshtml", "Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Platforms/Edit.cshtml", "" }
    };

    [Fact]
    public void EveryListInTheCatalogue_IsCovered()
    {
        Assert.Equal(
            ReferenceListCatalog.All.Select(list => list.Key).OrderBy(key => key),
            ListPages().Select(row => (string)row[0]).OrderBy(key => key));
    }

    [Theory]
    [MemberData(nameof(ListPages))]
    public void EveryList_AddsAndEdits_OnTheSharedForm(string key, string create, string edit, string listPartial)
    {
        _ = key;
        foreach (var page in new[] { create, edit })
        {
            var text = Read(page);
            Assert.StartsWith("@page", text);
            var form = string.IsNullOrEmpty(listPartial) ? text : Read(listPartial);
            if (!string.IsNullOrEmpty(listPartial))
            {
                Assert.Contains(Path.GetFileNameWithoutExtension(listPartial), text, StringComparison.Ordinal);
            }

            Assert.Contains($"~/Areas/Admin/Pages/Shared/{SharedForm}", form, StringComparison.Ordinal);
            Assert.Contains("AdminListFormModel.For(", form, StringComparison.Ordinal);
            // The shared form writes the heading, the buttons and the layout; the page only its fields.
            Assert.DoesNotContain("<footer", form, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-form-section__header", form, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-guidance-card", form, StringComparison.Ordinal);
        }

        Assert.StartsWith("@page \"{id:", Read(edit));
    }

    [Fact]
    public void NoListAddsOrEditsInADialog()
    {
        foreach (var page in new[]
        {
            "Areas/DocumentRepository/Pages/Admin/_DocRepoListPage.cshtml",
            "Areas/Admin/Pages/MasterData/ArppReferences/Index.cshtml",
            "Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml"
        })
        {
            var text = Read(page);
            Assert.DoesNotContain("class=\"modal fade\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("data-reference-edit", text, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "wwwroot/js/admin/admin-reference-edit-dialog.js")));
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "wwwroot/js/admin/arpp-reference-data.js")));
        Assert.Null(typeof(AdminReferenceListModel).GetProperty("EditDialog"));
        // The list's Edit link keeps the list's route values (?list= on the ARPP values).
        Assert.Contains("asp-page=\"@Model.EditPage\" asp-all-route-data=\"keep\"", Read("Areas/Admin/Pages/Shared/_AdminReferenceList.cshtml"), StringComparison.Ordinal);
        Assert.Contains("asp-page=\"./Create\" asp-route-list=\"@current.Key\"", Read("Areas/Admin/Pages/MasterData/ArppReferences/Index.cshtml"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryList_HasOnePlainSentence_AndTheButtonsSayAddAndSave()
    {
        foreach (var list in ReferenceListCatalog.All)
        {
            var purpose = AdminListFormWords.Purpose(list.Key);
            Assert.EndsWith(".", purpose);
            Assert.Equal(1, purpose.Count(character => character == '.'));
            Assert.InRange(purpose.Length, 20, 120);
            Assert.True(char.IsUpper(purpose[0]), purpose);

            var add = AdminListFormModel.For(list, isEdit: false, "bi-list", "/list", _ => HtmlString.Empty);
            var edit = AdminListFormModel.For(list, isEdit: true, "bi-list", "/list", _ => HtmlString.Empty);
            Assert.Equal("Add " + list.Label, add.Heading);
            Assert.Equal("Add " + list.Label, add.SubmitText);
            Assert.Equal("Edit " + list.Label, edit.Heading);
            Assert.Equal("Save", edit.SubmitText);
            Assert.Equal("Back to " + list.PluralInSentence, add.BackText);
        }
    }

    [Fact]
    public void ListPagesHeaderButton_SaysAddAndTheItem()
    {
        var root = FindRepositoryRoot();
        var presentations = Directory.EnumerateFiles(Path.Combine(root, "Areas"), "Index.cshtml.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .SelectMany(text => Regex.Matches(text, @"AdminReferenceListPresentation Shown = new\(\s*AdminNavigationKeys\.\w+, ""[^""]+"", ""(?<add>[^""]+)""").Select(match => match.Groups["add"].Value))
            .ToArray();

        Assert.Equal(11, presentations.Length);
        Assert.DoesNotContain("Add category", presentations);
        Assert.All(presentations, text => Assert.StartsWith("Add ", text));
    }

    [Fact]
    public void TheActiveSwitch_KeepsItsLabelApartFromItsHint()
    {
        var css = Read("wwwroot/css/admin.css");
        Assert.Matches(@"\.admin-switch-row \{[^}]*display: flex;[^}]*gap:", css);
        Assert.Matches(@"\.admin-switch-row > span \{[^}]*display: grid;", css);
    }

    // SECTION: the repository lists' new pages

    [Fact]
    public async Task RepositoryAdd_PostsToTheListOfItsFolder()
    {
        var lists = new RecordingDocRepoLists();
        var page = DocRepoPage(lists, "/Admin/OfficeCategories/Create");
        page.Input = new DocRepoListItemModel.InputModel { Name = "Plans branch" };

        var result = await page.OnPostAsync(null, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal((DocRepoListKind.Office, "Plans branch", (int?)null), lists.LastCreate);
        Assert.Equal("Done.", page.TempData[FlashMessageKeys.AdminMasterDataSuccess]);
    }

    [Fact]
    public async Task RepositoryEdit_SendsTheVersionItShowed()
    {
        var lists = new RecordingDocRepoLists();
        var page = DocRepoPage(lists, "/Admin/DocumentCategories/Edit");
        page.Input = new DocRepoListItemModel.InputModel { Name = "Minutes", SortOrder = 4, RowVersion = Convert.ToBase64String(BitConverter.GetBytes(7u)) };

        var result = await page.OnPostAsync(3, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal((DocRepoListKind.DocumentType, 3, "Minutes", 4, (uint?)7u), lists.LastUpdate);
    }

    [Fact]
    public async Task RepositoryEdit_WithoutAnOrder_IsRefusedOnThePage()
    {
        var lists = new RecordingDocRepoLists();
        var page = DocRepoPage(lists, "/Admin/DocumentCategories/Edit");
        page.Input = new DocRepoListItemModel.InputModel { Name = "Minutes" };

        var result = await page.OnPostAsync(3, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Null(lists.LastUpdate);
        Assert.True(page.ModelState.ContainsKey("Input.SortOrder"));
    }

    [Fact]
    public async Task RepositoryAdd_ADuplicateName_StaysOnThePageUnderTheName()
    {
        var lists = new RecordingDocRepoLists { Next = DocRepoListResult.Failure("Another office already uses this name.", "DuplicateName") };
        var page = DocRepoPage(lists, "/Admin/OfficeCategories/Create");
        page.Input = new DocRepoListItemModel.InputModel { Name = "North" };

        var result = await page.OnPostAsync(null, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Another office already uses this name.", page.ModelState["Input.Name"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task RepositoryEdit_OutOfDate_ReturnsToTheListWithTheMessage()
    {
        var lists = new RecordingDocRepoLists { Next = DocRepoListResult.Failure("Someone else changed this entry.", "ConcurrencyConflict") };
        var page = DocRepoPage(lists, "/Admin/OfficeCategories/Edit");
        page.Input = new DocRepoListItemModel.InputModel { Name = "North", SortOrder = 1, RowVersion = Convert.ToBase64String(BitConverter.GetBytes(2u)) };

        var result = await page.OnPostAsync(5, CancellationToken.None);

        Assert.Equal("./Index", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Equal("Someone else changed this entry.", page.TempData[FlashMessageKeys.AdminMasterDataError]);
    }

    [Fact]
    public async Task RepositoryPages_OutsideTheirFolders_AreNotFound()
    {
        var page = DocRepoPage(new RecordingDocRepoLists(), "/Admin/Trash/Create");

        Assert.IsType<NotFoundResult>(await page.OnPostAsync(null, CancellationToken.None));
    }

    [Fact]
    public void NewPages_KeepTheListPolicies()
    {
        Assert.Equal(Policies.Documents.ManageCategories, PolicyOf(typeof(DocRepoListItemModel)));
        Assert.Equal(AdminPolicies.MasterDataManage, PolicyOf(typeof(ArppReferenceItemModel)));
    }

    // SECTION: the ARPP values' new pages

    [Fact]
    public async Task ArppAdd_SavesIntoTheListOfThePage_WithoutADescriptionOutsideDfpds()
    {
        var data = new RecordingArppData();
        var page = ArppPage(data);
        page.List = "funds";
        page.Input = new IndexModel.ReferenceInputModel { Kind = ArppReferenceDataKind.Cfa, Id = 99, Value = "Capital", Description = "ignored", SortOrder = 3 };

        var result = await page.OnPostAsync(null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("funds", redirect.RouteValues!["list"]);
        Assert.Equal(ArppReferenceDataKind.Fund, data.Last!.Kind);
        Assert.Null(data.Last.Id);
        Assert.Null(data.Last.Description);
        Assert.Equal(3, data.Last.SortOrder);
    }

    [Fact]
    public async Task ArppEdit_SendsTheIdOfThePage_AndShowsFieldErrorsOnTheField()
    {
        var data = new RecordingArppData
        {
            Next = ArppReferenceDataCommandResult.Failed("Check the value.", new Dictionary<string, IReadOnlyList<string>> { ["Value"] = new[] { "That schedule already exists." } })
        };
        var page = ArppPage(data);
        page.List = "dfpds";
        page.Input = new IndexModel.ReferenceInputModel { Value = "7A", Description = "Works", SortOrder = 1, RowVersion = "AAAA" };

        var result = await page.OnPostAsync(12, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(12, data.Last!.Id);
        Assert.Equal(ArppReferenceDataKind.DfpdsSchedule, data.Last.Kind);
        Assert.Equal("Works", data.Last.Description);
        Assert.Equal("That schedule already exists.", page.ModelState["Input.Value"]!.Errors.Single().ErrorMessage);
        Assert.Equal("Schedule number", page.ValueLabel);
    }

    // SECTION: helpers

    private static DocRepoListItemModel DocRepoPage(IDocRepoListService lists, string viewEnginePath)
    {
        var page = new DocRepoListItemModel(lists, new UnusedReferenceLists());
        Configure(page, viewEnginePath);
        return page;
    }

    private static ArppReferenceItemModel ArppPage(IArppReferenceDataService data)
    {
        var page = new ArppReferenceItemModel(data, new UnusedReferenceLists());
        Configure(page, "/MasterData/ArppReferences/Create");
        return page;
    }

    private static void Configure(PageModel page, string viewEnginePath)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "admin") }, "test"))
        };
        var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = viewEnginePath, RelativePath = $"/Areas/X/Pages{viewEnginePath}.cshtml" };
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(httpContext, new RouteData(), descriptor))
        {
            ActionDescriptor = descriptor,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static string? PolicyOf(Type type) =>
        ((AuthorizeAttribute)Attribute.GetCustomAttribute(type, typeof(AuthorizeAttribute))!).Policy;

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class RecordingDocRepoLists : IDocRepoListService
    {
        public DocRepoListResult Next { get; init; } = DocRepoListResult.Success("Done.");
        public (DocRepoListKind, string?, int?)? LastCreate { get; private set; }
        public (DocRepoListKind, int, string?, int, uint?)? LastUpdate { get; private set; }

        public Task<DocRepoListResult> CreateAsync(DocRepoListKind kind, ClaimsPrincipal user, string? name, int? sortOrder, CancellationToken cancellationToken = default)
        {
            LastCreate = (kind, name, sortOrder);
            return Task.FromResult(Next);
        }

        public Task<DocRepoListResult> UpdateAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, string? name, int sortOrder, uint? version, CancellationToken cancellationToken = default)
        {
            LastUpdate = (kind, id, name, sortOrder, version);
            return Task.FromResult(Next);
        }

        public Task<DocRepoListResult> SetActiveAsync(DocRepoListKind kind, ClaimsPrincipal user, int id, bool? isActive, uint? version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingArppData : IArppReferenceDataService
    {
        public ArppReferenceDataCommandResult Next { get; init; } = ArppReferenceDataCommandResult.Succeeded("Saved.");
        public ArppReferenceDataSaveCommand? Last { get; private set; }

        public Task<ArppReferenceDataCommandResult> SaveAsync(ArppReferenceDataSaveCommand command, CancellationToken cancellationToken = default)
        {
            Last = command;
            return Task.FromResult(Next);
        }

        public Task<ArppReferenceDataSet> GetWorkspaceOptionsAsync(IReadOnlyCollection<int> selectedCfaIds, IReadOnlyCollection<int> selectedFundIds, IReadOnlyCollection<int> selectedDfpdsIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArppReferenceDataAdminSnapshot> GetAdminSnapshotAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArppReferenceDataCommandResult> SetActiveAsync(ArppReferenceDataActivationCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // The posts above never read an item; Edit's GET reads it through the shared list service.
    private sealed class UnusedReferenceLists : IReferenceListService
    {
        public Task<ReferenceListPage> GetPageAsync(string key, ReferenceListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReferenceListTiles> GetTilesAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReferenceListUsage>> GetUsageAsync(string key, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReferenceListRow?> GetItemAsync(string key, int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminOperationResult> DeleteAsync(string key, int id, byte[] rowVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminOperationResult> DeleteAsync(string key, Guid id, byte[] rowVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _data = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = new Dictionary<string, object>(values);
    }
}
