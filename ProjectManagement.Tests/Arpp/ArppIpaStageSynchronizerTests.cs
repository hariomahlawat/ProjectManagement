using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppIpaStageSynchronizerTests
{
    [Fact]
    public async Task SynchronizeProjects_UsesEarliestPublishedIssueDate_AndPreservesValidActualStart()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project AURA",
            CreatedByUserId = "seed"
        });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = ProcurementWorkflow.OrderOf(null, StageCodes.IPA),
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 2, 24),
            IsAutoCompleted = true,
            AutoCompletedFromCode = StageCodes.AON,
            RequiresBackfill = true
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        AddPublishedPosition(db, 2, 1, new DateOnly(2026, 7, 27), 1, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new DateOnly(2026, 2, 26), change.CompletionDate);
        Assert.Equal(1, change.SourceIssueId);
        Assert.Equal("Original ARPP", change.SourceDocumentLabel);
        Assert.Empty(result.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 1), stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task SynchronizeProjects_CreatesMissingIpaStage_WithoutInventingActualStart()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project One",
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            CreatedByUserId = "seed"
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.Delisted);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.True(change.StageCreated);
        Assert.Empty(result.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageCodes.IPA, stage.StageCode);
        Assert.Equal(ProcurementWorkflow.OrderOf(ProcurementWorkflow.VersionV2, StageCodes.IPA), stage.SortOrder);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task SynchronizeProjects_PreservesInvalidActualStart_AndRecordsDataQualityIssueOnce()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 5, 1)
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        var first = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        var issue = Assert.Single(first.DataQualityIssues);
        Assert.Equal(new DateOnly(2026, 5, 1), issue.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), issue.CompletionDate);

        db.AuditLogs.Add(new AuditLog
        {
            Action = "Arpp.IpaStageDataQualityIssue",
            Level = "Warning",
            Message = "Recorded IPA actual start is later than the authoritative completion date.",
            DataJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["ProjectId"] = "1",
                ["ActualStart"] = "2026-05-01",
                ["CompletionDate"] = "2026-04-10"
            })
        });
        await db.SaveChangesAsync();

        var second = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);
        Assert.Empty(second.DataQualityIssues);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 5, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 4, 10), stage.CompletedOn);
        Assert.Equal(StageStatus.Completed, stage.Status);

        Assert.DoesNotContain(
            await db.StageChangeLogs.ToListAsync(),
            log => string.Equals(log.Action, "ArppDataQuality", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SynchronizeProjects_LaterAddendumDoesNotMoveInitialIpaCompletionDate()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        AddPublishedPosition(db, 2, 1, new DateOnly(2026, 7, 27), 1, ArppCategory.CarryForward);
        await db.SaveChangesAsync();

        await new ArppIpaStageSynchronizer(db).SynchronizeProjectsAsync([1]);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.Null(stage.ActualStart);
    }

    [Fact]
    public async Task SynchronizeProjects_SupersedesPendingManualIpaRequest()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 1, 1)
        });
        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2026, 3, 1),
            RequestedByUserId = "po-1",
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 2, 26), 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Equal(1, result.SupersededRequestCount);
        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal("Superseded", request.DecisionStatus);
        Assert.Equal("PRISM system", request.DecidedByUserId);
        Assert.NotNull(request.DecidedOn);
        Assert.Contains("published ARPP records became authoritative", request.DecisionNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await db.StageChangeLogs.ToListAsync(), log =>
            log.Action == "Superseded" &&
            log.StageCode == StageCodes.IPA);
    }

    [Fact]
    public async Task SynchronizeProjects_SupersedesPendingRequest_WhenStageAlreadyMatchesAuthority()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 2, 26);
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.Completed,
            CompletedOn = authorityDate
        });
        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = authorityDate.AddDays(2),
            RequestedByUserId = "po-1",
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        });
        AddPublishedPosition(db, 1, 1, authorityDate, 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Empty(result.Changes);
        Assert.Equal(1, result.SupersededRequestCount);
        Assert.Equal("Superseded", (await db.StageChangeRequests.SingleAsync()).DecisionStatus);
    }

    [Fact]
    public async Task SynchronizeProjects_IgnoresWorkingRowsWithoutPublishedSnapshot()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 2,
            Status = StageStatus.NotStarted
        });
        db.ArppIssues.Add(new ArppIssue
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "Working ARPP",
            IssueDate = new DateOnly(2026, 4, 10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed",
            Entries =
            {
                new ArppEntry
                {
                    SortOrder = 1,
                    SerialNumber = "1",
                    PppNumber = "ARPP/IR&D/N/2026-27/1",
                    ProjectReference = "Project One",
                    ProjectId = 1,
                    Category = ArppCategory.New,
                    IpaCost = 1_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = "seed",
                    UpdatedByUserId = "seed"
                }
            }
        });
        await db.SaveChangesAsync();

        var result = await new ArppIpaStageSynchronizer(db)
            .SynchronizeProjectsAsync([1]);

        Assert.Empty(result.Changes);
        Assert.Empty(result.DataQualityIssues);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.NotStarted, stage.Status);
        Assert.Null(stage.CompletedOn);
    }

    [Fact]
    public async Task SynchronizeProjects_IsIdempotent()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var service = new ArppIpaStageSynchronizer(db);
        var first = await service.SynchronizeProjectsAsync([1]);
        var second = await service.SynchronizeProjectsAsync([1]);

        Assert.Single(first.Changes);
        Assert.Empty(second.Changes);
        Assert.Empty(second.DataQualityIssues);
        Assert.Single(await db.ProjectStages.ToListAsync());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public async Task SynchronizeProjects_RevisedPublishedAuthoritySupersedesPreviouslySynchronizedDate(int revisedDay)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();
        var synchronizer = new ArppIpaStageSynchronizer(db);
        await synchronizer.SynchronizeProjectsAsync([1]);

        // Model the already-published result of an authorised source reconciliation,
        // not a later addendum (which must not replace the earliest source).
        var publishedIssue = await db.ArppPublishedIssues.SingleAsync();
        publishedIssue.IssueDate = new DateOnly(2026, 4, revisedDay);
        publishedIssue.RevisionNumber++;
        await db.SaveChangesAsync();
        var result = await synchronizer.SynchronizeProjectsAsync([1]);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new DateOnly(2026, 4, 10), change.PreviousCompletedOn);
        Assert.Equal(publishedIssue.IssueDate, change.CompletionDate);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(publishedIssue.IssueDate, stage.CompletedOn);
        Assert.Null(stage.ActualStart);
        Assert.False(stage.RequiresBackfill);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public async Task SynchronizeProjects_RevisedAuthorityPreservesGenuineStartAndReportsNewInversion(int revisedDay)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        var genuineStart = new DateOnly(2026, 4, 8);
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.Completed,
            ActualStart = genuineStart, CompletedOn = new DateOnly(2026, 4, 9)
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();
        var synchronizer = new ArppIpaStageSynchronizer(db);
        await synchronizer.SynchronizeProjectsAsync([1]);
        var publishedIssue = await db.ArppPublishedIssues.SingleAsync();
        publishedIssue.IssueDate = new DateOnly(2026, 4, revisedDay);
        publishedIssue.RevisionNumber++;
        await db.SaveChangesAsync();

        var result = await synchronizer.SynchronizeProjectsAsync([1]);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(genuineStart, stage.ActualStart);
        Assert.Equal(publishedIssue.IssueDate, stage.CompletedOn);
        if (revisedDay < genuineStart.Day)
        {
            var conflict = Assert.Single(result.DataQualityIssues);
            Assert.Equal(genuineStart, conflict.ActualStart);
            Assert.Equal(publishedIssue.IssueDate, conflict.CompletionDate);
        }
        else
        {
            Assert.Empty(result.DataQualityIssues);
        }
    }

    [Fact]
    public async Task ActualsEdit_RejectsLegacyStoredCompletionThatDiffersFromPublishedAuthority()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed", LeadPoUserId = "assigned-po" });
        var legacyCompletion = new DateOnly(2026, 4, 20);
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.Completed,
            CompletedOn = legacyCompletion
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(2026, 8, 1),
            new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance);

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA, CompletedOn = legacyCompletion }]
            }, TestActors.ProjectOfficer("assigned-po"), "Assigned PO"));

        Assert.Contains(exception.Errors, message => message.Contains("ARPP", StringComparison.OrdinalIgnoreCase));
        // Rejected generic edits do not silently repair existing production data.
        Assert.Equal(legacyCompletion, (await db.ProjectStages.SingleAsync()).CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task ActualsEdit_ValidStartCorrectionUsesPublishedAuthorityRatherThanLegacyCompletion()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed" });
        var authorityDate = new DateOnly(2026, 4, 10);
        var legacyDate = new DateOnly(2026, 4, 20);
        var originalStart = new DateOnly(2026, 4, 1);
        var correctedStart = new DateOnly(2026, 4, 2);
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.Completed,
            ActualStart = originalStart, CompletedOn = legacyDate
        });
        AddPublishedPosition(db, 1, 1, authorityDate, 0, ArppCategory.New);
        await db.SaveChangesAsync();
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(2026, 8, 1),
            new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance);

        var result = await service.UpdateAsync(new ActualsEditInput
        {
            ProjectId = 1,
            Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA, ActualStart = correctedStart }]
        }, TestActors.Admin("admin-1"), "Administrator");

        Assert.Equal(1, result.UpdatedCount);
        var ipa = await db.ProjectStages.SingleAsync();
        Assert.Equal(correctedStart, ipa.ActualStart);
        Assert.Equal(authorityDate, ipa.CompletedOn);
        var history = Assert.Single(await db.StageChangeLogs.ToListAsync());
        Assert.Equal(originalStart, history.FromActualStart);
        Assert.Equal(correctedStart, history.ToActualStart);
        Assert.Equal(legacyDate, history.FromCompletedOn);
        Assert.Equal(authorityDate, history.ToCompletedOn);
    }

    [Fact]
    public async Task ActualsEdit_UnchangedUnknownStartDoesNotRepairLegacyCompletionAsSideEffect()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Project One", CreatedByUserId = "seed", LeadPoUserId = "assigned-po" });
        var legacyDate = new DateOnly(2026, 4, 20);
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.Completed, CompletedOn = legacyDate
        });
        AddPublishedPosition(db, 1, 1, new DateOnly(2026, 4, 10), 0, ArppCategory.New);
        await db.SaveChangesAsync();
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(2026, 8, 1),
            new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance);

        var result = await service.UpdateAsync(new ActualsEditInput
        {
            ProjectId = 1, Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA }]
        }, TestActors.ProjectOfficer("assigned-po"), "Assigned PO");

        Assert.Equal(0, result.UpdatedCount);
        var ipa = await db.ProjectStages.SingleAsync();
        Assert.Null(ipa.ActualStart);
        Assert.Equal(legacyDate, ipa.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }
    [Fact]
    public async Task DirectForceBackfill_DoesNotEraseManagedPredecessorCompletionOrGenuineStart()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        var genuineStart = new DateOnly(2026, 4, 1);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, genuineStart);
        var clock = FakeClock.ForIstDate(2026, 8, 1);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy),
            new StageRulesService(db, policy), policy);

        // The IPA row is deliberately inconsistent legacy data. A downstream
        // override cannot replace the source-controlled date with null/backfill.
        await service.ApplyAsync(1, StageCodes.SOW, StageStatus.Completed.ToString(),
            new DateOnly(2026, 4, 25), null, "hod-1", true, CancellationToken.None);

        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(authorityDate, ipa.CompletedOn);
        Assert.Equal(genuineStart, ipa.ActualStart);
        Assert.DoesNotContain(await db.StageChangeLogs.ToListAsync(), log =>
            log.StageCode == StageCodes.IPA && log.Action == "AutoBackfill");
    }

    [Fact]
    public async Task CompletionCascade_DoesNotReplaceManagedPredecessorDateWithDownstreamCompletion()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, null);
        var clock = FakeClock.ForIstDate(2026, 8, 1);
        var service = new StageProgressService(db, clock, new RecordingAudit(), new ProjectFactsReadService(db),
            new NullStageNotifications(), StageWorkflowTestFactory.CreatePolicy(db));

        await service.UpdateStageStatusAsync(1, StageCodes.SOW, StageStatus.Completed,
            new DateOnly(2026, 4, 25), "hod-1");

        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(authorityDate, ipa.CompletedOn);
        Assert.Null(ipa.ActualStart);
    }

    [Fact]
    public async Task AssignedOfficerRequest_CannotReplacePublishedIpaCompletion()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, null);
        var project = await db.Projects.SingleAsync();
        project.LeadPoUserId = "assigned-po";
        await db.SaveChangesAsync();
        var clock = FakeClock.ForIstDate(2026, 8, 1);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageRequestService(db, clock, new StageValidationService(db, clock, policy), policy);

        var result = await service.CreateAsync(new ProjectManagement.Contracts.Stages.StageChangeRequestInput
        {
            ProjectId = 1, StageCode = StageCodes.IPA, RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = authorityDate.AddDays(2)
        }, "assigned-po");

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Errors, message => message.Contains("ARPP", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.StageChangeRequests.ToListAsync());
        Assert.Equal(authorityDate, (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).CompletedOn);
    }

    [Theory]
    [InlineData(false, true, "hod-1")]
    [InlineData(true, false, "admin-1")]
    public async Task ApprovePendingManualIpaRequest_CannotOverridePublishedAuthority(bool isAdmin, bool isHoD, string actor)
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, null);
        var request = new StageChangeRequest
        {
            ProjectId = 1, StageCode = StageCodes.IPA, RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = authorityDate.AddDays(2), RequestedByUserId = "assigned-po",
            RequestedOn = DateTimeOffset.UtcNow, DecisionStatus = "Pending"
        };
        db.StageChangeRequests.Add(request);
        await db.SaveChangesAsync();
        var clock = FakeClock.ForIstDate(2026, 8, 1);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var progress = new StageProgressService(db, clock, new RecordingAudit(), new ProjectFactsReadService(db),
            new NullStageNotifications(), policy);
        var service = new StageDecisionService(db, clock, progress,
            NullLogger<StageDecisionService>.Instance, policy);

        var result = await service.DecideAsync(new StageDecisionInput(request.Id, StageDecisionAction.Approve, null),
            actor, isAdmin, isHoD);

        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Pending", (await db.StageChangeRequests.SingleAsync()).DecisionStatus);
        Assert.Equal(authorityDate, (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task CompletionBackfill_CannotOverridePublishedIpaAuthority()
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, null);
        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        ipa.Status = StageStatus.Completed;
        ipa.RequiresBackfill = true;
        await db.SaveChangesAsync();
        var service = new StageBackfillService(db, FakeClock.ForIstDate(2026, 8, 1));

        var exception = await Assert.ThrowsAsync<StageBackfillValidationException>(() => service.ApplyAsync(1,
            [new StageBackfillUpdate(StageCodes.IPA, null, authorityDate.AddDays(2))], TestActors.Hod("hod-1")));

        Assert.Contains(exception.Details, message => message.Contains("ARPP", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(authorityDate, ipa.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Theory]
    [InlineData("hod-1")]
    [InlineData("admin-1")]
    public async Task HistoricalManualRecord_CannotOverridePublishedIpaAuthority(string actor)
    {
        await using var db = CreateContext();
        var authorityDate = new DateOnly(2026, 4, 10);
        await SeedManagedIpaAndDownstreamAsync(db, authorityDate, null);
        var project = await db.Projects.SingleAsync();
        project.IsLegacy = true;
        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        await db.SaveChangesAsync();
        var service = new HistoricalStageRecordService(db, new WorkflowStageMetadataProvider(),
            FakeClock.ForIstDate(2026, 8, 1), new RecordingAudit(), NullLogger<HistoricalStageRecordService>.Instance);
        var input = new HistoricalStageRecordInput
        {
            ProjectId = 1, EvidenceNote = "Documented historical review",
            Rows = ProcurementWorkflow.StageDefinitionsFor(project.WorkflowVersion).Select(definition =>
                new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code,
                    Outcome = definition.Code == StageCodes.IPA ? HistoricalStageOutcome.Completed : HistoricalStageOutcome.NotRecorded,
                    CompletedOn = definition.Code == StageCodes.IPA ? authorityDate.AddDays(2) : null
                }).ToList()
        };

        var exception = await Assert.ThrowsAsync<HistoricalStageRecordValidationException>(() => service.SaveAsync(input, actor, actor));

        Assert.Contains(exception.Errors, message => message.Contains("ARPP", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(authorityDate, (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionAuthority_WithoutPublishedSnapshotDoesNotLockBackfill(bool hasDraft)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Manual IPA", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.IPA,
            Status = StageStatus.Completed, RequiresBackfill = true
        });
        if (hasDraft) AddWorkingPosition(db);
        await db.SaveChangesAsync();
        var authority = new ArppIpaStageAuthorityService(db);
        Assert.False(await authority.IsManagedAsync(1));
        Assert.Null(await authority.ResolveAsync(1));
        await authority.EnsureManualLifecycleMutationAllowedAsync(1, StageCodes.IPA);
        var completion = new DateOnly(2026, 4, 12);

        await new StageBackfillService(db, FakeClock.ForIstDate(2026, 8, 1)).ApplyAsync(
            1, [new StageBackfillUpdate(StageCodes.IPA, null, completion)], TestActors.Hod("hod-1"));

        var ipa = await db.ProjectStages.SingleAsync();
        Assert.Equal(completion, ipa.CompletedOn);
        Assert.Null(ipa.ActualStart);
        Assert.False(ipa.RequiresBackfill);
        Assert.Equal(completion, (await db.StageChangeLogs.SingleAsync()).ToCompletedOn);
    }

    [Theory]
    [InlineData(false, "hod-1")]
    [InlineData(true, "hod-1")]
    [InlineData(false, "admin-1")]
    [InlineData(true, "admin-1")]
    public async Task HistoricalCompletion_WithoutPublishedSnapshotRemainsManualUntilPublication(bool hasDraft, string actor)
    {
        await using var db = CreateContext();
        var project = new Project
        {
            Id = 1, Name = "Legacy manual IPA", CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV1, IsLegacy = true,
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };
        db.Projects.Add(project);
        if (hasDraft) AddWorkingPosition(db);
        await db.SaveChangesAsync();
        var manualDate = new DateOnly(2026, 4, 12);
        var service = new HistoricalStageRecordService(db, new WorkflowStageMetadataProvider(),
            FakeClock.ForIstDate(2026, 8, 1), new RecordingAudit(), NullLogger<HistoricalStageRecordService>.Instance);
        var input = new HistoricalStageRecordInput
        {
            ProjectId = 1, EvidenceNote = "Documented manual IPA completion",
            Rows = ProcurementWorkflow.StageDefinitionsFor(project.WorkflowVersion).Select(definition =>
                new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code,
                    Outcome = definition.Code == StageCodes.IPA ? HistoricalStageOutcome.Completed : HistoricalStageOutcome.NotRecorded,
                    CompletedOn = definition.Code == StageCodes.IPA ? manualDate : null
                }).ToList()
        };

        var result = await service.SaveAsync(input, actor, actor);

        var ipa = await db.ProjectStages.SingleAsync();
        Assert.Equal(manualDate, ipa.CompletedOn);
        Assert.Null(ipa.ActualStart);
        var manualHistory = await db.StageChangeLogs.SingleAsync();
        Assert.Equal(manualDate, manualHistory.ToCompletedOn);
        Assert.Equal(actor, manualHistory.UserId);
        var publishedDate = new DateOnly(2026, 4, 10);
        AddPublishedPosition(db, 1, 1, publishedDate, 0, ArppCategory.New);
        await db.SaveChangesAsync();

        var synchronization = await new ArppIpaStageSynchronizer(db).SynchronizeProjectsAsync([1]);

        var change = Assert.Single(synchronization.Changes);
        Assert.Equal(manualDate, change.PreviousCompletedOn);
        Assert.Equal(publishedDate, change.CompletionDate);
        Assert.Equal(publishedDate, ipa.CompletedOn);
        Assert.Null(ipa.ActualStart);
        Assert.Equal(manualDate, (await db.StageChangeLogs.SingleAsync()).ToCompletedOn);
        var authority = new ArppIpaStageAuthorityService(db);
        Assert.True(await authority.IsManagedAsync(1));
        await Assert.ThrowsAsync<ArppManagedIpaStageException>(() =>
            authority.EnsureManualLifecycleMutationAllowedAsync(1, StageCodes.IPA));
        await Assert.ThrowsAsync<HistoricalStageRecordValidationException>(() =>
            service.SaveAsync(input, actor, actor));
        Assert.Equal(publishedDate, ipa.CompletedOn);
    }

    private static void AddWorkingPosition(ApplicationDbContext db)
    {
        db.ArppIssues.Add(new ArppIssue
        {
            Id = 10, FinancialYearStart = 2026, Kind = ArppIssueKind.Original, IssueSequence = 0,
            Name = "Unpublished draft", IssueDate = new DateOnly(2026, 4, 1),
            CreatedByUserId = "seed", UpdatedByUserId = "seed",
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
            Entries =
            {
                new ArppEntry
                {
                    ProjectId = 1, ProjectReference = "Manual IPA", Category = ArppCategory.New,
                    CreatedByUserId = "seed", UpdatedByUserId = "seed",
                    CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
                }
            }
        });
    }

    private static async Task SeedManagedIpaAndDownstreamAsync(
        ApplicationDbContext db, DateOnly authorityDate, DateOnly? genuineStart)
    {
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Project One", CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.Completed,
                CompletedOn = new DateOnly(2026, 3, 1) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA, Status = StageStatus.NotStarted,
                CompletedOn = authorityDate, ActualStart = genuineStart },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.SOW, Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2026, 4, 11) });
        db.ProjectSowFacts.Add(new ProjectSowFact { ProjectId = 1, CreatedByUserId = "seed" });
        AddPublishedPosition(db, 1, 1, authorityDate, 0, ArppCategory.New);
        await db.SaveChangesAsync();
    }

    private sealed class NullStageNotifications : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(ProjectStage stage, Project project, StageStatus previousStatus,
            string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static void AddPublishedPosition(
        ApplicationDbContext db,
        long issueId,
        int projectId,
        DateOnly issueDate,
        int issueSequence,
        ArppCategory category)
    {
        var issue = new ArppIssue
        {
            Id = issueId,
            FinancialYearStart = 2026,
            Kind = issueSequence == 0 ? ArppIssueKind.Original : ArppIssueKind.Addendum,
            IssueSequence = issueSequence,
            Name = issueSequence == 0 ? "ARPP/I&R&D/ARTRAC" : "ARPP/I&R&D/ARTRAC",
            IssueDate = issueDate,
            IsVerified = true,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            VerifiedByUserId = "verifier",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issueId,
            RevisionNumber = 1,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = $"arpp/{issueId}.pdf",
            AttachmentOriginalFileName = $"ARPP-{issueId}.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    ArppIssueId = issueId,
                    SourceEntryId = issueId,
                    SortOrder = 1,
                    SerialNumber = category == ArppCategory.Delisted ? null : "33",
                    PppNumber = category == ArppCategory.Delisted ? null : "ARPP/IR&D/2026-27/33",
                    ProjectReference = $"Project {projectId}",
                    ProjectId = projectId,
                    Category = category,
                    IpaCost = issueSequence == 0 ? 40_000_000m : 50_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
