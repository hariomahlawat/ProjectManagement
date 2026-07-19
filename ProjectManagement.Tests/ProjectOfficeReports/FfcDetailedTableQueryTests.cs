using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcDetailedTableQueryTests
{
    [Fact]
    public async Task GetDetailedGroupsAsync_PreservesDetailedTableGroupingAndRowContract()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Ethiopia", IsoCode = "ETH", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2025,
            OverallRemarks = "Overall FFC position"
        };
        var linkedProject = new Project
        {
            Name = "Canonical IWTS",
            CreatedByUserId = "user"
        };
        db.FfcRecords.Add(record);
        db.Projects.Add(linkedProject);
        await db.SaveChangesAsync();

        var linkedFfcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Legacy display name",
            LinkedProjectId = linkedProject.Id,
            Quantity = 2,
            IsDelivered = true
        };
        var unlinkedFfcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "VR CMC",
            Remarks = "Unlinked FFC progress",
            Quantity = 4
        };
        db.FfcProjects.AddRange(linkedFfcProject, unlinkedFfcProject);
        await db.SaveChangesAsync();

        var externalRemark = new Remark
        {
            ProjectId = linkedProject.Id,
            AuthorUserId = "user",
            AuthorRole = RemarkActorRole.Administrator,
            Type = RemarkType.External,
            Scope = RemarkScope.General,
            Body = "TEC under progress.",
            EventDate = new DateOnly(2025, 1, 1),
            CreatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RowVersion = new byte[] { 1 }
        };
        db.Remarks.Add(externalRemark);
        await db.SaveChangesAsync();

        var progressService = new FfcProgressService(db, new NoOpRemarkService());
        var service = new FfcQueryService(
            db,
            new StubCostResolver(linkedProject.Id, 0.70m),
            progressService);

        var groups = await service.GetDetailedGroupsAsync(
            DateOnly.MinValue,
            DateOnly.MaxValue,
            incompleteOnly: false,
            applyYearFilter: false);

        var group = Assert.Single(groups);
        Assert.Equal(record.Id, group.FfcRecordId);
        Assert.Equal("Ethiopia", group.CountryName);
        Assert.Equal("ETH", group.CountryCode);
        Assert.Equal(2025, group.Year);
        Assert.Equal("Overall FFC position", group.OverallRemarks);
        Assert.True(group.HasIncomplete);
        Assert.Equal(2, group.Rows.Count);

        var linkedRow = group.Rows[0];
        Assert.Equal(1, linkedRow.Serial);
        Assert.Equal("Canonical IWTS", linkedRow.ProjectName);
        Assert.Equal(linkedProject.Id, linkedRow.LinkedProjectId);
        Assert.Equal(0.70m, linkedRow.CostInCr);
        Assert.Equal(2, linkedRow.Quantity);
        Assert.Equal("Delivered (not installed)", linkedRow.Status);
        Assert.Equal("TEC under progress.", linkedRow.ProgressText);
        Assert.Equal(externalRemark.Id, linkedRow.ExternalRemarkId);
        Assert.Equal(FfcProgressSource.ExternalProjectRemark, linkedRow.ProgressSource);
        Assert.True(linkedRow.IsProgressEditable);

        var unlinkedRow = group.Rows[1];
        Assert.Equal(2, unlinkedRow.Serial);
        Assert.Equal("VR CMC", unlinkedRow.ProjectName);
        Assert.Null(unlinkedRow.LinkedProjectId);
        Assert.Null(unlinkedRow.CostInCr);
        Assert.Equal(4, unlinkedRow.Quantity);
        Assert.Equal("Planned", unlinkedRow.Status);
        Assert.Null(unlinkedRow.ProgressText);
        Assert.Equal(FfcProgressSource.FfcProjectRemark, unlinkedRow.ProgressSource);
        Assert.False(unlinkedRow.IsProgressEditable);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class StubCostResolver : IProjectCostResolver
    {
        private readonly int _projectId;
        private readonly decimal _costInCr;

        public StubCostResolver(int projectId, decimal costInCr)
        {
            _projectId = projectId;
            _costInCr = costInCr;
        }

        public Task<Dictionary<int, ProjectCostResolution>> ResolveCostInCrAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken ct = default)
        {
            var result = new Dictionary<int, ProjectCostResolution>();
            foreach (var projectId in projectIds)
            {
                result[projectId] = projectId == _projectId
                    ? new ProjectCostResolution(_costInCr, ProjectCostSource.CostLakhs)
                    : new ProjectCostResolution(null, ProjectCostSource.None);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class NoOpRemarkService : IRemarkService
    {
        public Task<Remark> CreateRemarkAsync(CreateRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RemarkListResult> ListRemarksAsync(ListRemarksRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Remark?> EditRemarkAsync(int remarkId, EditRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> SoftDeleteRemarkAsync(int remarkId, SoftDeleteRemarkRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RemarkAudit>> GetRemarkAuditAsync(int remarkId, RemarkActorContext actor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
