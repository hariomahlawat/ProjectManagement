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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
