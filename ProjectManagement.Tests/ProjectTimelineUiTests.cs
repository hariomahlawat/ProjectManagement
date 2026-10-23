using ProjectManagement.Tests.Infrastructure;
using System;
using System.Collections.Generic;
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
        Assert.Contains("Requested change: Completed", html, StringComparison.Ordinal);
        Assert.Contains("Proposed completion", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned completion missing", html, StringComparison.Ordinal);
        Assert.Contains("Edit submission", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "Edit submission"));
    }


    // SECTION: PO Timeline Increment 1e (finished stages, always in workflow order)
    [Fact]
    public async Task FinishedStages_FoldIntoOneGroup_InWorkflowOrder_WithStagesNeedingAttentionInPlace()
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
        Assert.Contains("3 completed stages · 1 skipped · 1 needs attention", text, StringComparison.Ordinal);
        Assert.Contains("Latest: In-Principle Approval · 10 Feb 2026", text, StringComparison.Ordinal);
        var groupStart = html.IndexOf("data-timeline-completed-group", StringComparison.Ordinal);
        var groupEnd = html.IndexOf("</details>", StringComparison.Ordinal);
        int At(string code) => html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
        // Workflow order throughout: the stage needing a completion date keeps its place.
        Assert.True(groupStart < At("FS"));
        Assert.True(At("FS") < At("IPA"));
        Assert.True(At("IPA") < At("SOW"));
        Assert.True(At("SOW") < At("PNC"));
        Assert.True(At("PNC") < groupEnd);
        Assert.True(groupEnd < At("AON"));
        Assert.True(At("AON") < At("BID"));
        // The group starts open, so each completed stage shows its date and days taken;
        // it can still be folded away.
        Assert.Contains("data-timeline-completed-group open", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArppStageWithoutAStartDate_KeepsItsPlace_AndSaysWhy()
    {
        // The reported case: IPA (ARPP-derived, no actual start) was shown just above EAS.
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, CompletedOn = new DateOnly(2025, 3, 10) },
                new TimelineItemVm { Code = "SOW", Name = "SOW Vetting", Status = StageStatus.Completed, SortOrder = 1, CompletedOn = new DateOnly(2025, 5, 10) },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.Completed, SortOrder = 2, CompletedOn = new DateOnly(2025, 6, 17), IsArppManaged = true },
                new TimelineItemVm { Code = "AON", Name = "Acceptance of Necessity", Status = StageStatus.Completed, SortOrder = 3, CompletedOn = new DateOnly(2025, 9, 1) },
                new TimelineItemVm { Code = "PNC", Name = "PNC", Status = StageStatus.Skipped, SortOrder = 4 },
                new TimelineItemVm { Code = "EAS", Name = "EAS Approval", Status = StageStatus.InProgress, SortOrder = 5 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        int At(string code) => html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
        Assert.True(At("SOW") < At("IPA"));
        Assert.True(At("IPA") < At("AON"));
        Assert.True(At("PNC") < At("EAS"));
        Assert.True(At("IPA") < html.IndexOf("</details>", StringComparison.Ordinal));
        Assert.Contains("1 needs attention", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("data-stage-start-attention", html, StringComparison.Ordinal);
        Assert.Contains("Start date missing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheFinishedStagesAtTheStartFold_SoALaterFinishedStageNeverMovesUp()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, CompletedOn = new DateOnly(2026, 1, 10) },
                new TimelineItemVm { Code = "SOW", Name = "SOW Vetting", Status = StageStatus.Completed, SortOrder = 1, CompletedOn = new DateOnly(2026, 2, 10) },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.InProgress, SortOrder = 2 },
                new TimelineItemVm { Code = "AON", Name = "AON", Status = StageStatus.Completed, SortOrder = 3, CompletedOn = new DateOnly(2026, 3, 10) },
                new TimelineItemVm { Code = "BID", Name = "Bidding", Status = StageStatus.NotStarted, SortOrder = 4 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        var groupEnd = html.IndexOf("</details>", StringComparison.Ordinal);
        int At(string code) => html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
        Assert.Contains("2 completed stages", html, StringComparison.Ordinal);
        Assert.True(At("SOW") < groupEnd);
        Assert.True(groupEnd < At("IPA"));
        Assert.True(At("IPA") < At("AON"));
        Assert.True(At("AON") < At("BID"));
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
        Assert.Contains("pendingStageUpdateCount > 0 && !statusCoversAllPendingStageUpdates", workspace, StringComparison.Ordinal);
        Assert.Contains("pendingOwnedByCurrentUser && !statusShowsPlanPending", workspace, StringComparison.Ordinal);
        Assert.Contains("hasMyDraft && !statusShowsPlanReturned", workspace, StringComparison.Ordinal);
    }

    // C7.2: the rendered editor carries no chronology `min`; a refused post reopens it with the
    // user's entries in editable fields only and the server's stage-specific messages.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualsEditor_RefusedPost_RendersEntriesAndServerMessagesWithoutChronologyMin(bool direct)
    {
        var editor = new ActualsEditorVm
        {
            ProjectId = 7,
            Today = new DateOnly(2026, 3, 1),
            BaselineVersion = 1,
            Rows = new[]
            {
                new ActualsEditorRowVm
                {
                    StageCode = "SOW", StageName = "Statement of Work", Status = StageStatus.Completed, IsEditable = true,
                    ActualStart = new DateOnly(2026, 2, 1), CompletedOn = new DateOnly(2026, 2, 10),
                    EarliestAllowedStartDate = new DateOnly(2026, 1, 20), StartBoundarySourceName = "FS", StageToken = "t-sow"
                },
                new ActualsEditorRowVm
                {
                    StageCode = "IPA", StageName = "In-Principle Approval", Status = StageStatus.Completed, IsEditable = true,
                    ActualStart = new DateOnly(2026, 2, 11), CompletedOn = new DateOnly(2026, 2, 20),
                    EarliestAllowedStartDate = new DateOnly(2026, 2, 10), StartBoundarySourceName = "Statement of Work",
                    StageToken = "t-ipa", IsArppManaged = true
                }
            }
        };
        var payload = ActualsEditorRecovery.Serialize(
            new ActualsEditInput
            {
                ProjectId = 7,
                BaselineVersion = 1,
                SubmissionMode = direct ? ActualsSubmissionMode.Direct : ActualsSubmissionMode.Proposal,
                Note = "Per register",
                Rows = new[]
                {
                    new ActualsEditRowInput { StageCode = "SOW", StageToken = "t-sow", ActualStart = new DateOnly(2026, 2, 1), CompletedOn = new DateOnly(2026, 2, 12) },
                    // IPA's completion is ARPP-controlled (disabled), so only its start was posted.
                    new ActualsEditRowInput { StageCode = "IPA", StageToken = "t-ipa", ActualStart = new DateOnly(2026, 2, 11) }
                }.ToList()
            },
            null,
            "user",
            new[] { "IPA: Actual start 11 Feb 2026 would be before Statement of Work Actual completion 12 Feb 2026. Correct IPA in the same update." });
        var restored = ActualsEditorRecovery.Restore(
            editor, payload, 7, "user", direct ? ActualsSubmissionMode.Direct : ActualsSubmissionMode.Proposal);

        var html = await RenderActualsEditorAsync(restored, direct);

        Assert.Contains("data-actuals-restored=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-actuals-submission-errors", html, StringComparison.Ordinal);
        Assert.Contains("Correct IPA in the same update.", html, StringComparison.Ordinal);
        Assert.Contains(direct ? "Nothing was saved." : "Nothing was submitted.", html, StringComparison.Ordinal);
        Assert.Matches("id=\"actual-completed-SOW\"[^>]*value=\"2026-02-12\"", html);
        Assert.Matches("id=\"actual-start-IPA\"[^>]*value=\"2026-02-11\"", html);
        Assert.Matches("id=\"actual-completed-IPA\"[^>]*value=\"2026-02-20\"", html);
        Assert.DoesNotMatch(@"<input[^>]*type=""date""[^>]*\smin=", html);
        Assert.Equal(!direct, html.Contains(">Per register</textarea>", StringComparison.Ordinal));

        var fresh = await RenderActualsEditorAsync(editor, direct);
        Assert.DoesNotContain("data-actuals-restored=\"true\"", fresh, StringComparison.Ordinal);
        Assert.DoesNotContain("data-actuals-submission-errors", fresh, StringComparison.Ordinal);
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

    // PO Timeline Increment 3: one name for missing completion details, "submitted" once they
    // are in an approvable pending correction, the stage name first, and "by you" on one's own request.
    [Fact]
    public async Task TimelineCards_UseOneVocabularyAndNameTheStageFirst()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, RequiresBackfill = true },
                new TimelineItemVm { Code = "SOW", Name = "SOW Vetting", Status = StageStatus.Completed, SortOrder = 1, RequiresBackfill = true },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.InProgress, SortOrder = 2,
                    HasPendingRequest = true, PendingStatus = "Completed", PendingDate = new DateOnly(2026, 3, 1),
                    PendingRequestedBy = "Test Project Officer", PendingRequestedByUserId = "po-user", PendingRequestedOn = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero) }
            }
        };

        var html = System.Net.WebUtility.HtmlDecode(await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true,
            currentUserId: "po-user", completionDetailsSubmitted: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SOW" }));

        Assert.DoesNotContain("Backfill required", html, StringComparison.Ordinal);
        Assert.Matches("pm-pill-warning[^\"]*\"[^>]*aria-label=\"Add completion details for Feasibility Study\"", html);
        Assert.Matches("pm-pill-info[^\"]*\"[^>]*aria-label=\"Completion details submitted for SOW Vetting", html);
        Assert.Contains("Requested change: Completed", html, StringComparison.Ordinal);
        Assert.Contains("Submitted by you", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending lifecycle updates", html, StringComparison.Ordinal);
        // The status pill is followed directly by the stage name.
        Assert.Matches(@"data-stage-status-pill>\s*In progress\s*</span>\s*<span class=""pm-item-title", html);
    }

    [Fact]
    public async Task CurrentNotStartedStage_KeepsItsFullCard_WhileLaterStagesAreCompact()
    {
        // No stage is in progress, so the first unresolved stage (IPA) is the current one.
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "FS", Name = "Feasibility Study", Status = StageStatus.Completed, SortOrder = 0, CompletedOn = new DateOnly(2026, 1, 10) },
                new TimelineItemVm { Code = "IPA", Name = "In-Principle Approval", Status = StageStatus.NotStarted, SortOrder = 1 },
                new TimelineItemVm { Code = "AON", Name = "AON", Status = StageStatus.NotStarted, SortOrder = 2 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true);

        string RowTag(string code)
        {
            var start = html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
            return html.Substring(start, html.IndexOf('>', start) - start);
        }

        Assert.DoesNotContain("is-future", RowTag("IPA"), StringComparison.Ordinal);
        Assert.Contains("data-stage-current=\"true\"", RowTag("IPA"), StringComparison.Ordinal);
        Assert.Contains("is-future", RowTag("AON"), StringComparison.Ordinal);
        Assert.Contains("data-stage-current=\"false\"", RowTag("AON"), StringComparison.Ordinal);
    }

    // SECTION: Coming up and a single overdue mention
    [Fact]
    public async Task LaterStages_TagTheNextTwo_AndFoldTheRestWithTheirActions()
    {
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm { Code = "TEC", Name = "Technical Evaluation", Status = StageStatus.InProgress, SortOrder = 0 },
                new TimelineItemVm { Code = "BM", Name = "Benchmarking", Status = StageStatus.NotStarted, SortOrder = 1 },
                new TimelineItemVm { Code = "COB", Name = "Commercial Bid Opening", Status = StageStatus.NotStarted, SortOrder = 2 },
                new TimelineItemVm { Code = "EAS", Name = "EAS Approval", Status = StageStatus.NotStarted, SortOrder = 3 },
                new TimelineItemVm { Code = "SO", Name = "Supply Order", Status = StageStatus.NotStarted, SortOrder = 4 }
            }
        };

        var html = await RenderAsync(timeline, isHoD: true, isAssignedProjectOfficer: false);
        int At(string code) => html.IndexOf($"id=\"timeline-stage-{code}\"", StringComparison.Ordinal);
        var foldStart = html.IndexOf("data-timeline-later-group", StringComparison.Ordinal);
        var foldEnd = html.IndexOf("</details>", foldStart, StringComparison.Ordinal);

        Assert.Contains(">Next</span>", html.Substring(At("BM"), At("COB") - At("BM")), StringComparison.Ordinal);
        Assert.Contains(">Then</span>", html.Substring(At("COB"), foldStart - At("COB")), StringComparison.Ordinal);
        Assert.Contains("+2 more later stages", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.True(At("COB") < foldStart && foldStart < At("EAS") && At("SO") < foldEnd);
        // The folded stages keep the HoD's stage action menu.
        Assert.Contains("data-stage-action-menu", html.Substring(At("SO"), foldEnd - At("SO")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverdueCurrentStage_SaysHowLateOnce()
    {
        var today = new DateOnly(2026, 10, 22);
        var timeline = new TimelineVm
        {
            ProjectId = 1,
            Items = new[]
            {
                new TimelineItemVm
                {
                    Code = "TEC", Name = "Technical Evaluation", Status = StageStatus.InProgress, SortOrder = 0,
                    ActualStart = new DateOnly(2026, 6, 20), PlannedEnd = new DateOnly(2026, 8, 7), Today = today
                }
            }
        };

        var text = System.Net.WebUtility.HtmlDecode(await RenderAsync(timeline, isHoD: false, isAssignedProjectOfficer: true));

        Assert.Contains("Overdue 76 days", text, StringComparison.Ordinal);
        Assert.DoesNotContain("76 days overdue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Set planned completion", text, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(
        TimelineVm model,
        bool isHoD,
        bool isAssignedProjectOfficer,
        ProjectLifecycleStatus lifecycleStatus = ProjectLifecycleStatus.Active,
        bool isLegacy = false,
        bool canManageHistoricalStageHistory = false,
        string? currentUserId = null,
        IReadOnlySet<string>? completionDetailsSubmitted = null)
    {
        var panel = new ProjectTimelinePanelVm
        {
            CurrentUserId = currentUserId,
            CompletionDetailsSubmittedStages = completionDetailsSubmitted ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
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
        return await RenderPartialAsync("/Pages/Shared/_ProjectTimeline.cshtml", viewData, isHoD ? new[] { "HoD" } : Array.Empty<string>());
    }

    private static async Task<string> RenderPartialAsync(string viewPath, ViewDataDictionary viewData, string[] roles)
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;
        var viewEngine = provider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = provider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = BuildPrincipal(roles)
        };

        RazorPageTestRouting.EnableEndpointRouting(httpContext);
        var routeData = new RouteData();
        routeData.Values["page"] = "/Projects/Overview";
        var actionContext = new ActionContext(httpContext, routeData, new PageActionDescriptor
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

    // PO Timeline Increment 2b: a refused planner post shows its messages inside the panel.
    [Fact]
    public async Task PlanEditor_RefusedPost_ShowsMessagesInThePanel()
    {
        var editor = new PlanEditorVm
        {
            Exact = new PlanEditVm { ProjectId = 7 },
            Durations = new PlanDurationVm { ProjectId = 7 },
            ActiveMode = PlanEditorModes.Exact,
            SubmissionErrors = new[] { "For Feasibility Study, Start date cannot be after Due date." },
            RestoredSubmission = true
        };

        var html = await RenderPartialAsync("/Pages/Projects/Timeline/_EditPlanForm.cshtml", editor);

        Assert.Contains("data-plan-submission-errors", html, StringComparison.Ordinal);
        Assert.Contains("data-plan-restored", html, StringComparison.Ordinal);
        Assert.Matches(@"Nothing was saved\.\s+Your entries are shown below\.", html);
        Assert.Contains("<li>For Feasibility Study, Start date cannot be after Due date.</li>", html, StringComparison.Ordinal);

        var current = await RenderPartialAsync("/Pages/Projects/Timeline/_EditPlanForm.cshtml", new PlanEditorVm
        {
            Exact = new PlanEditVm { ProjectId = 7 },
            Durations = new PlanDurationVm { ProjectId = 7 },
            SubmissionErrors = new[] { "This timeline draft changed in another session. Reload before saving." }
        });
        Assert.Contains("the current plan is shown", current, StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan-restored", current, StringComparison.Ordinal);

        var clean = await RenderPartialAsync("/Pages/Projects/Timeline/_EditPlanForm.cshtml", new PlanEditorVm
        {
            Exact = new PlanEditVm { ProjectId = 7 },
            Durations = new PlanDurationVm { ProjectId = 7 }
        });
        Assert.DoesNotContain("data-plan-submission-errors", clean, StringComparison.Ordinal);
    }

    // PO Timeline Increment 2b/C5: the named stage is marked, and every stage field carries
    // its stage name for screen readers (the Duration input had no label at all).
    [Fact]
    public async Task PlanEditorRows_MarkTheirErrorsAndNameTheirStage()
    {
        var exact = await RenderPartialAsync("/Pages/Projects/Timeline/_PlanExactRow.cshtml", new PlanExactRowRenderVm
        {
            Row = new PlanEditVm.PlanEditRow { Code = "FS", Name = "Feasibility Study", PlannedStart = new DateOnly(2026, 3, 10) },
            Index = 0,
            Errors = new[] { "For Feasibility Study, Start date cannot be after Due date." }
        });
        Assert.Matches("id=\"ExactRows_0__PlannedStart\"[^>]*aria-invalid=\"true\"[^>]*aria-describedby=\"ExactRows_0__Errors\"", exact);
        Assert.Contains("id=\"ExactRows_0__Errors\"", exact, StringComparison.Ordinal);
        Assert.Contains("Planned start<span class=\"visually-hidden\"> for Feasibility Study</span>", exact, StringComparison.Ordinal);
        Assert.Contains("data-plan-stage-code=\"FS\"", exact, StringComparison.Ordinal);

        var valid = await RenderPartialAsync("/Pages/Projects/Timeline/_PlanExactRow.cshtml", new PlanExactRowRenderVm
        {
            Row = new PlanEditVm.PlanEditRow { Code = "SOW", Name = "SOW Vetting" },
            Index = 1
        });
        Assert.DoesNotContain("aria-invalid", valid, StringComparison.Ordinal);
        Assert.DoesNotContain("data-plan-row-error", valid, StringComparison.Ordinal);

        var duration = await RenderPartialAsync("/Pages/Projects/Timeline/_PlanDurationRow.cshtml", new PlanDurationRowRenderVm
        {
            Row = new PlanDurationRowVm { Code = "FS", Name = "Feasibility Study", DurationDays = 0 },
            Index = 0,
            Errors = new[] { "Duration for Feasibility Study must be a positive number of days." }
        });
        Assert.Contains("<label class=\"input-group-text\" for=\"DurationRows_0__DurationDays\"", duration, StringComparison.Ordinal);
        Assert.Contains("Duration (days)<span class=\"visually-hidden\"> for Feasibility Study</span>", duration, StringComparison.Ordinal);
        Assert.Matches("id=\"DurationRows_0__DurationDays\"[^>]*aria-invalid=\"true\"", duration);
    }

    // PO Timeline Increment 2c: an explicit clear control, official values beside the proposal,
    // and the values a rejected correction proposed, for reference.
    [Fact]
    public async Task ActualsEditor_ShowsClearControlOfficialValuesAndRejectedProposal()
    {
        var proposal = new ActualsEditorVm
        {
            ProjectId = 7,
            Today = new DateOnly(2026, 3, 1),
            BaselineVersion = 1,
            Rows = new[]
            {
                new ActualsEditorRowVm
                {
                    StageCode = "SOW", StageName = "SOW Vetting", Status = StageStatus.Completed, IsEditable = true,
                    ActualStart = new DateOnly(2026, 1, 21), CompletedOn = new DateOnly(2026, 2, 10), StageToken = "t-sow",
                    HasProposal = true, ProposedActualStart = null, ProposedCompletedOn = new DateOnly(2026, 2, 12), ProposalClearsStart = true
                },
                new ActualsEditorRowVm
                {
                    StageCode = "TEC", StageName = "Technical Evaluation", Status = StageStatus.InProgress, IsEditable = true,
                    ActualStart = new DateOnly(2026, 2, 20), StageToken = "t-tec"
                }
            }
        };

        var html = await RenderActualsEditorAsync(proposal, direct: false);

        // A completed stage with a recorded start can be cleared explicitly; an in-progress one cannot.
        Assert.Matches("id=\"actual-start-SOW-clear\"[^>]*data-clear-start-toggle[^>]*checked", html);
        Assert.DoesNotContain("actual-start-TEC-clear", html, StringComparison.Ordinal);
        Assert.Contains("data-actuals-official-start>Official: 21 Jan 2026<", html, StringComparison.Ordinal);
        Assert.Contains("data-actuals-official-completed>Official: 10 Feb 2026<", html, StringComparison.Ordinal);
        // The server contract is unchanged: the hidden clear flag is still posted.
        Assert.Contains("name=\"Input.Rows[0].ClearActualStart\" value=\"true\"", html, StringComparison.Ordinal);

        var rejected = new ActualsEditorVm
        {
            ProjectId = 7,
            Today = new DateOnly(2026, 3, 1),
            BaselineVersion = 1,
            Rows = new[]
            {
                new ActualsEditorRowVm
                {
                    StageCode = "SOW", StageName = "SOW Vetting", Status = StageStatus.Completed, IsEditable = true,
                    ActualStart = new DateOnly(2026, 1, 21), CompletedOn = new DateOnly(2026, 2, 10), StageToken = "t-sow"
                }
            },
            LastRejection = new ActualRejectionNoticeVm(1, new DateTimeOffset(2026, 2, 20, 0, 0, 0, TimeSpan.Zero), "Attach minutes", true,
                new Dictionary<string, ActualRejectedItemVm>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SOW"] = new ActualRejectedItemVm(null, new DateOnly(2026, 2, 12), ClearsActualStart: true)
                })
        };

        var rejectedHtml = await RenderActualsEditorAsync(rejected, direct: false);

        Assert.Contains("Rejected proposal: start cleared · completed 12 Feb 2026.", System.Net.WebUtility.HtmlDecode(rejectedHtml), StringComparison.Ordinal);
        // The rows stay official after a rejection.
        Assert.Matches("id=\"actual-completed-SOW\"[^>]*value=\"2026-02-10\"", rejectedHtml);
        Assert.DoesNotMatch("id=\"actual-start-SOW-clear\"[^>]*checked", rejectedHtml);
    }

    private static Task<string> RenderActualsEditorAsync(ActualsEditorVm model, bool direct)
    {
        var viewData = new ViewDataDictionary<ActualsEditorVm>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
            ["ActualsDirectPublication"] = direct,
            ["ActualsProposalSubmission"] = !direct
        };
        return RenderPartialAsync("/Pages/Projects/Timeline/_EditActualsForm.cshtml", viewData, direct ? new[] { "HoD" } : new[] { "Project Officer" });
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
