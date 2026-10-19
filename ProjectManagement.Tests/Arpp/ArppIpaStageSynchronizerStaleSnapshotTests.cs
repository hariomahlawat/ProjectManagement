using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

/// <summary>
/// Phase D-1c review: ARPP synchronization projects from the current IPA row,
/// not from values its context already tracked. EF writes only the columns that
/// differ from the tracked values, so a stale snapshot leaves a half-updated stage.
/// The cross-connection race is covered on PostgreSQL by
/// <see cref="StageDecisionConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class ArppIpaStageSynchronizerStaleSnapshotTests
{
    private static readonly DateOnly ArppIssueDate = new(2026, 2, 1);

    [Fact]
    public async Task StageAlreadyTrackedBeforeAReopenCommitted_IsCompletedFromItsCurrentRow()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        int projectId;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Users.Add(new ApplicationUser { Id = "seed", UserName = "seed" });
            var project = new Project { Name = "Stale snapshot", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV1 };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            projectId = project.Id;
            seed.ProjectStages.Add(new ProjectStage
            {
                ProjectId = projectId, StageCode = StageCodes.IPA, SortOrder = 2,
                Status = StageStatus.Completed, ActualStart = new DateOnly(2026, 1, 12), CompletedOn = new DateOnly(2026, 1, 20)
            });
            await seed.SaveChangesAsync();
        }

        await using var synchronizerDb = new ApplicationDbContext(options);
        // Loaded earlier in the same unit of work (e.g. an enclosing ARPP transaction).
        await synchronizerDb.ProjectStages.SingleAsync(stage => stage.ProjectId == projectId);

        // A lifecycle reopen commits meanwhile.
        await using (var decision = new ApplicationDbContext(options))
        {
            var ipa = await decision.ProjectStages.SingleAsync(stage => stage.ProjectId == projectId);
            ipa.Status = StageStatus.InProgress;
            ipa.CompletedOn = null;
            await decision.SaveChangesAsync();
        }

        var result = await new ArppIpaStageSynchronizer(
                synchronizerDb, new Authority(projectId), FakeClock.ForIstDate(2026, 3, 31))
            .SynchronizeProjectsAsync(new[] { projectId });

        Assert.Single(result.Changes);
        Assert.Equal(StageStatus.InProgress, result.Changes[0].PreviousStatus);
        await using var verify = new ApplicationDbContext(options);
        var stored = await verify.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId);
        Assert.Equal(StageStatus.Completed, stored.Status);
        Assert.Equal(ArppIssueDate, stored.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 12), stored.ActualStart);
    }

    private sealed class Authority(int projectId) : IArppIpaStageAuthorityService
    {
        private readonly ArppIpaStageAuthority _authority = new(
            projectId, 2025, 1, ArppIssueKind.Original, 1, "ARPP 2025-26", ArppIssueDate, 1, "1", null);

        public Task<ArppIpaStageAuthority?> ResolveAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArppIpaStageAuthority?>(_authority);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                new Dictionary<int, ArppIpaStageAuthority> { [projectId] = _authority });

        public Task<bool> IsManagedAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task EnsureManualLifecycleMutationAllowedAsync(int id, string stageCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
