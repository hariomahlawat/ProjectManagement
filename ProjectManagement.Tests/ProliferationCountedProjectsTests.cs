using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

// Approved proliferation is delivered history: it keeps counting when its project is later
// reopened or cancelled, and drops out only when the project is deleted or archived. Every
// totals surface reads ProliferationProjectEligibility.CountedProjects.
public sealed class ProliferationCountedProjectsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CountedProjects_KeepsReopenedAndCancelled_DropsDeletedAndArchived()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            Project(1, ProjectLifecycleStatus.Completed),
            Project(2, ProjectLifecycleStatus.Active),
            Project(3, ProjectLifecycleStatus.Cancelled),
            Project(4, ProjectLifecycleStatus.Completed, isDeleted: true),
            Project(5, ProjectLifecycleStatus.Completed, isArchived: true));
        await db.SaveChangesAsync();

        var ids = await ProliferationProjectEligibility
            .CountedProjects(db.Projects.AsNoTracking())
            .Select(p => p.Id)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 1, 2, 3 }, ids);
    }

    [Fact]
    public async Task Analysis_AllProliferation_IncludesReopenedProjectLikeTheSummaryDoes()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            Project(1, ProjectLifecycleStatus.Completed),
            Project(2, ProjectLifecycleStatus.Active));
        db.ProliferationYearlies.AddRange(Yearly(1, 30), Yearly(2, 12));
        await db.SaveChangesAsync();

        var aggregates = new ProliferationAggregateReadService(db);
        var clock = FakeClock.AtUtc(Now);
        var analysis = new ProliferationAnalysisService(
            db,
            aggregates,
            new ProliferationAnalysisExcelBuilder(),
            new ProliferationChronologyQualityService(db, aggregates, clock),
            clock,
            Options.Create(new ProliferationExportOptions()),
            NullLogger<ProliferationAnalysisService>.Instance);

        var result = await analysis.RunAsync(
            new ProliferationAnalysisRequestDto
            {
                Scope = ProliferationAnalysisScope.All,
                PeriodMode = ProliferationAnalysisPeriodMode.AllTime
            },
            CancellationToken.None);

        var summaryTotal = (await aggregates.GetApprovedAggregatesAsync(null, CancellationToken.None))
            .Sum(row => row.ReportedTotal);
        Assert.Equal(42, result.Summary.TotalProliferation);
        Assert.Equal(summaryTotal, result.Summary.TotalProliferation);
    }

    private static Project Project(int id, ProjectLifecycleStatus status, bool isDeleted = false, bool isArchived = false)
        => new()
        {
            Id = id,
            Name = $"Simulator {id}",
            CreatedByUserId = "seed",
            LifecycleStatus = status,
            IsDeleted = isDeleted,
            IsArchived = isArchived,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

    private static ProliferationYearly Yearly(int projectId, int quantity)
        => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Source = ProliferationSource.Abw515,
            Year = 2025,
            TotalQuantity = quantity,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "seed",
            CreatedOnUtc = Now.UtcDateTime,
            LastUpdatedOnUtc = Now.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
