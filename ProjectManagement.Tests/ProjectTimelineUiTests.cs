using ProjectManagement.Tests.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectTimelineUiTests
{
    private static readonly IServiceProvider Services = BuildServiceProvider();

    [Theory]
    [InlineData(true, false, "2026-08-06")]
    [InlineData(false, false, "")]
    [InlineData(false, true, "2026-08-11")]
    public async Task ReopenStage_OffersExplicitStartAndReset_WithoutInventingDate(bool hasRecordedStart, bool hasReopenSuggestion, string expected)
    {
        var stage = new TimelineItemVm { Code = "BM", Name = "Benchmarking", Status = StageStatus.Completed,
            ActualStart = hasRecordedStart ? new DateOnly(2026, 8, 6) : null,
            PlannedStart = new DateOnly(2026, 8, 1), SuggestedStartDate = new DateOnly(2026, 8, 7),
            ReopenSuggestedStartDate = hasReopenSuggestion ? new DateOnly(2026, 8, 11) : null,
            EarliestAllowedStartDate = new DateOnly(2026, 8, 6) };
        var html = await RenderAsync(new TimelineVm { ProjectId = 1, Items = new[] { stage } }, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("data-reopen-start=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("Reset to not started", html, StringComparison.Ordinal);
        Assert.Contains($"data-default-date=\"{expected}\"", html, StringComparison.Ordinal);
        if (hasRecordedStart) Assert.Contains("data-actual-start=\"2026-08-06\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-default-date=\"2026-08-01\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-default-date=\"2026-08-07\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("Skipped")]
    public async Task NonCompletedReopen_DoesNotExposeCompletedOnlyDefault(string status)
    {
        var stage = new TimelineItemVm { Code = "BM", Name = "Benchmarking", Status = Enum.Parse<StageStatus>(status),
            SuggestedStartDate = new DateOnly(2026, 8, 7), ReopenSuggestedStartDate = new DateOnly(2026, 8, 11) };
        var html = await RenderAsync(new TimelineVm { ProjectId = 1, Items = new[] { stage } }, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("data-default-date=\"\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-default-date=\"2026-08-11\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NotStarted")]
    [InlineData("InProgress")]
    [InlineData("Blocked")]
    [InlineData("Completed")]
    [InlineData("Skipped")]
    public async Task EveryDirectAction_ReceivesServerOperationalToday(string status)
    {
        var stage = new TimelineItemVm { Code = "BM", Name = "Benchmarking", Status = Enum.Parse<StageStatus>(status), Today = new DateOnly(2026, 8, 28) };
        var html = await RenderAsync(new TimelineVm { ProjectId = 1, Items = new[] { stage } }, isHoD: true, isAssignedProjectOfficer: false);
        var triggers = Regex.Matches(html, "<button\\b[^>]*\\bdata-direct-apply(?:\\s|>)[^>]*>", RegexOptions.Singleline);
        Assert.NotEmpty(triggers);
        foreach (Match trigger in triggers)
        {
            Assert.Contains("data-actual-today=\"2026-08-28\"", trigger.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ActionsDropdown_RenderedForHoDOnly()
    {
        var model = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.NotStarted,
                    SortOrder = 1
                }
            }
        };

        var hodHtml = await RenderAsync(model, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("data-direct-apply", hodHtml, StringComparison.Ordinal);

        var otherHtml = await RenderAsync(model, isHoD: false, isAssignedProjectOfficer: false);
        Assert.DoesNotContain("data-direct-apply", otherHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotStartedStage_OffersDirectCompletionWithSuggestedEditableStart()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "BID",
                    Name = "Bidding/Tendering",
                    Status = StageStatus.NotStarted,
                    SortOrder = 1,
                    SuggestedStartDate = new DateOnly(2026, 6, 11),
                    EarliestAllowedStartDate = new DateOnly(2026, 6, 10),
                    SuggestedStartSourceName = "Acceptance of Necessity"
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: false);

        Assert.Contains("Complete stage directly", html, StringComparison.Ordinal);
        Assert.Contains("data-direct-completion=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-default-start-date=\"2026-06-11\"", html, StringComparison.Ordinal);
        Assert.Contains("data-earliest-start=\"2026-06-10\"", html, StringComparison.Ordinal);
        Assert.Contains("data-start-source=\"Acceptance of Necessity\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionsDropdown_FiltersActionsByCurrentStatusAndAuthority()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.InProgress,
                    SortOrder = 1
                }
            }
        };

        var hodHtml = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: false);
        Assert.Contains("Complete stage", hodHtml, StringComparison.Ordinal);
        Assert.Contains("Mark blocked", hodHtml, StringComparison.Ordinal);
        Assert.Contains("Skip stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Start stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Reopen stage", hodHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Update stage", hodHtml, StringComparison.Ordinal);

        var dualRoleHtml = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);
        Assert.DoesNotContain("Update stage", dualRoleHtml, StringComparison.Ordinal);

        var poHtml = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);
        Assert.Contains("Update stage", poHtml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(poHtml, "Update stage"));
        Assert.DoesNotContain("Request change", poHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-direct-apply", poHtml, StringComparison.Ordinal);
    }


    [Fact]
    public async Task CompletedSameDayStage_RendersOneInclusiveCalendarDay()
    {
        var date = new DateOnly(2026, 8, 10);
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "FS",
                    Name = "Feasibility Study",
                    Status = StageStatus.Completed,
                    ActualStart = date,
                    EffectiveActualStart = date,
                    CompletedOn = date,
                    SortOrder = 1
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: false);

        Assert.Contains("1 calendar day", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_RendersAuthorityAndUnknownDuration_WithoutManualActions()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 208,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2026, 2, 26),
                    ActualStart = null,
                    EffectiveActualStart = null,
                    SortOrder = 1,
                    IsArppManaged = true,
                    ArppSourceIssueId = 10,
                    ArppSourceDocumentLabel = "Original ARPP",
                    ArppSourceIssueDate = new DateOnly(2026, 2, 26),
                    ArppSourceSerialNumber = "33"
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);

        Assert.Contains("ARPP-derived", html, StringComparison.Ordinal);
        Assert.Contains("Initial IPA approval", html, StringComparison.Ordinal);
        Assert.Contains("Original ARPP", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.Contains("Not recorded", html, StringComparison.Ordinal);
        Assert.Contains("View ARPP issue", html, StringComparison.Ordinal);
        Assert.Contains("Record actual start", html, StringComparison.Ordinal);
        Assert.Contains("data-actuals-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-direct-apply", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-stage-request-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_WithInvalidStart_ShowsCorrectionWarningAndNoFabricatedDuration()
    {
        var item = new TimelineItemVm
        {
            Code = "IPA",
            Name = "In-Principle Approval",
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 2, 26),
            ActualStart = new DateOnly(2026, 3, 1),
            EffectiveActualStart = new DateOnly(2026, 3, 1),
            SortOrder = 1,
            IsArppManaged = true,
            ArppSourceIssueId = 10,
            ArppSourceDocumentLabel = "Original ARPP",
            ArppSourceIssueDate = new DateOnly(2026, 2, 26)
        };
        var timeline = new TimelineVm
        {
            ProjectId = 208,
            Items = new[] { item }
        };

        Assert.Null(item.ActualDurationDays);

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: true);

        Assert.Contains("Actual start needs correction", html, StringComparison.Ordinal);
        Assert.Contains("Correct actual start", html, StringComparison.Ordinal);
        Assert.Contains("Use Correct actual start to amend or clear it.", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">1 day<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppManagedIpa_OnTerminalLegacyProject_UsesHistoricalStartCorrection()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 182,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "IPA",
                    Name = "In-Principle Approval",
                    Status = StageStatus.Completed,
                    CompletedOn = new DateOnly(2026, 2, 26),
                    ActualStart = null,
                    EffectiveActualStart = null,
                    SortOrder = 1,
                    IsArppManaged = true,
                    ArppSourceIssueId = 10,
                    ArppSourceDocumentLabel = "Original ARPP",
                    ArppSourceIssueDate = new DateOnly(2026, 2, 26)
                }
            }
        };

        var html = await RenderAsync(
            timeline,
            isHoD: true,
            isAssignedProjectOfficer: false,
            lifecycleStatus: ProjectLifecycleStatus.Cancelled,
            isLegacy: true,
            canManageHistoricalStageHistory: true);

        Assert.Contains("Record actual start", html, StringComparison.Ordinal);
        Assert.Contains("data-historical-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-actuals-focus-stage=\"IPA\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingProjectOfficerUpdate_IsImmediatelyVisibleAsAwaitingApproval()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "DEVP",
                    Name = "Development",
                    Status = StageStatus.InProgress,
                    SortOrder = 1,
                    HasPendingRequest = true,
                    PendingStatus = StageStatus.Completed.ToString(),
                    PendingDate = new DateOnly(2026, 6, 26),
                    PendingNote = "Development completed.",
                    PendingRequestedBy = "Project Officer",
                    PendingRequestedOn = new DateTimeOffset(2026, 6, 26, 8, 30, 0, TimeSpan.Zero)
                }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        Assert.Contains("Awaiting HoD approval", html, StringComparison.Ordinal);
        Assert.Contains("Completion submitted", html, StringComparison.Ordinal);
        Assert.Contains("Proposed completion", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned completion missing", html, StringComparison.Ordinal);
        Assert.Contains("Edit submission", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "Edit submission"));
    }


    // SECTION: PO Timeline Increment 1e (lead with the stage that needs action)
    [Fact]
    public async Task SettledStages_CollapseIntoOneGroupAboveTheStagesThatNeedAction()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, CompletedOn = new DateOnly(2026, 1, 10) },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.Completed, SortOrder = 1, CompletedOn = new DateOnly(2026, 2, 10) },
                new TimelineItemVm { Code = "SOW", Name = "SOW Vetting", Status = StageStatus.Completed, SortOrder = 2, RequiresBackfill = true },
                new TimelineItemVm { Code = "PNC", Name = "PNC", Status = StageStatus.Skipped, SortOrder = 3 },
                new TimelineItemVm { Code = "AON", Name = "AON", Status = StageStatus.InProgress, SortOrder = 4 },
                new TimelineItemVm { Code = "BID", Name = "Bidding", Status = StageStatus.NotStarted, SortOrder = 5 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        Assert.Equal(1, CountOccurrences(html, "data-timeline-completed-group"));
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("2 completed stages · 1 skipped", text, StringComparison.Ordinal);
        Assert.Contains("Latest: In-Principle Approval · 10 Feb 2026", text, StringComparison.Ordinal);
        var groupStart = html.IndexOf("data-timeline-completed-group", StringComparison.Ordinal);
        var groupEnd = html.IndexOf("</details>", StringComparison.Ordinal);
        int At(string code) => html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
        Assert.True(groupStart < At("FS"));
        Assert.True(At("FS") < At("IPA"));
        Assert.True(At("PNC") < groupEnd);
        // A stage that still needs a completion date stays visible, after the group.
        Assert.True(groupEnd < At("SOW"));
        Assert.True(At("SOW") < At("AON"));
        Assert.True(At("AON") < At("BID"));
        // The group starts collapsed; it opens only on request or for a deep link.
        Assert.DoesNotContain("data-timeline-completed-group open", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedStageWithAPendingUpdate_StaysOutsideTheGroup()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, CompletedOn = new DateOnly(2026, 1, 10) },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.Completed, SortOrder = 1, CompletedOn = new DateOnly(2026, 2, 10) },
                new TimelineItemVm { Code = "SOW", Name = "SOW Vetting", Status = StageStatus.Completed, SortOrder = 2, CompletedOn = new DateOnly(2026, 3, 10),
                    HasPendingRequest = true, PendingStatus = "NotStarted" },
                new TimelineItemVm { Code = "AON", Name = "AON", Status = StageStatus.InProgress, SortOrder = 3 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        Assert.Contains("2 completed stages", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("</details>", StringComparison.Ordinal) < html.IndexOf("id=\"timeline-stage-SOW\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectLifecycleStatus.Active, 1)]
    [InlineData(ProjectLifecycleStatus.Completed, 2)]
    public async Task Grouping_NeedsTwoSettledStagesOnAnActiveProject(ProjectLifecycleStatus lifecycle, int completedStages)
    {
        var items = Enumerable.Range(0, completedStages)
            .Select(index => new TimelineItemVm
            {
                Code = $"S{index}",
                Name = $"Stage {index}",
                Status = StageStatus.Completed,
                SortOrder = index,
                CompletedOn = new DateOnly(2026, 1, 10 + index)
            })
            .Append(new TimelineItemVm { Code = "CUR", Name = "Current", Status = StageStatus.InProgress, SortOrder = 9 })
            .ToArray();

        var html = await RenderAsync(new TimelineVm { ProjectId = 1, Items = items }, isHoD: true, isAssignedProjectOfficer: false, lifecycleStatus: lifecycle);

        Assert.DoesNotContain("data-timeline-completed-group", html, StringComparison.Ordinal);
        Assert.Contains("id=\"timeline-stage-S0\"", html, StringComparison.Ordinal);
    }

    // SECTION: PO Timeline Increment 1b (status line partial)
    [Fact]
    public async Task SubmissionStatus_RendersOneLinePerCategoryWithNotesAndActions()
    {
        var status = new TimelineSubmissionStatusVm(new[]
        {
            new TimelineSubmissionLineVm(TimelineSubmissionStatusVm.PlannedDatesCategory, new[]
            {
                new TimelineSubmissionEntryVm(TimelineSubmissionState.Pending, "Awaiting HoD approval (submitted 14 Oct 2026).",
                    ActionLabel: "View", OffcanvasTarget: "#offcanvasPlanEdit")
            }),
            new TimelineSubmissionLineVm(TimelineSubmissionStatusVm.StageUpdatesCategory, new[]
            {
                new TimelineSubmissionEntryVm(TimelineSubmissionState.Returned, "Completion of SOW Vetting returned on 17 Oct 2026.",
                    "Attach <trial> report first.", "Review", StageCode: "SOW")
            }, HiddenCount: 2)
        });

        var html = await RenderPartialAsync("/Pages/Projects/_TimelineSubmissionStatus.cshtml", status);

        Assert.Contains("aria-label=\"Your submissions\"", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "data-timeline-status-category="));
        Assert.Contains("data-bs-target=\"#offcanvasPlanEdit\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"offcanvasPlanEdit\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#timeline-stage-SOW\"", html, StringComparison.Ordinal);
        Assert.Contains(">Returned<", html, StringComparison.Ordinal);
        // The reviewer's note is shown, HTML-encoded.
        Assert.Contains("Attach &lt;trial&gt; report first.", html, StringComparison.Ordinal);
        Assert.Contains("and 2 more on the timeline below.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmissionStatus_RendersNothingWhenEmpty()
    {
        var html = await RenderPartialAsync("/Pages/Projects/_TimelineSubmissionStatus.cshtml", TimelineSubmissionStatusVm.Empty);

        Assert.Equal(string.Empty, html.Trim());
    }

    [Fact]
    public void LifecycleWorkspace_RendersTheStatusLineAndDropsTheBadgesItRepeats()
    {
        var workspace = File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
            "Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml"));

        Assert.Contains("<partial name=\"_TimelineSubmissionStatus\" model=\"submissionStatus\" />", workspace, StringComparison.Ordinal);
        Assert.Contains("pendingStageUpdateCount > 0 && !statusShowsStagePending", workspace, StringComparison.Ordinal);
        Assert.Contains("pendingOwnedByCurrentUser && !statusShowsPlanPending", workspace, StringComparison.Ordinal);
        Assert.Contains("hasMyDraft && !statusShowsPlanReturned", workspace, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static async Task<string> RenderAsync(
        TimelineVm model,
        bool isHoD,
        bool isAssignedProjectOfficer,
        ProjectLifecycleStatus lifecycleStatus = ProjectLifecycleStatus.Active,
        bool isLegacy = false,
        bool canManageHistoricalStageHistory = false)
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = BuildPrincipal(isHoD ? new[] { "HoD" } : Array.Empty<string>())
        };

        RazorPageTestRouting.EnableEndpointRouting(httpContext);
        var routeData = new RouteData();
        routeData.Values["page"] = "/Projects/Overview";
        var actionContext = new ActionContext(httpContext, routeData, new PageActionDescriptor
        {
            ViewEnginePath = "/Projects/Overview",
            RelativePath = "/Pages/Projects/Overview.cshtml"
        });
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: "/Pages/Shared/_ProjectTimeline.cshtml", isMainPage: false);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException("Unable to locate _ProjectTimeline partial view.");
        }

        await using var writer = new StringWriter();
        var panel = new ProjectTimelinePanelVm
        {
            Timeline = model,
            Access = new ProjectOverviewAccessVm
            {
                IsHoD = isHoD,
                IsAssignedProjectOfficer = isAssignedProjectOfficer,
                CanEditTimeline = isHoD || isAssignedProjectOfficer
            },
            LifecycleStatus = lifecycleStatus,
            IsLegacy = isLegacy,
            CanManageHistoricalStageHistory = canManageHistoricalStageHistory
        };
        var viewData = new ViewDataDictionary<ProjectTimelinePanelVm>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = panel
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static async Task<string> RenderPartialAsync<TModel>(string viewPath, TModel model)
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();
        var httpContext = new DefaultHttpContext { RequestServices = provider, User = BuildPrincipal(Array.Empty<string>()) };
        RazorPageTestRouting.EnableEndpointRouting(httpContext);
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor
        {
            ViewEnginePath = "/Projects/Overview",
            RelativePath = "/Pages/Projects/Overview.cshtml"
        });
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"Unable to locate {viewPath}.");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData,
            new TempDataDictionary(httpContext, tempDataProvider), writer, new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static ClaimsPrincipal BuildPrincipal(string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user"));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var webRoot = Path.Combine(contentRoot, "wwwroot");

        var environment = new TestWebHostEnvironment
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name!,
            ContentRootPath = contentRoot,
            WebRootPath = webRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
            WebRootFileProvider = Directory.Exists(webRoot) ? new PhysicalFileProvider(webRoot) : new NullFileProvider(),
            EnvironmentName = Environments.Development
        };

        var services = new ServiceCollection();
        var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");

        services.AddSingleton<DiagnosticListener>(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();
        services.AddRouting();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ProjectOfficeReportsPolicies.ViewArpp,
                policy => policy.RequireAssertion(_ => true));
        });
        services.AddRazorPages();
        services.AddControllersWithViews();
        RazorPageTestRouting.AddPageEndpoints(services);

        return services.BuildServiceProvider();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
