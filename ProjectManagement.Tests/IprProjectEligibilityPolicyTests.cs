using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Tests;

public sealed class IprProjectEligibilityPolicyTests
{
    [Fact]
    public void Policy_AllowsArchivedOriginalProjects_ButRejectsDeletedAndRepeatBuildProjects()
    {
        var eligible = IprProjectEligibilityPolicy.EligibleProjectPredicate.Compile();

        Assert.True(eligible(new Project { IsArchived = true, IsBuild = false, IsDeleted = false }));
        Assert.False(eligible(new Project { IsArchived = false, IsBuild = true, IsDeleted = false }));
        Assert.False(eligible(new Project { IsArchived = false, IsBuild = false, IsDeleted = true }));
    }

    [Fact]
    public async Task DetachLinkedRecordsAsync_ClearsProjectOnlyOnLinkedIprRecords()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Repeat", CreatedByUserId = "user", IsBuild = true },
            new Project { Id = 2, Name = "Original", CreatedByUserId = "user", IsBuild = false });
        db.IprRecords.AddRange(
            NewIpr("IPR-1", 1),
            NewIpr("IPR-2", 1),
            NewIpr("IPR-3", 2),
            NewIpr("IPR-4", null));
        await db.SaveChangesAsync();

        var detached = await IprProjectLinkMaintenance.DetachLinkedRecordsAsync(db, 1);
        await db.SaveChangesAsync();

        Assert.Equal(2, detached);
        var rows = await db.IprRecords.AsNoTracking().OrderBy(row => row.IprFilingNumber).ToListAsync();
        Assert.Null(rows[0].ProjectId);
        Assert.Null(rows[1].ProjectId);
        Assert.Equal(2, rows[2].ProjectId);
        Assert.Null(rows[3].ProjectId);
    }

    private static IprRecord NewIpr(string number, int? projectId)
        => new()
        {
            IprFilingNumber = number,
            Title = number,
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            FiledAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ProjectId = projectId
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
