using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotTrackerReadServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsLatestExternalAndInternalRemarks()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Orion",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 1,
                Status = ProjectTotStatus.Completed,
                StartedOn = new DateOnly(2023, 1, 1),
                CompletedOn = new DateOnly(2023, 12, 31)
            }
        });

        context.Remarks.AddRange(
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "internal-1",
                AuthorRole = RemarkActorRole.ProjectOfficer,
                Type = RemarkType.Internal,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "Internal note",
                EventDate = new DateOnly(2023, 10, 15),
                CreatedAtUtc = new DateTime(2023, 10, 20, 8, 0, 0, DateTimeKind.Utc)
            },
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "external-1",
                AuthorRole = RemarkActorRole.ProjectOffice,
                Type = RemarkType.External,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "External summary",
                EventDate = new DateOnly(2023, 11, 10),
                CreatedAtUtc = new DateTime(2023, 11, 12, 9, 30, 0, DateTimeKind.Utc)
            },
            new Remark
            {
                ProjectId = 1,
                AuthorUserId = "external-2",
                AuthorRole = RemarkActorRole.ProjectOffice,
                Type = RemarkType.External,
                Scope = RemarkScope.TransferOfTechnology,
                Body = "Earlier external",
                EventDate = new DateOnly(2023, 8, 5),
                CreatedAtUtc = new DateTime(2023, 8, 6, 10, 0, 0, DateTimeKind.Utc)
            });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);
        var row = Assert.Single(rows);

        Assert.Equal("Project Orion", row.ProjectName);
        Assert.Equal(ProjectTotStatus.Completed, row.TotStatus);
        Assert.Equal("External summary", row.LatestExternalRemark?.Body);
        Assert.Equal(new DateOnly(2023, 11, 10), row.LatestExternalRemark?.EventDate);
        Assert.Equal("Internal note", row.LatestInternalRemark?.Body);
    }

    [Fact]
    public async Task GetAsync_PrefersLeadProjectOfficerNameThenUserNameThenId()
    {
        await using var context = CreateContext();

        var officerWithFullName = new ApplicationUser
        {
            Id = "po-1",
            FullName = "Squadron Leader Mira Rao",
            UserName = "mira.rao"
        };

        var officerWithUserNameOnly = new ApplicationUser
        {
            Id = "po-2",
            FullName = string.Empty,
            UserName = "ajay.singh"
        };

        context.Users.AddRange(officerWithFullName, officerWithUserNameOnly);

        context.Projects.AddRange(
            new Project
            {
                Id = 11,
                Name = "Project Zenith",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                LeadPoUserId = officerWithFullName.Id,
                LeadPoUser = officerWithFullName,
                Tot = new ProjectTot
                {
                    ProjectId = 11,
                    Status = ProjectTotStatus.InProgress
                }
            },
            new Project
            {
                Id = 12,
                Name = "Project Horizon",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                LeadPoUserId = officerWithUserNameOnly.Id,
                LeadPoUser = officerWithUserNameOnly,
                Tot = new ProjectTot
                {
                    ProjectId = 12,
                    Status = ProjectTotStatus.InProgress
                }
            },
            new Project
            {
                Id = 13,
                Name = "Project Ion",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                LeadPoUserId = "po-3",
                Tot = new ProjectTot
                {
                    ProjectId = 13,
                    Status = ProjectTotStatus.InProgress
                }
            });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);

        var zenith = Assert.Single(rows.Where(r => r.ProjectId == 11));
        var horizon = Assert.Single(rows.Where(r => r.ProjectId == 12));
        var ion = Assert.Single(rows.Where(r => r.ProjectId == 13));

        Assert.Equal("Squadron Leader Mira Rao", zenith.LeadProjectOfficer);
        Assert.Equal("ajay.singh", horizon.LeadProjectOfficer);
        Assert.Equal("po-3", ion.LeadProjectOfficer);
    }

    [Fact]
    public async Task GetAsync_HandlesProjectsWithoutExternalRemarks()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 2,
            Name = "Project Nova",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 2,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2024, 1, 5)
            }
        });

        context.Remarks.Add(new Remark
        {
            ProjectId = 2,
            AuthorUserId = "internal-2",
            AuthorRole = RemarkActorRole.ProjectOfficer,
            Type = RemarkType.Internal,
            Scope = RemarkScope.TransferOfTechnology,
            Body = "Internal progress",
            EventDate = new DateOnly(2024, 2, 1),
            CreatedAtUtc = new DateTime(2024, 2, 2, 6, 45, 0, DateTimeKind.Utc)
        });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);
        var row = Assert.Single(rows.Where(r => r.ProjectId == 2));

        Assert.Null(row.LatestExternalRemark);
        Assert.Equal("Internal progress", row.LatestInternalRemark?.Body);
    }

    [Fact]
    public async Task GetAsync_WithStartedDateFilters_ReturnsProjectsWithinRange()
    {
        await using var context = CreateContext();
        context.Projects.AddRange(
            new Project
            {
                Id = 3,
                Name = "Project Atlas",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 3,
                    Status = ProjectTotStatus.InProgress,
                    StartedOn = new DateOnly(2024, 1, 10)
                }
            },
            new Project
            {
                Id = 4,
                Name = "Project Beacon",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 4,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2024, 1, 20),
                    CompletedOn = new DateOnly(2024, 2, 5)
                }
            },
            new Project
            {
                Id = 5,
                Name = "Project Cobalt",
                LifecycleStatus = ProjectLifecycleStatus.Completed
            });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var filter = new ProjectTotTrackerFilter
        {
            StartedFrom = new DateOnly(2024, 1, 15),
            StartedTo = new DateOnly(2024, 1, 31)
        };

        var rows = await service.GetAsync(filter, CancellationToken.None);
        var names = rows.Select(r => r.ProjectName).ToList();

        Assert.Single(names);
        Assert.Contains("Project Beacon", names);
        Assert.DoesNotContain("Project Atlas", names);
        Assert.DoesNotContain("Project Cobalt", names);
    }

    [Fact]
    public async Task GetAsync_WithCompletedDateFilters_ReturnsProjectsWithinRange()
    {
        await using var context = CreateContext();
        context.Projects.AddRange(
            new Project
            {
                Id = 6,
                Name = "Project Delta",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 6,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2024, 1, 5),
                    CompletedOn = new DateOnly(2024, 2, 20)
                }
            },
            new Project
            {
                Id = 7,
                Name = "Project Eclipse",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 7,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2024, 1, 10),
                    CompletedOn = new DateOnly(2024, 3, 5)
                }
            },
            new Project
            {
                Id = 8,
                Name = "Project Forge",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                Tot = new ProjectTot
                {
                    ProjectId = 8,
                    Status = ProjectTotStatus.Completed,
                    StartedOn = new DateOnly(2023, 11, 1),
                    CompletedOn = new DateOnly(2023, 12, 15)
                }
            });

        await context.SaveChangesAsync();

        var service = new ProjectTotTrackerReadService(context);
        var filter = new ProjectTotTrackerFilter
        {
            CompletedFrom = new DateOnly(2024, 2, 1),
            CompletedTo = new DateOnly(2024, 2, 28)
        };

        var rows = await service.GetAsync(filter, CancellationToken.None);
        var ids = rows.Select(r => r.ProjectId).ToList();

        Assert.Single(ids);
        Assert.Contains(6, ids);
        Assert.DoesNotContain(7, ids);
        Assert.DoesNotContain(8, ids);
    }

    [Fact]
    public async Task GetAsync_WhenRequestColumnsMissing_FallsBackWithoutMetadata()
    {
        await using var context = CreateContext();
        context.Projects.Add(new Project
        {
            Id = 99,
            Name = "Project Nimbus",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            Tot = new ProjectTot
            {
                ProjectId = 99,
                Status = ProjectTotStatus.InProgress,
                StartedOn = new DateOnly(2024, 3, 1)
            },
            TotRequest = new ProjectTotRequest
            {
                ProjectId = 99,
                DecisionState = ProjectTotRequestDecisionState.Pending,
                ProposedStatus = ProjectTotStatus.Completed,
                ProposedStartedOn = new DateOnly(2024, 3, 1),
                ProposedCompletedOn = new DateOnly(2024, 4, 15),
                RowVersion = new byte[] { 0x01, 0x02 }
            }
        });

        await context.SaveChangesAsync();

        var service = new ThrowingProjectTotTrackerReadService(context);
        var rows = await service.GetAsync(new ProjectTotTrackerFilter(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Project Nimbus", row.ProjectName);
        Assert.Equal(ProjectTotStatus.InProgress, row.TotStatus);
        Assert.Equal(ProjectTotRequestDecisionState.Pending, row.RequestState);
        Assert.False(row.RequestMetadataAvailable);
        Assert.Null(row.RequestRowVersion);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class ThrowingProjectTotTrackerReadService : ProjectTotTrackerReadService
    {
        private bool _hasThrown;

        public ThrowingProjectTotTrackerReadService(ApplicationDbContext db)
            : base(db)
        {
        }

        protected override bool ShouldSimulateUndefinedColumn(
            ProjectTotTrackerFilter filter,
            bool includeTotDetailColumns,
            bool includeRequestDetailColumns)
        {
            if (!_hasThrown && includeRequestDetailColumns)
            {
                _hasThrown = true;
                return true;
            }

            return false;
        }
    }
}
