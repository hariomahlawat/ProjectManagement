using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Tests;

public sealed class IprReadServiceTests
{
    [Fact]
    public async Task SearchAsync_AppliesFiltersPagingAndOrdering()
    {
        await using var db = CreateDbContext();

        var project = new Project
        {
            Name = "Apex Initiative",
            CreatedByUserId = "creator"
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var records = new List<IprRecord>
        {
            new()
            {
                IprFilingNumber = "IPR-001",
                Title = "Alpha",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess,
                FiledAtUtc = null,
                ProjectId = null
            },
            new()
            {
                IprFilingNumber = "IPR-002",
                Title = "Bravo",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero),
                ProjectId = project.Id
            },
            new()
            {
                IprFilingNumber = "IPR-003",
                Title = "Charlie",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 3, 10, 0, 0, 0, TimeSpan.Zero),
                ProjectId = project.Id
            },
            new()
            {
                IprFilingNumber = "IPR-004",
                Title = "Delta",
                Type = IprType.Copyright,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 2, 5, 0, 0, 0, TimeSpan.Zero),
                ProjectId = project.Id
            }
        };

        db.IprRecords.AddRange(records);
        await db.SaveChangesAsync();

        var service = new IprReadService(db);

        var filter = new IprFilter
        {
            Types = new[] { IprType.Patent },
            Statuses = new[] { IprStatus.Filed },
            ProjectId = project.Id,
            FiledFrom = new DateOnly(2024, 1, 1),
            FiledTo = new DateOnly(2024, 12, 31)
        };
        filter.PageSize = 1;
        filter.Page = 2;

        var result = await service.SearchAsync(filter);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);

        var item = Assert.Single(result.Items);
        Assert.Equal("IPR-002", item.FilingNumber);
        Assert.Equal(IprStatus.Filed, item.Status);
        Assert.Equal(project.Id, item.ProjectId);
        Assert.Equal("Bravo", item.Title);
    }

    [Fact]
    public async Task GetKpisAsync_RespectsStatusFilter()
    {
        await using var db = CreateDbContext();

        var records = new List<IprRecord>
        {
            new()
            {
                IprFilingNumber = "IPR-101",
                Title = "Orion",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            },
            new()
            {
                IprFilingNumber = "IPR-102",
                Title = "Pegasus",
                Type = IprType.Patent,
                Status = IprStatus.Filed
            },
            new()
            {
                IprFilingNumber = "IPR-103",
                Title = "Phoenix",
                Type = IprType.Copyright,
                Status = IprStatus.Granted
            },
            new()
            {
                IprFilingNumber = "IPR-104",
                Title = "Quasar",
                Type = IprType.Copyright,
                Status = IprStatus.Filed
            }
        };

        db.IprRecords.AddRange(records);
        await db.SaveChangesAsync();

        var service = new IprReadService(db);

        var filter = new IprFilter
        {
            Statuses = new[] { IprStatus.Filed, IprStatus.Granted }
        };

        var search = await service.SearchAsync(filter);
        var kpis = await service.GetKpisAsync(filter);

        Assert.Equal(search.Total, kpis.Total);
        Assert.Equal(search.Items.Count(x => x.Status == IprStatus.Filed), kpis.Filed);
        Assert.Equal(search.Items.Count(x => x.Status == IprStatus.Granted), kpis.Granted);
        Assert.Equal(0, kpis.FilingUnderProcess);
        Assert.Equal(0, kpis.Rejected);
        Assert.Equal(0, kpis.Withdrawn);
    }

    [Fact]
    public async Task GetExportAsync_ReturnsFilteredOrderedRows()
    {
        await using var db = CreateDbContext();

        var project = new Project
        {
            Name = "Beacon Project",
            CreatedByUserId = "owner"
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var grantedWithDate = new IprRecord
        {
            IprFilingNumber = "IPR-010",
            Title = "Gamma",
            Type = IprType.Copyright,
            Status = IprStatus.Granted,
            FiledAtUtc = new DateTimeOffset(2023, 12, 20, 0, 0, 0, TimeSpan.Zero),
            Notes = "Ready for publication",
            ProjectId = project.Id,
            Project = project
        };

        var grantedWithoutDate = new IprRecord
        {
            IprFilingNumber = "IPR-011",
            Title = "Helios",
            Type = IprType.Copyright,
            Status = IprStatus.Granted,
            FiledAtUtc = null,
            Notes = "Awaiting certificate",
            ProjectId = null
        };

        var otherRecord = new IprRecord
        {
            IprFilingNumber = "IPR-012",
            Title = "Iota",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            FiledAtUtc = new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero)
        };

        db.IprRecords.AddRange(grantedWithDate, grantedWithoutDate, otherRecord);
        await db.SaveChangesAsync();

        var service = new IprReadService(db);

        var filter = new IprFilter
        {
            Types = new[] { IprType.Copyright },
            Statuses = new[] { IprStatus.Granted }
        };

        var rows = await service.GetExportAsync(filter);

        Assert.Equal(2, rows.Count);
        Assert.Equal("IPR-010", rows[0].FilingNumber);
        Assert.Equal("Gamma", rows[0].Title);
        Assert.Equal(project.Name, rows[0].ProjectName);
        Assert.Equal("Ready for publication", rows[0].Remarks);
        Assert.Equal(new DateTimeOffset(2023, 12, 20, 0, 0, 0, TimeSpan.Zero), rows[0].FiledAtUtc);

        Assert.Equal("IPR-011", rows[1].FilingNumber);
        Assert.Null(rows[1].ProjectName);
        Assert.Null(rows[1].FiledAtUtc);
        Assert.Equal("Awaiting certificate", rows[1].Remarks);
    }

    [Fact]
    public async Task SearchAsync_Attachments_ExcludeArchivedAndIncludeMetadata()
    {
        await using var db = CreateDbContext();

        var uploaderWithName = new ApplicationUser
        {
            Id = "user-1",
            FullName = "Taylor Swift",
            UserName = "tswift"
        };

        var uploaderWithUserName = new ApplicationUser
        {
            Id = "user-2",
            UserName = "analyst-2"
        };

        db.Users.AddRange(uploaderWithName, uploaderWithUserName);

        var record = new IprRecord
        {
            IprFilingNumber = "IPR-200",
            Title = "Lambda",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            Attachments = new List<IprAttachment>
            {
                new IprAttachment
                {
                    StorageKey = "visible-1",
                    OriginalFileName = "visible-1.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    UploadedByUserId = uploaderWithName.Id,
                    UploadedAtUtc = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero)
                },
                new IprAttachment
                {
                    StorageKey = "visible-2",
                    OriginalFileName = "visible-2.pdf",
                    ContentType = "application/pdf",
                    FileSize = 2048,
                    UploadedByUserId = uploaderWithUserName.Id,
                    UploadedAtUtc = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)
                },
                new IprAttachment
                {
                    StorageKey = "archived",
                    OriginalFileName = "archived.pdf",
                    ContentType = "application/pdf",
                    FileSize = 512,
                    UploadedByUserId = "user-3",
                    IsArchived = true
                }
            }
        };

        db.IprRecords.Add(record);
        await db.SaveChangesAsync();

        var service = new IprReadService(db);

        var result = await service.SearchAsync(new IprFilter());

        var item = Assert.Single(result.Items);
        Assert.Equal(2, item.AttachmentCount);
        Assert.Equal(2, item.Attachments.Count);
        Assert.Equal(new[] { "visible-2.pdf", "visible-1.pdf" }, item.Attachments.Select(a => a.FileName).ToArray());
        Assert.Equal("Taylor Swift", item.Attachments[0].UploadedBy);
        Assert.Equal("analyst-2", item.Attachments[1].UploadedBy);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), item.Attachments[0].UploadedAtUtc);
    }

    [Fact]
    public async Task SearchAsync_ClampsOutOfRangePageToLastAvailablePage()
    {
        await using var db = CreateDbContext();

        db.IprRecords.AddRange(
            new IprRecord { IprFilingNumber = "IPR-301", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-302", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-303", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        var filter = new IprFilter { Page = 99, PageSize = 2 };
        var result = await new IprReadService(db).SearchAsync(filter);

        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("IPR-303", result.Items[0].FilingNumber);
    }

    [Fact]
    public async Task GetPageNumberForRecordAsync_UsesFilteredRegisterOrdering()
    {
        await using var db = CreateDbContext();

        var records = Enumerable.Range(1, 5)
            .Select(index => new IprRecord
            {
                IprFilingNumber = $"IPR-40{index}",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2026, 6 - index, 1, 0, 0, 0, TimeSpan.Zero)
            })
            .ToArray();

        db.IprRecords.AddRange(records);
        await db.SaveChangesAsync();

        var filter = new IprFilter { PageSize = 2 };
        var pageNumber = await new IprReadService(db)
            .GetPageNumberForRecordAsync(filter, records[2].Id);

        Assert.Equal(2, pageNumber);
    }

    [Fact]
    public async Task GetPageNumberForRecordAsync_ReturnsNullWhenRecordDoesNotMatchFilter()
    {
        await using var db = CreateDbContext();

        var patent = new IprRecord
        {
            IprFilingNumber = "IPR-501",
            Type = IprType.Patent,
            Status = IprStatus.Filed
        };
        db.IprRecords.Add(patent);
        await db.SaveChangesAsync();

        var filter = new IprFilter
        {
            Types = new[] { IprType.Copyright },
            PageSize = 15
        };

        var pageNumber = await new IprReadService(db)
            .GetPageNumberForRecordAsync(filter, patent.Id);

        Assert.Null(pageNumber);
    }

    [Fact]
    public async Task SearchAsync_FiltersByGrantOrRegistrationYear()
    {
        await using var db = CreateDbContext();

        db.IprRecords.AddRange(
            new IprRecord
            {
                IprFilingNumber = "IPR-601",
                Title = "Protected in 2025",
                Type = IprType.Patent,
                Status = IprStatus.Granted,
                FiledAtUtc = new DateTimeOffset(2023, 4, 1, 0, 0, 0, TimeSpan.Zero),
                GrantedAtUtc = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero)
            },
            new IprRecord
            {
                IprFilingNumber = "IPR-602",
                Title = "Protected in 2024",
                Type = IprType.Copyright,
                Status = IprStatus.Granted,
                FiledAtUtc = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
                GrantedAtUtc = new DateTimeOffset(2024, 12, 20, 0, 0, 0, TimeSpan.Zero)
            },
            new IprRecord
            {
                IprFilingNumber = "IPR-603",
                Title = "Still pending",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2025, 2, 2, 0, 0, 0, TimeSpan.Zero)
            });
        await db.SaveChangesAsync();

        var filter = new IprFilter
        {
            DateBasis = IprDateBasis.Protected,
            Year = 2025
        };

        var result = await new IprReadService(db).SearchAsync(filter);

        var item = Assert.Single(result.Items);
        Assert.Equal("IPR-601", item.FilingNumber);
    }

    [Fact]
    public async Task SearchAsync_FiltersByProjectLinkageAndEvidenceState()
    {
        await using var db = CreateDbContext();

        var project = new Project { Name = "Linked project", CreatedByUserId = "owner" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var linkedWithEvidence = new IprRecord
        {
            IprFilingNumber = "IPR-701",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            ProjectId = project.Id,
            Attachments = new List<IprAttachment>
            {
                new()
                {
                    StorageKey = "evidence-701",
                    OriginalFileName = "evidence.pdf",
                    ContentType = "application/pdf",
                    FileSize = 128,
                    UploadedByUserId = "owner"
                }
            }
        };
        var unassignedWithoutEvidence = new IprRecord
        {
            IprFilingNumber = "IPR-702",
            Type = IprType.Copyright,
            Status = IprStatus.Filed
        };
        var unassignedWithArchivedEvidence = new IprRecord
        {
            IprFilingNumber = "IPR-703",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            Attachments = new List<IprAttachment>
            {
                new()
                {
                    StorageKey = "archived-703",
                    OriginalFileName = "archived.pdf",
                    ContentType = "application/pdf",
                    FileSize = 128,
                    UploadedByUserId = "owner",
                    IsArchived = true
                }
            }
        };

        db.IprRecords.AddRange(linkedWithEvidence, unassignedWithoutEvidence, unassignedWithArchivedEvidence);
        await db.SaveChangesAsync();

        var filter = new IprFilter
        {
            Linkage = IprLinkageFilter.Unassigned,
            Evidence = IprEvidenceFilter.Missing
        };

        var result = await new IprReadService(db).SearchAsync(filter);

        Assert.Equal(2, result.Total);
        Assert.Equal(new[] { "IPR-702", "IPR-703" }, result.Items.Select(item => item.FilingNumber).OrderBy(value => value).ToArray());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
