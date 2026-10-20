using Microsoft.Data.Sqlite;
using ProjectManagement.Infrastructure;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Text;
using ProjectManagement.Tests.Fakes;
using ProjectsOverviewModel = ProjectManagement.Pages.Projects.OverviewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class EditPlanDurationSafetyTests
{
    [Fact]
    public async Task DirectExact_RejectsChangedPredecessorThatInvalidatesOmittedSuccessorWithoutAnyMutation()
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        project.ActivePlanVersionNo = 8;
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = StageCodes.FS
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 1, 5),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 3, SortOrder = 10 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA, DurationDays = 3, SortOrder = 20 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = "EXTENSION", DurationDays = 2, SortOrder = 80 });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2024, 1, 4),
                PlannedDue = new DateOnly(2024, 1, 6)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "EXTENSION",
                SortOrder = 80,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2024, 2, 1),
                PlannedDue = new DateOnly(2024, 2, 2)
            });
        var actorDraft = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 9,
            OwnerUserId = f.User.UserId,
            CreatedByUserId = f.User.UserId!,
            Status = PlanVersionStatus.Draft,
            PlanningInputVersion = PlanningInputVersions.Current,
            AnchorStageCode = StageCodes.FS,
            PncApplicable = false
        };
        actorDraft.StagePlans.Add(new StagePlan
        {
            StageCode = "EXTENSION",
            SortOrder = 80,
            DurationDays = 2,
            PlannedStart = new DateOnly(2024, 2, 1),
            PlannedDue = new DateOnly(2024, 2, 2)
        });
        f.Db.PlanVersions.Add(actorDraft);
        await f.Db.SaveChangesAsync();
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new()
            {
                Code = StageCodes.FS,
                Name = "FS",
                PlannedStart = new DateOnly(2024, 1, 1),
                PlannedDue = new DateOnly(2024, 1, 5)
            }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Fact]
    public async Task DirectExact_FullFormDoesNotLowerAnchorForUnchangedPreAnchorRow()
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveChanges", relational: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = StageCodes.FS
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.PlannedStart = new DateOnly(2024, 1, 1);
        fs.PlannedDue = new DateOnly(2024, 1, 15);
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 20,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2024, 1, 10),
            PlannedDue = new DateOnly(2024, 1, 12)
        });
        await f.Db.SaveChangesAsync();
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new()
            {
                Code = StageCodes.FS,
                Name = "FS",
                PlannedStart = new DateOnly(2024, 1, 1),
                PlannedDue = new DateOnly(2024, 1, 15)
            },
            new()
            {
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                PlannedStart = new DateOnly(2024, 1, 10),
                PlannedDue = new DateOnly(2024, 1, 13)
            }
        };

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var stages = await f.Db.ProjectStages.ToDictionaryAsync(stage => stage.StageCode);
        Assert.Equal(new DateOnly(2024, 1, 15), stages[StageCodes.FS].PlannedDue);
        Assert.Equal(new DateOnly(2024, 1, 13), stages[StageCodes.IPA].PlannedDue);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectExact_AppliesExplicitClearAndPreservesOmittedExtension(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 1, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA, DurationDays = 3, SortOrder = 20 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = "EXTENSION", DurationDays = 2, SortOrder = 80 });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2024, 1, 4),
                PlannedDue = new DateOnly(2024, 1, 6)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "EXTENSION",
                SortOrder = 80,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2024, 2, 1),
                PlannedDue = new DateOnly(2024, 2, 2)
            });
        await f.Db.SaveChangesAsync();
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new()
            {
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                PlannedStart = null,
                PlannedDue = null
            }
        };

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var stages = await f.Db.ProjectStages.ToDictionaryAsync(stage => stage.StageCode);
        Assert.Null(stages[StageCodes.IPA].PlannedStart);
        Assert.Null(stages[StageCodes.IPA].PlannedDue);
        Assert.Equal(new DateOnly(2024, 2, 1), stages["EXTENSION"].PlannedStart);
        Assert.Equal(new DateOnly(2024, 2, 2), stages["EXTENSION"].PlannedDue);
        Assert.Null((await f.Db.ProjectPlanDurations.SingleAsync(row => row.StageCode == StageCodes.IPA)).DurationDays);
        Assert.Equal(2, (await f.Db.ProjectPlanDurations.SingleAsync(row => row.StageCode == "EXTENSION")).DurationDays);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectExact_RejectsConfiguredPreAnchorPredecessorChangeWithoutAnyMutation(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        f.Db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = "CUSTOM",
                Name = "Custom Review",
                Sequence = 15
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = "CUSTOM"
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2026, 11, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "CUSTOM",
                SortOrder = 15,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 1),
                PlannedDue = new DateOnly(2026, 11, 10)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 11),
                PlannedDue = new DateOnly(2026, 11, 12)
            });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new()
            {
                Code = "CUSTOM",
                Name = "Custom Review",
                PlannedStart = new DateOnly(2026, 11, 1),
                PlannedDue = new DateOnly(2026, 11, 15)
            }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsConfiguredGeneratedPredecessorChangeWithoutAnyMutation(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        f.Db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = "CUSTOM",
                Name = "Custom Review",
                Sequence = 15
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = "CUSTOM"
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2026, 11, 10),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = "CUSTOM",
            DurationDays = 1,
            SortOrder = 15
        });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "CUSTOM",
                SortOrder = 15,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 10),
                PlannedDue = new DateOnly(2026, 11, 10)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 11),
                PlannedDue = new DateOnly(2026, 11, 12)
            });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 10);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = "CUSTOM", Name = "Custom Review", DurationDays = 6 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsPolicyOnlyChangeAcrossConfiguredPreAnchorPredecessorWithoutAnyMutation(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.PlannedStart = null;
        fs.PlannedDue = null;
        f.Db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = "CUSTOM",
                Name = "Custom Review",
                Sequence = 15
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = "CUSTOM"
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2026, 11, 10),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            DurationDays = 2,
            SortOrder = 20
        });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "CUSTOM",
                SortOrder = 15,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 10),
                PlannedDue = new DateOnly(2026, 11, 10)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 10),
                PlannedDue = new DateOnly(2026, 11, 11)
            });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 10);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.IPA, Name = "In-Principle Approval", DurationDays = 2 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsAnchorOnlyChangeAcrossConfiguredPreAnchorPredecessorWithoutAnyMutation(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.PlannedStart = null;
        fs.PlannedDue = null;
        f.Db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = "CUSTOM",
                Name = "Custom Review",
                Sequence = 15
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = "CUSTOM"
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2026, 11, 10),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            DurationDays = 2,
            SortOrder = 20
        });
        f.Db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = "CUSTOM",
                SortOrder = 15,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 10),
                PlannedDue = new DateOnly(2026, 11, 10)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 20,
                Status = StageStatus.NotStarted,
                PlannedStart = new DateOnly(2026, 11, 10),
                PlannedDue = new DateOnly(2026, 11, 11)
            });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 9);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.IPA, Name = "In-Principle Approval", DurationDays = 2 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsGeneratedPredecessorThatInvalidatesRetainedManualSuccessorWithoutAnyMutation(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        project.ActivePlanVersionNo = 8;
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = StageCodes.FS
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 1, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = StageCodes.FS,
            DurationDays = 3,
            SortOrder = 10
        });
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 20,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2024, 1, 4),
            PlannedDue = new DateOnly(2024, 1, 6)
        });
        var actorDraft = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 9,
            OwnerUserId = f.User.UserId,
            CreatedByUserId = f.User.UserId!,
            Status = PlanVersionStatus.Draft,
            PlanningInputVersion = PlanningInputVersions.Current,
            AnchorStageCode = StageCodes.FS,
            AnchorDate = new DateOnly(2024, 1, 1),
            PncApplicable = false,
            TransitionRule = PlanTransitionRule.SameDay,
            SkipWeekends = false,
            SkipHolidays = false
        };
        actorDraft.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.IPA,
            SortOrder = 20,
            DurationDays = 3,
            PlannedStart = new DateOnly(2024, 1, 4),
            PlannedDue = new DateOnly(2024, 1, 6)
        });
        f.Db.PlanVersions.Add(actorDraft);
        await f.Db.SaveChangesAsync();
        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2024, 1, 1);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.FS, Name = "FS", DurationDays = 5 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsTransitionPolicyOnlyChangeAgainstRetainedManualPredecessor(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.PlannedStart = new DateOnly(2024, 1, 1);
        fs.PlannedDue = new DateOnly(2024, 1, 5);
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = StageCodes.FS
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 1, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            DurationDays = 2,
            SortOrder = 20
        });
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 20,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2024, 1, 5),
            PlannedDue = new DateOnly(2024, 1, 6)
        });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2024, 1, 5);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.IPA, Name = "In-Principle Approval", DurationDays = 2 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("In-Principle Approval", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDuration_RejectsHolidayPolicyOnlyChangeAgainstRetainedManualPredecessor(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Current;
        project.PncApplicable = false;
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.PlannedStart = new DateOnly(2026, 10, 19);
        fs.PlannedDue = new DateOnly(2026, 10, 23);
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.IPA,
            Name = "In-Principle Approval",
            Sequence = 20
        });
        f.Db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.IPA,
            DependsOnStageCode = StageCodes.FS
        });
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2026, 10, 26),
            IncludeWeekends = false,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            DurationDays = 5,
            SortOrder = 20
        });
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 20,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 10, 26),
            PlannedDue = new DateOnly(2026, 10, 30)
        });
        f.Db.Holidays.Add(new Holiday
        {
            Date = new DateOnly(2026, 10, 26),
            Name = "New gazetted closure",
            Type = HolidayType.Gazetted,
            IsObservedAsOfficeHoliday = true
        });
        await f.Db.SaveChangesAsync();

        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.AnchorStart = new DateOnly(2026, 10, 26);
        f.Page.Input.IncludeWeekends = false;
        f.Page.Input.SkipHolidays = true;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.IPA, Name = "In-Principle Approval", DurationDays = 5 }
        };
        var before = await PlannerStateAsync(f.Db);
        var auditBefore = await AuditStateAsync(f.Db);

        var result = await f.Page.OnPostAsync(1, default);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("27 Oct 2026", Assert.IsType<string>(f.Page.TempData["Error"]));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(auditBefore, await AuditStateAsync(f.Db));
    }

    [Theory]
    [InlineData("HoD", true, PlanEditorModes.Durations)]
    [InlineData("HoD", false, PlanEditorModes.Durations)]
    [InlineData("Admin", true, PlanEditorModes.Durations)]
    [InlineData("Admin", false, PlanEditorModes.Durations)]
    [InlineData("HoD", true, PlanEditorModes.Exact)]
    [InlineData("HoD", false, PlanEditorModes.Exact)]
    [InlineData("Admin", true, PlanEditorModes.Exact)]
    [InlineData("Admin", false, PlanEditorModes.Exact)]
    public async Task DirectPublication_CommitsCurrentApplicabilityAndRefreshesScopedPolicy(string role, bool initiallyApplicable, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        f.Page.Input.Mode = mode;
        var pnc = f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC);
        pnc.DurationDays = initiallyApplicable ? null : 2;
        pnc.PlannedStart = initiallyApplicable ? null : new DateOnly(2026, 10, 22);
        pnc.PlannedDue = initiallyApplicable ? null : new DateOnly(2026, 10, 23);
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal(!initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(f.Db.Database.GetDbConnection()).Options);
        var freshPolicy = new ProjectStageWorkflowPolicy(fresh, new WorkflowStageMetadataProvider());
        Assert.Equal(!initiallyApplicable, (await freshPolicy.GetAsync(1)).PncApplicable);
        var rules = new StageRulesService(fresh, freshPolicy);
        Assert.Equal(initiallyApplicable, rules.CanStart(await rules.GetContextAsync(1, default), StageCodes.EAS).Allowed);
        Assert.Equal(initiallyApplicable, (await fresh.PlanVersions.SingleAsync(p => p.VersionNo == 40)).PncApplicable);
        Assert.Equal(40, (await fresh.Projects.SingleAsync()).ActivePlanVersionNo);
        var stale = await fresh.ProjectStages.SingleAsync(p => p.StageCode == StageCodes.PNC);
        Assert.Equal(StageStatus.NotStarted, stale.Status);
        var audit = Assert.Single(await fresh.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(mode == PlanEditorModes.Exact ? "Projects.PlanUpdated" : "Projects.PlanGeneratedFromDurations", audit.Action);
        var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(audit.DataJson!)!;
        Assert.Equal(initiallyApplicable.ToString(), data["PncApplicable.Before"]);
        Assert.Equal((!initiallyApplicable).ToString(), data["PncApplicable.After"]);
    }

    [Theory]
    [InlineData("HoD", false)]
    [InlineData("Admin", false)]
    [InlineData("HoD", true)]
    [InlineData("Admin", true)]
    public async Task ExactPublication_UnchangedDatesStillPublishExplicitApplicability(string role, bool initiallyApplicable)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        if (initiallyApplicable)
        {
            var cleared = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
            cleared.PlannedStart = cleared.PlannedDue = null;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        f.Page.Input.Mode = PlanEditorModes.Exact;
        foreach (var row in f.Page.Input.Rows)
        {
            var stage = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == row.Code);
            row.PlannedStart = stage.PlannedStart;
            row.PlannedDue = stage.PlannedDue;
        }
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(!initiallyApplicable, (await f.Db.Projects.SingleAsync()).PncApplicable);
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(initiallyApplicable ? (DateOnly?)null : new DateOnly(2024, 1, 3), pnc.PlannedStart);
        Assert.Equal(StageStatus.NotStarted, pnc.Status);
        var audit = Assert.Single(await f.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal("Projects.PlanUpdated", audit.Action);
        var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(audit.DataJson!)!;
        Assert.Equal("1", data["ProjectId"]);
        Assert.Equal("Direct", data["SaveMode"]);
        Assert.Equal(initiallyApplicable.ToString(), data["PncApplicable.Before"]);
        Assert.Equal((!initiallyApplicable).ToString(), data["PncApplicable.After"]);
        Assert.Equal(string.Empty, data["ChangedStages"]);
        Assert.DoesNotContain(data.Keys, key => key.StartsWith("Stage[", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Exact, false)]
    [InlineData("HoD", PlanEditorModes.Exact, true)]
    [InlineData("Admin", PlanEditorModes.Exact, false)]
    [InlineData("Admin", PlanEditorModes.Exact, true)]
    [InlineData("HoD", PlanEditorModes.Durations, false)]
    [InlineData("HoD", PlanEditorModes.Durations, true)]
    [InlineData("Admin", PlanEditorModes.Durations, false)]
    [InlineData("Admin", PlanEditorModes.Durations, true)]
    [InlineData("HoD", "exact", false)]
    [InlineData("Admin", "DURATIONS", true)]
    public async Task DirectPublication_ExplicitUnchangedDecisionLosesConcurrencyAndRollsBackAllWrites(string role, string mode, bool initiallyApplicable)
    {
        var conflicts = new PublicationConcurrencyObserver();
        await using var loser = await Fixture.CreateAsync(role, "SaveChanges", relational: true, persistAudit: true, interceptor: conflicts);
        await SeedRecognizablePlannerStateAsync(loser);
        await SeedPublishedExecutionAsync(loser, initiallyApplicable);

        // The two request contexts read the same original canonical value/token.
        // Keep A's tracked Project stale while B publishes the opposite decision.
        // Transactions themselves run in commit order, avoiding SQLite writer locks:
        // rejection must come from EF's Project token predicate, not a lock error.
        var original = await loser.Db.Projects.SingleAsync();
        var originalToken = original.RowVersion.ToArray();
        await using var winner = await Fixture.ForExistingDatabaseAsync(role, loser.Db.Database.GetDbConnection());
        var winnerProject = await winner.Db.Projects.SingleAsync();
        Assert.Equal(initiallyApplicable, winnerProject.PncApplicable);
        Assert.Equal(originalToken, winnerProject.RowVersion);
        ConfigurePublication(winner.Page, mode, !initiallyApplicable, new DateOnly(2026, 10, 19), 3);
        ConfigurePublication(loser.Page, mode, initiallyApplicable, new DateOnly(2026, 11, 2), 7);
        var submittedInput = JsonSerializer.Serialize(loser.Page.Input);
        Assert.IsType<RedirectToPageResult>(await winner.Page.OnPostAsync(1, default));
        Assert.False(winner.Page.TempData.ContainsKey("Error"));
        Assert.Equal(!initiallyApplicable, (await winner.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        Assert.False(originalToken.SequenceEqual((await winner.Db.Projects.AsNoTracking().SingleAsync()).RowVersion));
        var winningState = await PlannerStateAsync(winner.Db);
        var winningAudit = await AuditStateAsync(winner.Db);
        Assert.Single(await winner.Db.AuditLogs.AsNoTracking().ToListAsync());

        var result = await loser.Page.OnPostAsync(1, default);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Projects/Overview", redirect.PageName);
        Assert.False(string.IsNullOrWhiteSpace(loser.Page.TempData["Error"] as string));
        Assert.Equal("plan-edit", loser.Page.TempData["OpenOffcanvas"]);
        Assert.False(loser.Page.TempData.ContainsKey("Flash"));
        Assert.Equal(submittedInput, JsonSerializer.Serialize(loser.Page.Input));
        var recoveryPayload = Assert.IsType<string>(loser.Page.TempData["PlanEditRecovery"]);
        using var recovery = JsonDocument.Parse(recoveryPayload);
        Assert.Equal("other-user", recovery.RootElement.GetProperty("UserId").GetString());
        Assert.Equal(submittedInput, JsonSerializer.Serialize(recovery.RootElement.GetProperty("Input")));
        await AssertOverviewRecoversInputAsync(loser.Page.Input, recoveryPayload);
        var conflict = Assert.Single(conflicts.Exceptions);
        Assert.IsType<Project>(Assert.Single(conflict.Entries).Entity);

        // This includes official dates, working durations/settings, private draft
        // StagePlans, publication state, and audit records from both contexts.
        Assert.Equal(winningState, await PlannerStateAsync(winner.Db));
        Assert.Equal(winningAudit, await AuditStateAsync(winner.Db));
        Assert.Equal(!initiallyApplicable, (await winner.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
    }

    [Theory]
    [InlineData(PlanEditorModes.Exact)]
    [InlineData(PlanEditorModes.Durations)]
    [InlineData("exact")]
    [InlineData("DURATIONS")]
    public async Task Overview_ConflictRecoveryKeepsFreshAuthorityAndIgnoresUnknownStages(string mode)
    {
        var input = new PlanEditInput { ProjectId = 1, Action = "SaveChanges" };
        var page = await Fixture.CreateAsync("Admin", "SaveChanges");
        await using (page)
        {
            ConfigurePublication(page.Page, mode, false, new DateOnly(2026, 11, 2), 7);
            input = page.Page.Input;
        }
        input.Rows[0].Name = "Forged stage name";
        input.Rows.Add(new PlanEditInputRow { Code = "UNKNOWN", Name = "Untrusted", DurationDays = 99 });
        var payload = JsonSerializer.Serialize(new { UserId = "other-user", Input = input });
        await AssertOverviewRecoversInputAsync(input, payload);
    }

    [Theory]
    [InlineData("wrong-user")]
    [InlineData("wrong-project")]
    [InlineData("malformed")]
    [InlineData("wrong-mode")]
    [InlineData("unauthorized-role")]
    public async Task Overview_ConflictRecoveryRejectsWrongScopeOrInvalidPayload(string scenario)
    {
        await using var f = await Fixture.CreateAsync("Admin", "SaveChanges");
        await SeedRecognizablePlannerStateAsync(f);
        var before = await new PlanReadService(f.Db).GetAsync(1, "other-user");
        ConfigurePublication(f.Page, scenario == "wrong-mode" ? "Unknown" : PlanEditorModes.Durations,
            false, new DateOnly(2026, 11, 2), 7);
        if (scenario == "wrong-project") f.Page.Input.ProjectId = 2;
        var payload = scenario == "malformed" ? "{invalid-json"
            : JsonSerializer.Serialize(new { UserId = scenario == "wrong-user" ? "somebody-else" : "other-user", Input = f.Page.Input });
        var user = scenario == "unauthorized-role"
            ? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "other-user") }, "Test"))
            : f.User.User;
        var overview = CreateOverviewPage(f.Db, user, payload);
        Assert.IsType<PageResult>(await overview.OnGetAsync(1, default));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(overview.PlanEdit));
    }

    // PO Timeline Increment 2a/2b: a refused PO post keeps what was typed, and the reopened
    // editor shows the server's messages inside the panel instead of behind it.
    [Fact]
    public async Task PoValidationFailure_ReopensTheEditorWithEntriesAndMessages()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft");
        var token = (await new PlanReadService(f.Db).GetAsync(1, f.User.UserId)).State.EditToken;
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.EditToken = token;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.FS, Name = "FS", PlannedStart = new DateOnly(2026, 3, 10), PlannedDue = new DateOnly(2026, 3, 1) }
        };
        var before = await PlannerStateAsync(f.Db);

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));

        Assert.Equal(before, await PlannerStateAsync(f.Db));
        var payload = Assert.IsType<string>(f.Page.TempData[PlanEditorRecovery.TempDataKey]);
        Assert.Contains("Start date cannot be after Due date", Assert.IsType<string>(f.Page.TempData["Error"]));

        var overview = CreateOverviewPage(f.Db, f.User.User, payload);
        overview.TempData["Error"] = f.Page.TempData["Error"];
        Assert.IsType<PageResult>(await overview.OnGetAsync(1, default));

        var restored = overview.PlanEdit;
        Assert.True(restored.RestoredSubmission);
        Assert.Equal(PlanEditorModes.Exact, restored.ActiveMode);
        var fs = restored.Exact.Rows.Single(row => row.Code == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 3, 10), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 3, 1), fs.PlannedDue);
        Assert.Contains(restored.SubmissionErrors, message => message == "For FS, Start date cannot be after Due date.");
        Assert.Single(restored.ErrorsFor("FS"));
        // The editor shows the messages itself, so the page alert behind it is dropped.
        Assert.False(overview.TempData.ContainsKey("Error"));
    }

    private static async Task AssertOverviewRecoversInputAsync(PlanEditInput input, string payload)
    {
        // A fresh GET rebuilds server authority/lifecycle state from persisted
        // data, then restores only this actor/project's submitted editor values.
        await using var f = await Fixture.CreateAsync("Admin", "SaveChanges");
        await SeedRecognizablePlannerStateAsync(f);
        await SeedPublishedExecutionAsync(f, true);
        var stage = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        stage.Status = StageStatus.Completed;
        stage.ActualStart = new DateOnly(2024, 1, 1);
        stage.CompletedOn = new DateOnly(2024, 1, 3);
        await f.Db.SaveChangesAsync();
        var fresh = await new PlanReadService(f.Db).GetAsync(1, "other-user");
        var persisted = await PlannerStateAsync(f.Db);
        var overview = CreateOverviewPage(f.Db, f.User.User, payload);
        Assert.IsType<PageResult>(await overview.OnGetAsync(1, default));
        var restored = overview.PlanEdit;
        Assert.Equal(string.Equals(input.Mode, PlanEditorModes.Exact, StringComparison.OrdinalIgnoreCase)
            ? PlanEditorModes.Exact : PlanEditorModes.Durations, restored.ActiveMode);
        Assert.Equal(JsonSerializer.Serialize(fresh.State), JsonSerializer.Serialize(restored.State));
        Assert.Equal(fresh.Exact.Rows.Select(r => (r.Code, r.Name, r.Status)), restored.Exact.Rows.Select(r => (r.Code, r.Name, r.Status)));
        Assert.Equal(fresh.Durations.Rows.Select(r => (r.Code, r.Name, r.Status, r.IsOptional)), restored.Durations.Rows.Select(r => (r.Code, r.Name, r.Status, r.IsOptional)));
        Assert.DoesNotContain(restored.Exact.Rows, r => r.Code == "UNKNOWN");
        Assert.DoesNotContain(restored.Durations.Rows, r => r.Code == "UNKNOWN");
        foreach (var submitted in input.Rows.Where(r => r.Code != "UNKNOWN"))
        {
            if (string.Equals(input.Mode, PlanEditorModes.Exact, StringComparison.OrdinalIgnoreCase))
            {
                var row = restored.Exact.Rows.Single(r => r.Code == submitted.Code);
                Assert.Equal(submitted.PlannedStart, row.PlannedStart);
                Assert.Equal(submitted.PlannedDue, row.PlannedDue);
            }
            else
            {
                Assert.Equal(submitted.DurationDays, restored.Durations.Rows.Single(r => r.Code == submitted.Code).DurationDays);
            }
        }
        if (string.Equals(input.Mode, PlanEditorModes.Durations, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(input.AnchorStart, restored.Durations.AnchorStart);
            Assert.Equal(input.IncludeWeekends, restored.Durations.IncludeWeekends);
            Assert.Equal(input.SkipHolidays, restored.Durations.SkipHolidays);
            Assert.Equal(input.NextStageStartPolicy, restored.Durations.NextStageStartPolicy);
        }
        Assert.Equal(persisted, await PlannerStateAsync(f.Db));
    }

    private static ProjectsOverviewModel CreateOverviewPage(ApplicationDbContext db, ClaimsPrincipal user, string payload)
    {
        var clock = new TestClock();
        var metadata = new WorkflowStageMetadataProvider();
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var users = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db), Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), services, NullLogger<UserManager<ApplicationUser>>.Instance);
        var audit = new NullAudit();
        var procurement = new ProjectProcurementReadService(db);
        var overview = new ProjectsOverviewModel(db, procurement,
            new ProjectTimelineReadService(db, clock, metadata, StageWorkflowTestFactory.CreatePolicy(db)),
            new HistoricalStageRecordService(db, metadata, clock, audit, NullLogger<HistoricalStageRecordService>.Instance),
            users, new PlanReadService(db), new PlanCompareService(db), NullLogger<ProjectsOverviewModel>.Instance, clock,
            new ProjectRemarksPanelService(users, clock, metadata), new ProjectLifecycleService(db, audit, clock),
            new PassThroughMarkdownRenderer(), new ProjectManagement.Services.Workspace.ProjectRecordHealthService(db, procurement),
            new ProjectProliferationProfileService(db, clock, audit), new IndustryPartnerService(db), new ThrowingProjectContentService());
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        overview.PageContext = new PageContext(new ActionContext(http, new RouteData(), new PageActionDescriptor()))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        overview.TempData = new TempDataDictionary(http, new TempProvider()) { ["PlanEditRecovery"] = payload };
        overview.Url = new SimpleUrlHelper(overview.PageContext);
        return overview;
    }

    private sealed class PassThroughMarkdownRenderer : IMarkdownRenderer
    {
        public string ToSafeHtml(string? markdown) => markdown ?? string.Empty;
    }

    private static void ConfigurePublication(EditPlanModel page, string mode, bool applicable, DateOnly anchor, int duration)
    {
        page.Input.Mode = mode;
        page.Input.AnchorStart = anchor;
        page.Input.IncludeWeekends = true;
        page.Input.SkipHolidays = false;
        page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        page.Input.Rows = new()
        {
            new() { Code = StageCodes.FS, Name = "FS", DurationDays = duration, PlannedStart = anchor, PlannedDue = anchor.AddDays(duration - 1) },
            new() { Code = StageCodes.PNC, Name = "PNC", DurationDays = applicable ? 2 : null,
                PlannedStart = applicable ? anchor.AddDays(duration) : null,
                PlannedDue = applicable ? anchor.AddDays(duration + 1) : null }
        };
    }

    private static async Task<string> AuditStateAsync(ApplicationDbContext db) =>
        JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Action, a.DataJson, a.UserId }).ToListAsync());

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task Publication_OmittedPncNeverInfersApplicabilityFromWorkingOrStageRows(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, false);
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 2, SortOrder = 80 });
        await f.Db.SaveChangesAsync();
        f.Page.Input.Rows.RemoveAll(r => r.Code == StageCodes.PNC);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False((await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.False((await policy.GetAsync(1)).PncApplicable);
    }

    [Theory]
    [InlineData(PlanEditorModes.Durations, true)]
    [InlineData(PlanEditorModes.Durations, false)]
    [InlineData(PlanEditorModes.Exact, true)]
    [InlineData(PlanEditorModes.Exact, false)]
    public async Task Publication_DuplicatePncRowsUseTheSameFinalSubmittedDecisionAsPlannerWrites(string mode, bool finalApplicable)
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, !finalApplicable);
        f.Page.Input.Mode = mode;
        var first = f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC);
        first.DurationDays = finalApplicable ? null : 2;
        first.PlannedStart = finalApplicable ? null : new DateOnly(2026, 10, 22);
        first.PlannedDue = finalApplicable ? null : new DateOnly(2026, 10, 23);
        f.Page.Input.Rows.Add(new PlanEditInputRow
        {
            Code = "pnc", Name = "PNC repeated", DurationDays = finalApplicable ? 2 : null,
            PlannedStart = finalApplicable ? new DateOnly(2026, 10, 22) : null,
            PlannedDue = finalApplicable ? new DateOnly(2026, 10, 23) : null
        });
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.Equal(finalApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var pnc = await f.Db.ProjectStages.AsNoTracking().SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(finalApplicable, pnc.PlannedStart.HasValue && pnc.PlannedDue.HasValue);
    }

    public static IEnumerable<object[]> NonAuthoritativePublicationCases()
    {
        foreach (var initial in new[] { true, false })
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var action in role == "Project Officer" ? new[] { "Autosave", "SaveDraft", "Calculate", "Submit" } : new[] { "Autosave", "SaveDraft", "Calculate" })
            yield return new object[] { role, action, initial };
    }

    [Theory]
    [MemberData(nameof(NonAuthoritativePublicationCases))]
    public async Task WorkingActions_NeverPublishExecutionApplicability(string role, string action, bool initiallyApplicable)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = initiallyApplicable ? null : 2;
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        var result = await f.Page.OnPostAsync(1, default);
        if (action == "Autosave") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        Assert.Equal(initiallyApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var working = await f.Db.PlanVersions.AsNoTracking().SingleAsync(p => p.VersionNo != 40);
        Assert.Equal(!initiallyApplicable, working.PncApplicable);
        Assert.Equal(action == "Submit" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, working.Status);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task DirectPublication_FailureRollsBackApplicabilityAndAllPlannerWrites(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, failAudit: true);
        await SeedPublishedExecutionAsync(f, true);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = null;
        var before = await PlannerStateAsync(f.Db);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.True((await policy.GetAsync(1)).PncApplicable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Page.OnPostAsync(1, default));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.True((await policy.GetAsync(1)).PncApplicable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approval_OuterRollbackKeepsPublishedApplicabilityAndPlanState(bool initiallyApplicable)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Submit", relational: true);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        var pending = new PlanVersion { ProjectId = 1, VersionNo = 41, CreatedByUserId = "po-user", SubmittedByUserId = "po-user", Status = PlanVersionStatus.PendingApproval, PncApplicable = !initiallyApplicable };
        pending.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC, PlannedStart = new DateOnly(2026, 10, 22), PlannedDue = new DateOnly(2026, 10, 23) });
        f.Db.PlanVersions.Add(pending);
        await f.Db.SaveChangesAsync();
        var before = await PlannerStateAsync(f.Db);
        var approval = new PlanApprovalService(f.Db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(f.Db), new NullNotifications());
        await using (var outer = await RelationalTransactionScope.CreateAsync(f.Db.Database))
        {
            Assert.True(await approval.ApproveVersionAsync(pending.Id, "hod-reviewer", false, true));
            Assert.Equal(!initiallyApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
            await outer.RollbackAsync();
        }
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(initiallyApplicable, (await new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider()).GetAsync(1)).PncApplicable);
    }

    private static async Task SeedPublishedExecutionAsync(Fixture f, bool applicable)
    {
        var project = await f.Db.Projects.SingleAsync();
        project.PncApplicable = applicable;
        project.ActivePlanVersionNo = 40;
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 40, CreatedByUserId = "earlier-po", Status = PlanVersionStatus.Approved, PncApplicable = applicable });
        f.Db.StageTemplates.Add(new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "PNC", Sequence = 80, Optional = true });
        // EAS's non-optional ancestry is already resolved. The only unresolved
        // predecessor under test is the conditional, optional PNC branch.
        var fs = await f.Db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.Status = StageStatus.Completed;
        fs.ActualStart = new DateOnly(2024, 1, 1);
        fs.CompletedOn = new DateOnly(2024, 1, 3);
        foreach (var code in new[] { StageCodes.IPA, StageCodes.SOW, StageCodes.AON, StageCodes.BID, StageCodes.TEC, StageCodes.BM })
        {
            f.Db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1, StageCode = code, Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 2)
            });
        }
        f.Db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.COB, Status = StageStatus.Completed, ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 2) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2024, 1, 3), PlannedDue = new DateOnly(2024, 1, 4) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.EAS, Status = StageStatus.NotStarted });
        f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC", DurationDays = applicable ? 2 : null });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    public static IEnumerable<object?[]> AllowedPlannerActions()
    {
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        {
            foreach (var action in new[] { "Autosave", "SaveDraft", "Calculate", role == "Project Officer" ? "Submit" : "SaveChanges" })
                yield return new object?[] { role, PlanEditorModes.Durations, action };
            foreach (var action in role == "Project Officer" ? new[] { "SaveDraft", "Submit" } : new[] { "SaveChanges" })
                yield return new object?[] { role, PlanEditorModes.Exact, action };
        }
        yield return new object?[] { "Project Officer", PlanEditorModes.Exact, " savedraft " };
        yield return new object?[] { "HoD", PlanEditorModes.Durations, " savechanges " };
        yield return new object?[] { "Admin", PlanEditorModes.Durations, "aUtOsAvE" };
    }

    [Theory]
    [MemberData(nameof(AllowedPlannerActions))]
    public async Task AllowedActionMatrix_PreservesPublicationIntent(string role, string mode, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var result = await f.Page.OnPostAsync(1, default);
        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction == "AUTOSAVE") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.SingleAsync();
        var publishes = normalizedAction == "SAVECHANGES";
        Assert.Equal(publishes ? new DateOnly(2026, 10, 19) : new DateOnly(2024, 1, 1), live.PlannedStart);
        Assert.Equal(publishes ? new DateOnly(2026, 10, 21) : new DateOnly(2024, 1, 3), live.PlannedDue);
        Assert.Equal(StageStatus.NotStarted, live.Status);
        var plan = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(normalizedAction == "SUBMIT" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, plan.Status);
        Assert.Equal(new DateOnly(2026, 10, 19), Assert.Single(plan.StagePlans).PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(plan.StagePlans).PlannedDue);
        if (normalizedAction == "SUBMIT")
        {
            Assert.Equal(f.User.UserId, plan.SubmittedByUserId);
            Assert.NotNull(plan.SubmittedOn);
        }
        else
        {
            Assert.Null(plan.SubmittedByUserId);
            Assert.Null(plan.SubmittedOn);
        }
    }

    public static IEnumerable<object?[]> RejectedPlannerActions()
    {
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var mode in new[] { PlanEditorModes.Durations, PlanEditorModes.Exact })
        {
            var allowed = mode == PlanEditorModes.Durations
                ? new[] { "Autosave", "SaveDraft", "Calculate", role == "Project Officer" ? "Submit" : "SaveChanges" }
                : role == "Project Officer" ? new[] { "SaveDraft", "Submit" } : new[] { "SaveChanges" };
            foreach (var action in new string?[] { "Autosave", "SaveDraft", "Calculate", "Submit", "SaveChanges", "Unknown", null, "", " \t ", "1", "Apply" })
                if (!allowed.Contains(action)) yield return new object?[] { role, mode, action };
        }
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var mode in new string?[] { "Unknown", "", null })
            yield return new object?[] { role, mode, role == "Project Officer" ? "SaveDraft" : "SaveChanges" };
    }

    [Theory]
    [MemberData(nameof(RejectedPlannerActions))]
    public async Task RejectedActionMatrix_PreservesAllSeededPlannerState(string role, string? mode, string? action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode!;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, action);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations, "Submit")]
    [InlineData("Admin", PlanEditorModes.Durations, "Submit")]
    [InlineData("HoD", PlanEditorModes.Exact, "SaveDraft")]
    [InlineData("Admin", PlanEditorModes.Exact, "Submit")]
    [InlineData("Project Officer", PlanEditorModes.Exact, "Unknown")]
    [InlineData("Project Officer", PlanEditorModes.Durations, null)]
    public async Task RejectedAction_CannotCreateDraftBeforeValidation(string role, string mode, string? action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f, ownDraft: false);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, action);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task DirectRoleAlsoAssignedAsPo_CannotSubmit(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "Submit");
        ((ClaimsIdentity)f.User.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Project Officer"));
        (await f.Db.Projects.SingleAsync()).LeadPoUserId = f.User.UserId;
        await f.Db.SaveChangesAsync();
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, "Submit");
    }

    private static void AssertRejectedPlannerRequest(EditPlanModel page, IActionResult result, string? action)
    {
        if (string.Equals(action?.Trim(), "Autosave", StringComparison.OrdinalIgnoreCase))
        {
            var json = AssertJson(result, 400, false);
            Assert.NotEmpty(json.GetProperty("errors").EnumerateArray());
        }
        else
        {
            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);
            Assert.Equal(1, redirect.RouteValues!["id"]);
            Assert.False(string.IsNullOrWhiteSpace(page.TempData["Error"] as string));
            Assert.Equal("plan-edit", page.TempData["OpenOffcanvas"]);
        }
    }

    private static async Task SeedRecognizablePlannerStateAsync(Fixture f, bool ownDraft = true)
    {
        (await f.Db.Projects.SingleAsync()).PublishedPlanningInputVersion = 1;
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 10, 5), IncludeWeekends = true,
            SkipHolidays = true, NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 9, SortOrder = 17 });
        var owner = ownDraft ? f.User.UserId! : "previous-owner";
        var plan = new PlanVersion
        {
            ProjectId = 1, VersionNo = 7, OwnerUserId = owner, CreatedByUserId = owner,
            CreatedOn = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Status = ownDraft ? PlanVersionStatus.Draft : PlanVersionStatus.Approved,
            AnchorStageCode = StageCodes.FS, AnchorDate = new DateOnly(2026, 10, 5),
            PncApplicable = false, SkipWeekends = false, TransitionRule = PlanTransitionRule.SameDay
        };
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, PlannedStart = new DateOnly(2026, 10, 6), PlannedDue = new DateOnly(2026, 10, 8), DurationDays = 3 });
        f.Db.PlanVersions.Add(plan);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<string> PlannerStateAsync(ApplicationDbContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            Published = await db.Projects.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.PncApplicable, p.PublishedPlanningInputVersion, p.ActivePlanVersionNo, p.RowVersion }).ToListAsync(),
            Settings = await db.ProjectScheduleSettings.AsNoTracking().OrderBy(s => s.ProjectId)
                .Select(s => new { s.ProjectId, s.AnchorStart, s.IncludeWeekends, s.SkipHolidays, s.NextStageStartPolicy }).ToListAsync(),
            Durations = await db.ProjectPlanDurations.AsNoTracking().OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.ProjectId, d.StageCode, d.DurationDays, d.SortOrder }).ToListAsync(),
            StagePlans = await db.StagePlans.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.PlanVersionId, s.StageCode, s.PlannedStart, s.PlannedDue, s.DurationDays, s.SortOrder }).ToListAsync(),
            Stages = await db.ProjectStages.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.ProjectId, s.StageCode, s.PlannedStart, s.PlannedDue, s.Status, s.ActualStart, s.CompletedOn, s.SortOrder }).ToListAsync(),
            Plans = await db.PlanVersions.AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.ProjectId, p.VersionNo, p.Title, p.OwnerUserId, p.CreatedByUserId, p.CreatedOn, p.Status,
                    p.SubmittedByUserId, p.SubmittedOn, p.ApprovedByUserId, p.ApprovedOn, p.RejectedByUserId, p.RejectedOn, p.RejectionNote,
                    p.AnchorStageCode, p.AnchorDate, p.SkipWeekends, p.SkipHolidays, p.TransitionRule, p.PncApplicable, p.PlanningInputVersion }).ToListAsync(),
            ApprovalLogs = await db.PlanApprovalLogs.AsNoTracking().OrderBy(l => l.Id).Select(l => l.Id).ToListAsync()
        });
    }

    [Theory]
    [InlineData("HoD", null)]
    [InlineData("HoD", 0)]
    [InlineData("Admin", null)]
    [InlineData("Admin", 0)]
    public async Task LeadingClearedStage_DurationApplyAndExactRoundTripKeepLiveAndDraftAligned(string role, int? leadingDuration)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        var fs = await f.Db.ProjectStages.SingleAsync();
        fs.Status = StageStatus.Skipped;
        f.Page.Input.Rows[0].DurationDays = leadingDuration;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "IPA", Sequence = 20 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.SOW, Name = "SOW", Sequence = 30 });
        f.Page.Input.Rows.AddRange(new[]
        {
            new PlanEditInputRow { Code = StageCodes.IPA, Name = "IPA", DurationDays = 2 },
            new PlanEditInputRow { Code = StageCodes.SOW, Name = "SOW", DurationDays = 2 }
        });
        await f.Db.SaveChangesAsync();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var draftIpa = await f.Db.StagePlans.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2026, 10, 19), draftIpa.PlannedStart);
        // Calculate is still non-authoritative, including for the skipped FS.
        Assert.Equal(new DateOnly(2024, 1, 1), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).PlannedStart);
        f.Page.Input.Action = "SaveChanges";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        Assert.Equal(new DateOnly(2026, 10, 19), live[StageCodes.IPA].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 20), live[StageCodes.IPA].PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 21), live[StageCodes.SOW].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 22), live[StageCodes.SOW].PlannedDue);
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        foreach (var code in new[] { StageCodes.IPA, StageCodes.SOW })
        {
            var preview = view.Durations.Rows.Single(r => r.Code == code);
            var exact = view.Exact.Rows.Single(r => r.Code == code);
            Assert.Equal(live[code].PlannedStart, preview.PreviewStart);
            Assert.Equal(live[code].PlannedDue, preview.PreviewDue);
            Assert.Equal(live[code].PlannedStart, exact.PlannedStart);
            Assert.Equal(live[code].PlannedDue, exact.PlannedDue);
        }
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = view.Exact.Rows.Select(r => new PlanEditInputRow
        {
            Code = r.Code, Name = r.Name, PlannedStart = r.PlannedStart, PlannedDue = r.PlannedDue
        }).ToList();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var afterExact = await f.Db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        foreach (var code in new[] { StageCodes.IPA, StageCodes.SOW })
        {
            Assert.Equal(live[code].PlannedStart, afterExact[code].PlannedStart);
            Assert.Equal(live[code].PlannedDue, afterExact[code].PlannedDue);
        }
        Assert.Null(afterExact[StageCodes.FS].PlannedStart);
        Assert.Null(afterExact[StageCodes.FS].PlannedDue);
        Assert.Equal(StageStatus.Skipped, afterExact[StageCodes.FS].Status);
        Assert.Null(afterExact[StageCodes.FS].ActualStart);
        Assert.Null(afterExact[StageCodes.FS].CompletedOn);
    }

    [Theory]
    [InlineData("HoD", null)]
    [InlineData("HoD", 0)]
    [InlineData("HoD", -1)]
    [InlineData("Admin", null)]
    [InlineData("Admin", 0)]
    [InlineData("Admin", -1)]
    public async Task DurationPnc_PositiveThenCleared_DoesNotResurrectOnExactSave(string role, int? removedDuration)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await AddOptionalPncAsync(f);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.True(draft.PncApplicable);
        Assert.NotNull(draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC).PlannedDue);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = removedDuration;
        f.Page.Input.Action = "SaveChanges";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();

        draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.False(draft.PncApplicable);
        var pncPlan = draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pncPlan.PlannedStart);
        Assert.Null(pncPlan.PlannedDue);
        Assert.Equal(0, pncPlan.DurationDays);
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        var pncPreview = view.Durations.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.Null(pncPreview.PreviewStart);
        Assert.Null(pncPreview.PreviewDue);
        var pncExact = view.Exact.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.Null(pncExact.PlannedStart);
        Assert.Null(pncExact.PlannedDue);

        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = view.Exact.Rows.Select(r => new PlanEditInputRow
        {
            Code = r.Code, Name = r.Name, PlannedStart = r.PlannedStart, PlannedDue = r.PlannedDue
        }).ToList();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Null((await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC)).DurationDays);
        Assert.Equal(StageStatus.Completed, pnc.Status);
        Assert.Equal(new DateOnly(2025, 1, 1), pnc.ActualStart);
        Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task PoDurationPnc_PositiveThenCleared_SubmitAndApproveDoesNotPublishOldDates(int? removedDuration)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Calculate");
        await AddOptionalPncAsync(f);
        await f.Page.OnPostAsync(1, default);
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.NotNull(draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC).PlannedDue);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = removedDuration;
        f.Page.Input.Action = "Submit";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var pending = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.False(pending.PncApplicable);
        var pncPlan = pending.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pncPlan.PlannedStart);
        Assert.Null(pncPlan.PlannedDue);
        Assert.Equal(0, pncPlan.DurationDays);
        // PO submission cannot erase the already-published plan before approval.
        Assert.Equal(new DateOnly(2024, 1, 2), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).PlannedDue);
        var approval = new PlanApprovalService(f.Db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(f.Db), new NullNotifications());
        Assert.True(await approval.ApproveVersionAsync(pending.Id, "hod-reviewer", false, true));
        f.Db.ChangeTracker.Clear();
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Equal(StageStatus.Completed, pnc.Status);
        Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
    }

    private static async Task AddOptionalPncAsync(Fixture f)
    {
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "PNC", Sequence = 80, Optional = true
        });
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.Completed,
            PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2),
            ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2)
        });
        f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC", DurationDays = 2 });
        await f.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Project Officer", "Autosave")]
    [InlineData("HoD", "Autosave")]
    [InlineData("Admin", "Autosave")]
    [InlineData("Project Officer", "Calculate")]
    [InlineData("HoD", "Calculate")]
    [InlineData("Admin", "Calculate")]
    [InlineData("Project Officer", "SaveDraft")]
    [InlineData("HoD", "SaveDraft")]
    [InlineData("Admin", "SaveDraft")]
    public async Task WorkingActions_NeverPublishLiveDates(string role, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        var result = await f.Page.OnPostAsync(1, default);
        if (action == "Autosave") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        await f.AssertLiveDatesUnchangedAsync();
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.Draft, draft.Status);
        Assert.Equal(new DateOnly(2026, 10, 19), Assert.Single(draft.StagePlans).PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(draft.StagePlans).PlannedDue);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task ExplicitSaveChanges_PreservesDirectAuthority(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges");
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var live = await f.Db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 19), live.PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), live.PlannedDue);
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task CalculateThenExplicitApply_RefreshesPreviewWithoutHidingNewOfficialDates(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await f.Page.OnPostAsync(1, default);
        await f.AssertLiveDatesUnchangedAsync();
        f.Page.Input.Action = "SaveChanges";
        f.Page.Input.Rows[0].DurationDays = 4;
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.SingleAsync();
        var preview = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(new DateOnly(2026, 10, 22), live.PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 22), preview.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 22), preview.Durations.Rows.Single(r => r.Code == StageCodes.FS).PreviewDue);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DurationPreviewThenExactSave_DoesNotReloadOrRepublishStaleDraftDates(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await f.Page.OnPostAsync(1, default);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Action = "SaveChanges";
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 11, 2);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 11, 4);
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        var row = view.Exact.Rows.Single(r => r.Code == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 11, 2), row.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 4), row.PlannedDue);
        f.Page.Input.Rows[0].PlannedStart = row.PlannedStart;
        f.Page.Input.Rows[0].PlannedDue = row.PlannedDue;
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(new DateOnly(2026, 11, 4), (await f.Db.ProjectStages.SingleAsync()).PlannedDue);
    }

    [Theory]
    [InlineData("HoD", "Autosave")]
    [InlineData("Admin", "Autosave")]
    [InlineData("HoD", "Calculate")]
    [InlineData("Admin", "Calculate")]
    public async Task WorkingActions_DoNotMaterialiseOfficialStages(string role, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        f.Db.ProjectStages.RemoveRange(f.Db.ProjectStages);
        await f.Db.SaveChangesAsync();
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.ProjectStages.ToListAsync());
        Assert.NotEmpty(await f.Db.StagePlans.ToListAsync());
    }

    [Fact]
    public async Task PoSubmit_SubmitsGeneratedDraftWithoutPublishing()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Submit");
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var plan = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.PendingApproval, plan.Status);
        Assert.Equal("po-user", plan.SubmittedByUserId);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(plan.StagePlans).PlannedDue);
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task InvalidAutosave_ReturnsJsonFailureAndDoesNotPersist(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Autosave");
        f.Page.Input.Rows[0].DurationDays = 0;
        var json = AssertJson(await f.Page.OnPostAsync(1, default), 400, false);
        Assert.NotEmpty(json.GetProperty("errors").EnumerateArray());
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task ModelBindingErrors_AreJsonAutosaveFailures()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        f.Page.ModelState.AddModelError("Input.Rows[0].DurationDays", "The value is invalid.");
        AssertJson(await f.Page.OnPostAsync(1, default), 400, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Unassigned PO")]
    public async Task UnauthorisedAutosave_CannotMutate(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Autosave");
        AssertJson(await f.Page.OnPostAsync(1, default), 403, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task PoCannotForgeDirectSaveIntent()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveChanges");
        AssertRejectedPlannerRequest(f.Page, await f.Page.OnPostAsync(1, default), "SaveChanges");
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task LegacyAjaxSaveDraft_DoesNotDirectApplyAndReturnsJson(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveDraft");
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var before = await PublishedStateAsync(f.Db);
        AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        Assert.Equal(before, await PublishedStateAsync(f.Db));
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task PendingOwner_AutosaveLeavesReviewActiveUntilExplicitMaterialSave()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        var submitted = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, Title = PlanVersion.ProjectTimelineTitle,
            OwnerUserId = "po-user", CreatedByUserId = "po-user",
            SubmittedByUserId = "po-user", SubmittedOn = DateTimeOffset.UtcNow.AddHours(-1),
            Status = PlanVersionStatus.PendingApproval
        };
        f.Db.PlanVersions.Add(submitted);
        await f.Db.SaveChangesAsync();
        AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        Assert.Equal(PlanVersionStatus.PendingApproval, submitted.Status);
        var successor = await f.Db.PlanVersions.SingleAsync(p => p.VersionNo == 2);
        Assert.Equal(PlanVersionStatus.Draft, successor.Status);
        Assert.Equal("po-user", successor.OwnerUserId);
        Assert.Empty(await f.Db.PlanApprovalLogs.ToListAsync());

        // Pre-C2 cached clients sent AJAX SaveDraft instead of Autosave.
        // This compatibility path must also leave HoD's review actionable.
        f.Page.Input.Action = "SaveDraft";
        f.Page.HttpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        f.Page.Input.Rows[0].DurationDays += 2;
        AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        Assert.Equal(PlanVersionStatus.PendingApproval, submitted.Status);
        Assert.Empty(await f.Db.PlanApprovalLogs.ToListAsync());

        f.Page.HttpContext.Request.Headers.Remove("X-Requested-With");
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.Equal(PlanVersionStatus.Superseded, submitted.Status);
        // The PO is told the HoD review ended, not merely "Draft saved." (C2.2).
        Assert.Equal(EditPlanModel.DraftSavedSupersededMessage, f.Page.TempData["Flash"]);
        Assert.Single(await f.Db.PlanApprovalLogs.Where(l => l.PlanVersionId == submitted.Id &&
            l.Action == "Superseded").ToListAsync());
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task C2_StaleDurationTabCannotOverwriteAnotherSavedRevision()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft", relational: true);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var saved = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        var previousToken = PlanEditFingerprint.Compute(saved);

        f.Page.Input.Action = "Autosave";
        f.Page.Input.EditToken = previousToken;
        f.Page.Input.Rows[0].DurationDays = 5;
        var successful = AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        var nextToken = successful.GetProperty("editToken").GetString();
        Assert.NotNull(nextToken);
        Assert.NotEqual(previousToken, nextToken);

        f.Page.Input.Rows[0].DurationDays = 8;
        AssertJson(await f.Page.OnPostAsync(1, default), 409, false);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(5, (await f.Db.StagePlans.SingleAsync()).DurationDays);

        f.Page.Input.EditToken = nextToken;
        AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(8, (await f.Db.StagePlans.SingleAsync()).DurationDays);
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task PendingOwner_ExactSaveDraftThatSupersedesTheReviewSaysSo()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft", relational: true);
        var submitted = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, Title = PlanVersion.ProjectTimelineTitle,
            OwnerUserId = "po-user", CreatedByUserId = "po-user",
            SubmittedByUserId = "po-user", SubmittedOn = DateTimeOffset.UtcNow.AddHours(-1),
            Status = PlanVersionStatus.PendingApproval
        };
        submitted.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS, SortOrder = 10,
            PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 3)
        });
        f.Db.PlanVersions.Add(submitted);
        await f.Db.SaveChangesAsync();
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.FS, Name = "FS", PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 9) }
        };

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));

        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(PlanVersionStatus.Superseded, (await f.Db.PlanVersions.SingleAsync(p => p.Id == submitted.Id)).Status);
        Assert.Equal(EditPlanModel.DraftSavedSupersededMessage, f.Page.TempData["Flash"]);
    }

    [Fact]
    public async Task ExactSaveDraftWithoutAPendingSubmission_KeepsThePlainConfirmation()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft", relational: true);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = new()
        {
            new() { Code = StageCodes.FS, Name = "FS", PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 9) }
        };

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));

        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal("Draft saved.", f.Page.TempData["Flash"]);
    }

    [Theory]
    [InlineData("mismatched-project", 400)]
    [InlineData("missing-project", 404)]
    [InlineData("missing-user", 403)]
    [InlineData("wrong-mode", 400)]
    [InlineData("invalid-policy", 400)]
    public async Task RejectedAutosave_HasMachineReadableFailure(string scenario, int status)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        var id = 1;
        if (scenario == "mismatched-project") f.Page.Input.ProjectId = 2;
        if (scenario == "missing-project") id = f.Page.Input.ProjectId = 2;
        if (scenario == "missing-user") f.User.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (scenario == "wrong-mode") f.Page.Input.Mode = PlanEditorModes.Exact;
        if (scenario == "invalid-policy") f.Page.Input.NextStageStartPolicy = "invalid";
        AssertJson(await f.Page.OnPostAsync(id, default), status, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    public static IEnumerable<object[]> PrivateDurationActions()
    {
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var action in role == "Project Officer"
            ? new[] { "Autosave", "SaveDraft", "Calculate", "Submit" }
            : new[] { "Autosave", "SaveDraft", "Calculate" })
            yield return new object[] { role, action };
    }

    [Theory]
    [MemberData(nameof(PrivateDurationActions))]
    public async Task PrivateDurationAction_PreservesEveryPublishedInputAndOtherActorProposal(string role, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f);
        var otherPlan = new PlanVersion
        {
            ProjectId = 1, VersionNo = 8, OwnerUserId = "unrelated-owner", CreatedByUserId = "unrelated-owner",
            Status = PlanVersionStatus.PendingApproval, AnchorDate = new DateOnly(2027, 2, 1),
            SkipWeekends = false, SkipHolidays = true, TransitionRule = PlanTransitionRule.SameDay,
            PncApplicable = true
        };
        otherPlan.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 21, SortOrder = 60,
            PlannedStart = new DateOnly(2027, 2, 1), PlannedDue = new DateOnly(2027, 2, 21) });
        // Submission needs an empty pending slot; the approved pointer is part of
        // the immutable published snapshot in every case.
        if (action == "Submit") otherPlan.Status = PlanVersionStatus.Draft;
        f.Db.PlanVersions.Add(otherPlan);
        (await f.Db.Projects.SingleAsync()).ActivePlanVersionNo = 6;
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 6, OwnerUserId = "approved-owner",
            CreatedByUserId = "approved-owner", Status = PlanVersionStatus.Approved });
        await f.Db.SaveChangesAsync();
        var before = await PublishedStateAsync(f.Db);
        var otherBefore = await OtherProposalStateAsync(f.Db, f.User.UserId!);
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 10);
        f.Page.Input.IncludeWeekends = false;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Page.Input.Rows[0].DurationDays = 12;

        var result = await f.Page.OnPostAsync(1, default);
        if (action == "Autosave") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal(before, await PublishedStateAsync(f.Db));
        Assert.Equal(otherBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
        var proposal = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == f.User.UserId);
        Assert.Equal(action == "Submit" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, proposal.Status);
        Assert.Equal(f.Page.Input.AnchorStart, proposal.AnchorDate);
        Assert.True(proposal.SkipWeekends);
        Assert.False(proposal.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, proposal.TransitionRule);
        Assert.Equal(12, proposal.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        var own = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(f.Page.Input.AnchorStart, own.Durations.AnchorStart);
        Assert.False(own.Durations.SkipHolidays);
        Assert.Equal(12, own.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.False(own.State.IsLocked);
        var outsider = await new PlanReadService(f.Db).GetAsync(1, "no-proposal-user");
        Assert.Equal(new DateOnly(2026, 10, 5), outsider.Durations.AnchorStart);
        Assert.True(outsider.Durations.SkipHolidays);
        Assert.Equal(9, outsider.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2024, 1, 1), outsider.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
    }

    [Fact]
    public async Task IndependentActorDraftsAndDiscard_ReloadOnlyTheirOwnInputsThenPublishedBaseline()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Calculate");
        await SeedRecognizablePlannerStateAsync(f);
        await AddOptionalPncAsync(f);
        var before = await PublishedStateAsync(f.Db);
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 10);
        f.Page.Input.Rows[0].DurationDays = 12;
        await f.Page.OnPostAsync(1, default);
        var poBefore = await OtherProposalStateAsync(f.Db, "hod-user");
        f.User.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, "hod-user"), new Claim(ClaimTypes.Role, "HoD") }, "Test"));
        f.Page.Input.Action = "SaveDraft";
        f.Page.Input.AnchorStart = new DateOnly(2026, 12, 3);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = true;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        f.Page.Input.Rows[0].DurationDays = 4;
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = 0;
        await f.Page.OnPostAsync(1, default);
        Assert.Equal(poBefore, await OtherProposalStateAsync(f.Db, "hod-user"));
        Assert.Equal(before, await PublishedStateAsync(f.Db));
        var po = await new PlanReadService(f.Db).GetAsync(1, "po-user");
        var hod = await new PlanReadService(f.Db).GetAsync(1, "hod-user");
        Assert.Equal(new DateOnly(2026, 11, 10), po.Durations.AnchorStart);
        Assert.False(po.Durations.IncludeWeekends);
        Assert.False(po.Durations.SkipHolidays);
        Assert.Equal(12, po.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.True(po.State.PncApplicable);
        Assert.Equal(new DateOnly(2026, 12, 3), hod.Durations.AnchorStart);
        Assert.True(hod.Durations.IncludeWeekends);
        Assert.True(hod.Durations.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.SameDay, hod.Durations.NextStageStartPolicy);
        Assert.Equal(4, hod.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.False(hod.State.PncApplicable);
        await f.Page.OnPostDeleteDraftAsync(1, default);
        Assert.Equal(before, await PublishedStateAsync(f.Db));
        Assert.Equal(poBefore, await OtherProposalStateAsync(f.Db, "hod-user"));
        Assert.False(await f.Db.PlanVersions.AnyAsync(p => p.OwnerUserId == "hod-user"));
        Assert.False(await f.Db.StagePlans.AnyAsync(s => s.PlanVersion!.OwnerUserId == "hod-user"));
        hod = await new PlanReadService(f.Db).GetAsync(1, "hod-user");
        Assert.Equal(new DateOnly(2026, 10, 5), hod.Durations.AnchorStart);
        Assert.Equal(9, hod.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2024, 1, 1), hod.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DirectDurationSave_SynchronizesOnlyExistingActorDraftWithAllPublishedInputs(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges");
        await SeedRecognizablePlannerStateAsync(f);
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 8, OwnerUserId = "other-author",
            CreatedByUserId = "other-author", Status = PlanVersionStatus.Draft, AnchorDate = new DateOnly(2028, 1, 1),
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 27, SortOrder = 42 } } });
        await f.Db.SaveChangesAsync();
        var otherBefore = await OtherProposalStateAsync(f.Db, f.User.UserId!);
        f.Page.Input.AnchorStart = new DateOnly(2026, 11, 10);
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.Equal(otherBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
        var own = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == f.User.UserId);
        Assert.Equal(f.Page.Input.AnchorStart, own.AnchorDate);
        Assert.False(own.SkipWeekends);
        Assert.False(own.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, own.TransitionRule);
        Assert.Equal(3, own.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        var settings = await f.Db.ProjectScheduleSettings.SingleAsync();
        Assert.Equal(own.AnchorDate, settings.AnchorStart);
        Assert.Equal(!own.SkipWeekends, settings.IncludeWeekends);
        Assert.Equal(own.SkipHolidays, settings.SkipHolidays);
        var live = await f.Db.ProjectStages.SingleAsync();
        Assert.Equal(live.PlannedStart, own.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(live.PlannedDue, own.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedDue);
    }

    [Theory]
    [InlineData("HoD", true, false, 2, 4)]
    [InlineData("Admin", true, false, 2, 4)]
    [InlineData("HoD", false, true, 1, 2)]
    [InlineData("Admin", false, true, 1, 2)]
    public async Task DirectExactSave_PreservesSeparatePublishedAndActorProposalCalendars(
        string role, bool proposalIncludesWeekends, bool holidayOnMonday, int publishedDuration, int proposalDuration)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges");
        await SeedRecognizablePlannerStateAsync(f);
        var settings = await f.Db.ProjectScheduleSettings.SingleAsync();
        settings.IncludeWeekends = false;
        settings.SkipHolidays = true;
        var ownDraft = await f.Db.PlanVersions.SingleAsync();
        ownDraft.AnchorDate = new DateOnly(2026, 10, 23); // Friday
        ownDraft.SkipWeekends = !proposalIncludesWeekends;
        ownDraft.SkipHolidays = false;
        if (holidayOnMonday)
        {
            f.Db.Holidays.Add(new Holiday { Date = new DateOnly(2026, 10, 26),
                Name = "Office holiday", Type = HolidayType.Gazetted });
        }
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 8,
            OwnerUserId = "other-author", CreatedByUserId = "other-author", Status = PlanVersionStatus.Draft,
            AnchorDate = new DateOnly(2027, 2, 1), SkipWeekends = true, SkipHolidays = true,
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 21, SortOrder = 70,
                PlannedStart = new DateOnly(2027, 2, 1), PlannedDue = new DateOnly(2027, 3, 1) } } });
        await f.Db.SaveChangesAsync();
        var otherBefore = await OtherProposalStateAsync(f.Db, f.User.UserId!);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 23);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 26); // Monday

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(publishedDuration, (await f.Db.ProjectPlanDurations.SingleAsync()).DurationDays);
        var publishedStage = await f.Db.ProjectStages.SingleAsync();
        Assert.Equal(f.Page.Input.Rows[0].PlannedStart, publishedStage.PlannedStart);
        Assert.Equal(f.Page.Input.Rows[0].PlannedDue, publishedStage.PlannedDue);
        ownDraft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == f.User.UserId);
        Assert.Equal(proposalDuration, Assert.Single(ownDraft.StagePlans).DurationDays);
        Assert.Equal(new DateOnly(2026, 10, 23), ownDraft.AnchorDate);
        Assert.Equal(!proposalIncludesWeekends, ownDraft.SkipWeekends);
        Assert.False(ownDraft.SkipHolidays);
        Assert.Equal(PlanTransitionRule.SameDay, ownDraft.TransitionRule);
        Assert.Equal(otherBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
        settings = await f.Db.ProjectScheduleSettings.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 5), settings.AnchorStart);
        Assert.False(settings.IncludeWeekends);
        Assert.True(settings.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.SameDay, settings.NextStageStartPolicy);

        var publishedAfterExact = await PublishedStateAsync(f.Db);
        var editor = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        f.Page.Input.Mode = PlanEditorModes.Durations;
        f.Page.Input.Action = "Calculate";
        f.Page.Input.AnchorStart = editor.Durations.AnchorStart;
        f.Page.Input.IncludeWeekends = editor.Durations.IncludeWeekends;
        f.Page.Input.SkipHolidays = editor.Durations.SkipHolidays;
        f.Page.Input.NextStageStartPolicy = editor.Durations.NextStageStartPolicy;
        f.Page.Input.Rows[0].DurationDays = editor.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays;
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        editor = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        var recalculated = editor.Exact.Rows.Single(r => r.Code == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 10, 23), recalculated.PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 26), recalculated.PlannedDue);
        Assert.Equal(publishedAfterExact, await PublishedStateAsync(f.Db));
        Assert.Equal(otherBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
    }

    [Theory]
    [InlineData("SaveDraft", PlanVersionStatus.Draft)]
    [InlineData("Submit", PlanVersionStatus.PendingApproval)]
    public async Task PrivateExact_NewProposalWithoutPublishedSettings_ReloadsExactInputsAndMode(string action, PlanVersionStatus status)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", action);
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        var publishedBefore = await PublishedStateAsync(f.Db);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.AnchorStart = null;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 11, 2);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 11, 5);

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(PlanEditorModes.Exact, view.ActiveMode);
        Assert.Null(view.Durations.AnchorStart);
        var row = view.Exact.Rows.Single(r => r.Code == StageCodes.FS);
        Assert.Equal(f.Page.Input.Rows[0].PlannedStart, row.PlannedStart);
        Assert.Equal(f.Page.Input.Rows[0].PlannedDue, row.PlannedDue);
        Assert.Equal(status, view.State.Status);
        Assert.False(view.State.IsLocked);
        Assert.Equal(action == "SaveDraft", view.State.HasMyDraft);
        var proposal = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(status, proposal.Status);
        Assert.Equal(f.User.UserId, proposal.OwnerUserId);
        Assert.Null(proposal.AnchorDate);
        var stage = Assert.Single(proposal.StagePlans);
        Assert.Equal(row.PlannedStart, stage.PlannedStart);
        Assert.Equal(row.PlannedDue, stage.PlannedDue);
        Assert.Equal(4, stage.DurationDays);
        if (action == "Submit")
        {
            Assert.Equal(f.User.UserId, proposal.SubmittedByUserId);
            Assert.NotNull(proposal.SubmittedOn);
        }
        Assert.Equal(publishedBefore, await PublishedStateAsync(f.Db));
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
    }

    [Fact]
    public async Task PrivateExact_UsesActorDraftCalendarAndPreservesExplicitClearedDatesOnReload()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft");
        await SeedRecognizablePlannerStateAsync(f);
        var draft = await f.Db.PlanVersions.SingleAsync();
        draft.SkipWeekends = true;
        draft.SkipHolidays = false;
        await f.Db.SaveChangesAsync();
        var publishedBefore = await PublishedStateAsync(f.Db);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 23);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 26);
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(2, (await f.Db.StagePlans.SingleAsync()).DurationDays);
        f.Page.Input.Rows[0].PlannedStart = null;
        f.Page.Input.Rows[0].PlannedDue = null;
        await f.Page.OnPostAsync(1, default);
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Null(view.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.Null(view.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedDue);
        Assert.Equal(publishedBefore, await PublishedStateAsync(f.Db));
    }

    private static async Task<string> PublishedStateAsync(ApplicationDbContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new {
            Projects = await db.Projects.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.PncApplicable, p.PublishedPlanningInputVersion, p.ActivePlanVersionNo, p.RowVersion }).ToListAsync(),
            Settings = await db.ProjectScheduleSettings.AsNoTracking().OrderBy(s => s.ProjectId).Select(s => new { s.ProjectId, s.AnchorStart, s.IncludeWeekends, s.SkipHolidays, s.NextStageStartPolicy }).ToListAsync(),
            Durations = await db.ProjectPlanDurations.AsNoTracking().OrderBy(d => d.Id).Select(d => new { d.Id, d.StageCode, d.DurationDays, d.SortOrder }).ToListAsync(),
            Stages = await db.ProjectStages.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.StageCode, s.PlannedStart, s.PlannedDue, s.Status, s.ActualStart, s.CompletedOn, s.SortOrder }).ToListAsync(),
            Approved = await db.PlanVersions.AsNoTracking().Where(p => p.Status == PlanVersionStatus.Approved).OrderBy(p => p.Id).Select(p => new { p.Id, p.VersionNo, p.Status, p.ApprovedOn, p.ApprovedByUserId }).ToListAsync()
        });
    }

    private static async Task<string> OtherProposalStateAsync(ApplicationDbContext db, string actor)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(await db.PlanVersions.AsNoTracking().Where(p => p.OwnerUserId != actor).OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Status, p.OwnerUserId, p.AnchorDate, p.SkipWeekends, p.SkipHolidays, p.TransitionRule, p.PncApplicable,
                Stages = p.StagePlans.OrderBy(s => s.Id).Select(s => new { s.Id, s.StageCode, s.DurationDays, s.SortOrder, s.PlannedStart, s.PlannedDue }).ToList() }).ToListAsync());
    }

    private static JsonElement AssertJson(IActionResult result, int status, bool ok)
    {
        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(status, json.StatusCode ?? 200);
        var data = JsonSerializer.SerializeToElement(json.Value);
        Assert.Equal(ok, data.GetProperty("ok").GetBoolean());
        if (ok) Assert.True(DateTimeOffset.TryParse(data.GetProperty("savedAt").GetString(), out _));
        else Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("message").GetString()));
        return data;
    }

    [Theory]
    [InlineData(PlanEditorModes.Exact, "SaveDraft", false)]
    [InlineData(PlanEditorModes.Exact, "SaveDraft", true)]
    [InlineData(PlanEditorModes.Durations, "SaveDraft", false)]
    [InlineData(PlanEditorModes.Durations, "Calculate", false)]
    [InlineData(PlanEditorModes.Durations, "SaveDraft", true)]
    public async Task Cutover_FalsePncStaleInputsRequireAnExplicitNewProposalDecision(string mode, string action, bool explicitlyEnable)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", action, relational: true);
        await SeedRecognizablePlannerStateAsync(f);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = 0;
        project.PncApplicable = false;
        f.Db.PlanVersions.RemoveRange(await f.Db.PlanVersions.ToListAsync());
        await AddOptionalPncAsync(f);
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 18 });
        f.Page.Input.Rows.RemoveAll(row => row.Code == StageCodes.PNC);
        var stale = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        stale.PlannedStart = new DateOnly(2026, 8, 3);
        stale.PlannedDue = new DateOnly(2026, 8, 20);
        await f.Db.SaveChangesAsync();
        var editor = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        f.Page.Input.Mode = mode;
        var fs = f.Page.Input.Rows.Single(r => r.Code == StageCodes.FS);
        fs.PlannedStart = new DateOnly(2026, 11, 2);
        fs.PlannedDue = new DateOnly(2026, 11, 4);
        f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC",
            DurationDays = explicitlyEnable ? 2 : editor.Durations.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays,
            PlannedStart = explicitlyEnable ? new DateOnly(2026, 11, 5) : editor.Exact.Rows.Single(r => r.Code == StageCodes.PNC).PlannedStart,
            PlannedDue = explicitlyEnable ? new DateOnly(2026, 11, 6) : editor.Exact.Rows.Single(r => r.Code == StageCodes.PNC).PlannedDue });
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(1, draft.PlanningInputVersion);
        Assert.Equal(explicitlyEnable, draft.PncApplicable);
        var pnc = draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        if (!explicitlyEnable) { Assert.Equal(0, pnc.DurationDays); Assert.Null(pnc.PlannedStart); Assert.Null(pnc.PlannedDue); }
        Assert.False((await f.Db.Projects.SingleAsync()).PncApplicable);
        Assert.Equal(new DateOnly(2026, 8, 3), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).PlannedStart);
    }

    [Theory]
    [InlineData("SaveDraft", false, true, false)]
    [InlineData("Calculate", false, true, false)]
    [InlineData("SaveDraft", true, true, false)]
    [InlineData("Calculate", true, true, false)]
    [InlineData("SaveDraft", true, false, false)]
    [InlineData("SaveDraft", false, true, true)]
    public async Task Cutover_LegacyDurationPostPreservesPrivateInputsAndOmittedRowsWithoutPublishing(
        string action, bool livePnc, bool proposalPnc, bool explicitlyEnablePnc)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", action, relational: true);
        await SeedRecognizablePlannerStateAsync(f);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = PlanningInputVersions.Legacy;
        project.PncApplicable = livePnc;
        var legacy = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        legacy.PlanningInputVersion = PlanningInputVersions.Legacy;
        legacy.AnchorDate = new DateOnly(2035, 1, 1);
        legacy.SkipWeekends = false;
        legacy.SkipHolidays = false; // This legacy field must come from the accepted safe baseline.
        legacy.TransitionRule = PlanTransitionRule.SameDay;
        legacy.PncApplicable = proposalPnc;
        var fs = legacy.StagePlans.Single(s => s.StageCode == StageCodes.FS);
        fs.DurationDays = 99;
        fs.PlannedStart = new DateOnly(2035, 1, 1);
        fs.PlannedDue = new DateOnly(2035, 4, 9);
        legacy.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC, DurationDays = 15,
            PlannedStart = new DateOnly(2035, 4, 9), PlannedDue = new DateOnly(2035, 4, 23) });
        legacy.StagePlans.Add(new StagePlan { StageCode = "PRIVATE", DurationDays = 45, SortOrder = 90,
            PlannedStart = new DateOnly(2035, 4, 23), PlannedDue = new DateOnly(2035, 6, 6) });
        f.Db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC,
            PlannedStart = new DateOnly(2024, 2, 1), PlannedDue = new DateOnly(2024, 2, 15) });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = "SHARED-ONLY", DurationDays = 77 });
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, OwnerUserId = "someone-else", CreatedByUserId = "someone-else",
            VersionNo = 8, PlanningInputVersion = PlanningInputVersions.Legacy, Status = PlanVersionStatus.Draft,
            AnchorDate = new DateOnly(2040, 1, 1), StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 88 } } });
        await f.Db.SaveChangesAsync();
        var publishedBefore = await PublishedStateAsync(f.Db);
        var othersBefore = await OtherProposalStateAsync(f.Db, f.User.UserId!);

        // Simulate the full settings posted by the owner editor, changing only FS.
        // Extension and PNC rows are omitted so their proposal-owned inputs must survive.
        var editor = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(99, editor.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        f.Page.Input.AnchorStart = editor.Durations.AnchorStart;
        f.Page.Input.IncludeWeekends = editor.Durations.IncludeWeekends;
        f.Page.Input.SkipHolidays = editor.Durations.SkipHolidays;
        f.Page.Input.NextStageStartPolicy = editor.Durations.NextStageStartPolicy;
        f.Page.Input.Rows[0].DurationDays = 100;
        if (explicitlyEnablePnc)
            f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC", DurationDays = 2 });
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var proposal = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == f.User.UserId);
        Assert.Equal(PlanningInputVersions.Current, proposal.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), proposal.AnchorDate);
        Assert.False(proposal.SkipWeekends);
        Assert.True(proposal.SkipHolidays);
        Assert.Equal(PlanTransitionRule.SameDay, proposal.TransitionRule);
        Assert.Equal(100, proposal.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2035, 1, 1), proposal.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(new DateOnly(2035, 4, 10), proposal.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedDue);
        var extension = proposal.StagePlans.Single(s => s.StageCode == "PRIVATE");
        Assert.Equal(45, extension.DurationDays);
        Assert.Equal(90, extension.SortOrder);
        Assert.NotNull(extension.PlannedStart);
        Assert.NotNull(extension.PlannedDue);
        Assert.DoesNotContain(proposal.StagePlans, s => s.StageCode == "SHARED-ONLY");
        var pncEnabled = explicitlyEnablePnc || livePnc && proposalPnc;
        Assert.Equal(pncEnabled, proposal.PncApplicable);
        var pnc = proposal.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(explicitlyEnablePnc ? 2 : pncEnabled ? 15 : 0, pnc.DurationDays);
        if (!pncEnabled) { Assert.Null(pnc.PlannedStart); Assert.Null(pnc.PlannedDue); }
        else { Assert.NotNull(pnc.PlannedStart); Assert.NotNull(pnc.PlannedDue); }
        Assert.Equal(publishedBefore, await PublishedStateAsync(f.Db));
        Assert.Equal(othersBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostDeleteDraftAsync(1, default));
        Assert.False(await f.Db.PlanVersions.AnyAsync(p => p.OwnerUserId == f.User.UserId));
        Assert.Equal(publishedBefore, await PublishedStateAsync(f.Db));
        Assert.Equal(othersBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
    }

    [Theory]
    [InlineData(PlanEditorModes.Exact)]
    [InlineData(PlanEditorModes.Durations)]
    public async Task Cutover_LegacyDraftResumesWithAcceptedPolicyBeforePostedEdits(string mode)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveDraft", relational: true);
        await SeedRecognizablePlannerStateAsync(f);
        var draft = await f.Db.PlanVersions.SingleAsync();
        draft.PlanningInputVersion = 0;
        draft.AnchorDate = new DateOnly(2040, 1, 1);
        draft.SkipWeekends = true;
        draft.SkipHolidays = false;
        draft.TransitionRule = PlanTransitionRule.NextWorkingDay;
        await f.Db.SaveChangesAsync();
        var before = await PublishedStateAsync(f.Db);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 23);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 26);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(1, draft.PlanningInputVersion);
        Assert.Equal(mode == PlanEditorModes.Exact ? new DateOnly(2026, 10, 5) : f.Page.Input.AnchorStart, draft.AnchorDate);
        Assert.Equal(mode == PlanEditorModes.Durations, draft.SkipWeekends);
        Assert.Equal(mode == PlanEditorModes.Exact, draft.SkipHolidays);
        Assert.Equal(mode == PlanEditorModes.Exact ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay, draft.TransitionRule);
        Assert.Equal(mode == PlanEditorModes.Exact ? 4 : 3, draft.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(before, await PublishedStateAsync(f.Db));
        var editor = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(draft.AnchorDate, editor.Durations.AnchorStart);
        Assert.Equal(draft.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedStart, editor.Exact.Rows.Single(s => s.Code == StageCodes.FS).PlannedStart);
    }

    [Theory]
    [InlineData(PlanEditorModes.Exact)]
    [InlineData(PlanEditorModes.Durations)]
    public async Task Cutover_DirectPublicationResetsAmbiguousCompatibilityAndUpgradesOnlyOwnDraft(string mode)
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveChanges", relational: true);
        await SeedRecognizablePlannerStateAsync(f);
        (await f.Db.Projects.SingleAsync()).PublishedPlanningInputVersion = 0;
        (await f.Db.Projects.SingleAsync()).PncApplicable = false;
        var own = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        own.PlanningInputVersion = 0;
        own.PncApplicable = true;
        own.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC, DurationDays = 80,
            PlannedStart = new DateOnly(2030, 1, 1), PlannedDue = new DateOnly(2030, 2, 1) });
        own.StagePlans.Add(new StagePlan { StageCode = "PRIVATE-ONLY", DurationDays = 77, SortOrder = 92,
            PlannedStart = new DateOnly(2031, 1, 1), PlannedDue = new DateOnly(2031, 2, 1) });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = "PRIVATE-ONLY", DurationDays = 77 });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = "OFFICIAL-EXT", DurationDays = 66, SortOrder = 91 });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 80 });
        f.Db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC,
                PlannedStart = new DateOnly(2025, 1, 1), PlannedDue = new DateOnly(2025, 2, 1) },
            new ProjectStage { ProjectId = 1, StageCode = "OFFICIAL-EXT", SortOrder = 91,
                PlannedStart = new DateOnly(2025, 3, 1), PlannedDue = new DateOnly(2025, 3, 4) });
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, OwnerUserId = "someone-else", CreatedByUserId = "someone-else",
            VersionNo = 9, PlanningInputVersion = 0, Status = PlanVersionStatus.Draft, AnchorDate = new DateOnly(2035, 1, 1) });
        await f.Db.SaveChangesAsync();
        var otherBefore = await OtherProposalStateAsync(f.Db, f.User.UserId!);
        f.Page.Input.Mode = mode;
        // Exact posts unchanged authoritative dates: cutover must still derive compatibility durations.
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2024, 1, 1);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2024, 1, 3);
        f.Page.Input.IncludeWeekends = true;
        f.Page.Input.SkipHolidays = false;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, (await f.Db.Projects.SingleAsync()).PublishedPlanningInputVersion);
        Assert.False((await f.Db.Projects.SingleAsync()).PncApplicable);
        var settings = await f.Db.ProjectScheduleSettings.SingleAsync();
        Assert.Equal(mode == PlanEditorModes.Durations ? f.Page.Input.AnchorStart : null, settings.AnchorStart);
        Assert.Equal(mode == PlanEditorModes.Durations, settings.IncludeWeekends);
        Assert.Equal(mode != PlanEditorModes.Durations, settings.SkipHolidays);
        Assert.Equal(mode == PlanEditorModes.Durations ? NextStageStartPolicies.SameDay : NextStageStartPolicies.NextWorkingDay, settings.NextStageStartPolicy);
        Assert.Equal(3, (await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays);
        Assert.False(await f.Db.ProjectPlanDurations.AnyAsync(d => d.StageCode == "PRIVATE-ONLY"));
        var officialExtensionDuration = await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == "OFFICIAL-EXT");
        Assert.Null(officialExtensionDuration.DurationDays);
        Assert.Equal(91, officialExtensionDuration.SortOrder);
        var officialExtensionStage = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == "OFFICIAL-EXT");
        Assert.Equal(new DateOnly(2025, 3, 1), officialExtensionStage.PlannedStart);
        Assert.Equal(new DateOnly(2025, 3, 4), officialExtensionStage.PlannedDue);
        Assert.Null((await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC)).DurationDays);
        var freshEditor = await new PlanReadService(f.Db).GetAsync(1, "fresh-user");
        Assert.DoesNotContain(freshEditor.Durations.Rows, row => row.Code == "PRIVATE-ONLY");
        Assert.Contains(freshEditor.Durations.Rows, row => row.Code == "OFFICIAL-EXT");
        own = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == f.User.UserId);
        Assert.Equal(1, own.PlanningInputVersion);
        Assert.False(own.PncApplicable);
        Assert.DoesNotContain(own.StagePlans, stage => stage.StageCode == "PRIVATE-ONLY");
        var pnc = own.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(0, pnc.DurationDays);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Equal(otherBefore, await OtherProposalStateAsync(f.Db, f.User.UserId!));
    }

    [Theory]
    [InlineData("SaveDraft")]
    [InlineData("Submit")]
    public async Task Cutover_UnchangedLegacyExactDatesPersistRecalculatedDurationBeforePublication(string action)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", action, relational: true, persistAudit: true);
        await SeedRecognizablePlannerStateAsync(f);
        var project = await f.Db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = 0;
        project.PncApplicable = false;
        var legacy = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        legacy.PlanningInputVersion = 0;
        legacy.PncApplicable = false;
        var fs = legacy.StagePlans.Single(s => s.StageCode == StageCodes.FS);
        fs.PlannedStart = new DateOnly(2026, 10, 23); // Friday
        fs.PlannedDue = new DateOnly(2026, 10, 26);   // Monday
        fs.DurationDays = 99;
        await f.Db.SaveChangesAsync();
        var publishedBefore = await PublishedStateAsync(f.Db);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows[0].PlannedStart = fs.PlannedStart;
        f.Page.Input.Rows[0].PlannedDue = fs.PlannedDue;

        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var proposal = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(1, proposal.PlanningInputVersion);
        Assert.Equal(action == "Submit" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, proposal.Status);
        Assert.Equal(2, proposal.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(publishedBefore, await PublishedStateAsync(f.Db));
        Assert.DoesNotContain(await f.Db.AuditLogs.AsNoTracking().ToListAsync(), entry => entry.Action == "Projects.PlanUpdated");

        if (action == "Submit")
        {
            var approval = new PlanApprovalService(f.Db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
                new PlanSnapshotService(f.Db), new NullNotifications());
            Assert.True(await approval.ApproveVersionAsync(proposal.Id, "hod-user", false, true));
            f.Db.ChangeTracker.Clear();
            Assert.Equal(2, (await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays);
            var official = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
            Assert.Equal(new DateOnly(2026, 10, 23), official.PlannedStart);
            Assert.Equal(new DateOnly(2026, 10, 26), official.PlannedDue);
        }
    }

    [Theory]
    [InlineData("HoD", false)]
    [InlineData("HoD", true)]
    [InlineData("Admin", false)]
    [InlineData("Admin", true)]
    public async Task Cutover_FirstDirectDurationPublicationPreservesOmittedOfficialDatesRegardlessOfLegacyCompatibilityRow(string role, bool hasLegacyCompatibilityRow)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings { ProjectId = 1,
            AnchorStart = new DateOnly(2030, 1, 1), IncludeWeekends = true });
        f.Db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA,
            PlannedStart = new DateOnly(2026, 10, 22), PlannedDue = new DateOnly(2026, 10, 26),
            Status = StageStatus.NotStarted, SortOrder = 20 });
        if (hasLegacyCompatibilityRow)
        {
            f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA,
                DurationDays = 77, SortOrder = 20 });
        }
        await f.Db.SaveChangesAsync();
        Assert.Single(f.Page.Input.Rows); // Only FS is explicitly published.
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var ipa = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2026, 10, 22), ipa.PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 26), ipa.PlannedDue);
        Assert.Equal(1, (await f.Db.Projects.SingleAsync()).PublishedPlanningInputVersion);
        var compatibility = await f.Db.ProjectPlanDurations.SingleOrDefaultAsync(d => d.StageCode == StageCodes.IPA);
        Assert.Equal(hasLegacyCompatibilityRow, compatibility is not null);
        if (compatibility is not null) Assert.Null(compatibility.DurationDays);
        Assert.Equal(3, (await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays);
        Assert.Equal(new DateOnly(2026, 10, 19), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).PlannedStart);
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        public EditPlanModel Page { get; }
        public UserContext User { get; }

        private readonly SqliteConnection? _connection;
        private Fixture(ApplicationDbContext db, EditPlanModel page, UserContext user, SqliteConnection? connection) => (Db, Page, User, _connection) = (db, page, user, connection);

        public static async Task<Fixture> CreateAsync(string role, string? action, bool relational = false, bool failAudit = false, bool persistAudit = false, IInterceptor? interceptor = null)
        {
            // FK validation has dedicated PostgreSQL coverage; this disposable
            // SQLite fixture exercises the real transaction/rollback boundaries.
            var connection = relational ? new SqliteConnection($"Data Source=planner-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False") : null;
            if (connection is not null) await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>();
            if (connection is not null) options.UseSqlite(connection);
            else options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new ApplicationDbContext(options.Options);
            if (relational) await db.Database.EnsureCreatedAsync();
            db.Projects.Add(new Project { Id = 1, Name = "Disposable duration test", LeadPoUserId = "po-user", WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
            db.StageTemplates.Add(new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 });
            db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 3) });
            await db.SaveChangesAsync();
            return Build(db, role, action, connection, failAudit, persistAudit);
        }

        public static async Task<Fixture> ForExistingDatabaseAsync(string role, System.Data.Common.DbConnection originalConnection)
        {
            var connection = new SqliteConnection(originalConnection.ConnectionString);
            await connection.OpenAsync();
            return Build(new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options),
                role, "SaveChanges", connection, false, true);
        }

        private static Fixture Build(ApplicationDbContext db, string role, string? action, SqliteConnection? connection, bool failAudit, bool persistAudit)
        {
            var actor = role == "Project Officer" ? "po-user" : "other-user";
            var user = new UserContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Name, actor),
                new Claim(ClaimTypes.Role, role == "Unassigned PO" ? "Project Officer" : role)
            }, "Test")) };
            var clock = new TestClock();
            IAuditService audit = failAudit ? new FailingAudit()
                : persistAudit ? new AuditService(db, new HttpContextAccessor(), clock) : new NullAudit();
            var draft = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, user);
            var approval = new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), new NullNotifications());
            var page = new EditPlanModel(db, audit, new PlanGenerationService(db), draft, approval, NullLogger<EditPlanModel>.Instance, user)
            {
                Input = new PlanEditInput
                {
                    ProjectId = 1, Mode = PlanEditorModes.Durations, Action = action,
                    AnchorStart = new DateOnly(2026, 10, 19), SkipHolidays = false,
                    Rows = new() { new() { Code = StageCodes.FS, Name = "FS", DurationDays = 3 } }
                }
            };
            var http = new DefaultHttpContext { User = user.User };
            page.PageContext = new PageContext(new ActionContext(http, new RouteData(), new PageActionDescriptor()));
            page.TempData = new TempDataDictionary(http, new TempProvider());
            return new Fixture(db, page, user, connection);
        }

        public async Task AssertLiveDatesUnchangedAsync()
        {
            Db.ChangeTracker.Clear();
            var stage = await Db.ProjectStages.SingleAsync();
            Assert.Equal(new DateOnly(2024, 1, 1), stage.PlannedStart);
            Assert.Equal(new DateOnly(2024, 1, 3), stage.PlannedDue);
            Assert.Equal(StageStatus.NotStarted, stage.Status);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
    }

    private sealed class PublicationConcurrencyObserver : SaveChangesInterceptor
    {
        public List<DbUpdateConcurrencyException> Exceptions { get; } = new();
        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Exceptions.Add((DbUpdateConcurrencyException)eventData.Exception);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class UserContext : IUserContext
    {
        public ClaimsPrincipal User { get; set; } = new();
        public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero); }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
    private sealed class FailingAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => throw new InvalidOperationException("Injected publication failure before transaction commit.");
    }
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
    private sealed class NullNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}