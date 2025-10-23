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
                Status = IprStatus.Draft,
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
                Type = IprType.Trademark,
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
                Status = IprStatus.Draft
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
                Type = IprType.Trademark,
                Status = IprStatus.Granted
            },
            new()
            {
                IprFilingNumber = "IPR-104",
                Title = "Quasar",
                Type = IprType.Trademark,
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
        Assert.Equal(0, kpis.Draft);
        Assert.Equal(0, kpis.Rejected);
        Assert.Equal(0, kpis.Expired);
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
            Type = IprType.Trademark,
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
            Type = IprType.Trademark,
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
            Types = new[] { IprType.Trademark },
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
