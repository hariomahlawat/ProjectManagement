using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

// The counting-rule preview behind the Records and counting-rule editors. The browser shows
// these numbers; it no longer repeats the counting rule itself.
public sealed class ProliferationCountingPreviewTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(YearPreferenceMode.UseYearly, 0, ProliferationCountedBasis.Annual)]
    [InlineData(YearPreferenceMode.UseYearly, 5, ProliferationCountedBasis.Annual)]
    [InlineData(YearPreferenceMode.UseGranular, 0, ProliferationCountedBasis.Detailed)]
    [InlineData(YearPreferenceMode.Auto, 0, ProliferationCountedBasis.Annual)]
    [InlineData(YearPreferenceMode.Auto, 5, ProliferationCountedBasis.Detailed)]
    [InlineData(YearPreferenceMode.UseYearlyAndGranular, 5, ProliferationCountedBasis.AnnualAndDetailed)]
    public void CountedBasis_AndReportedTotal_AgreeForEveryRule(YearPreferenceMode mode, int detailed, ProliferationCountedBasis expected)
    {
        Assert.Equal(expected, ProliferationAggregateReadService.GetCountedBasis(mode, detailed));

        var total = ProliferationAggregateReadService.CalculateReportedTotal(mode, 10, detailed);
        var expectedTotal = expected switch
        {
            ProliferationCountedBasis.Annual => 10,
            ProliferationCountedBasis.Detailed => detailed,
            _ => 10 + detailed
        };
        Assert.Equal(expectedTotal, total);
    }

    [Fact]
    public async Task DetailedEntryOn515Abw_IsShownAsNotCounted()
    {
        await using var db = CreateContext();
        await SeedAsync(db, Yearly(6, ApprovalStatus.Approved));

        var preview = await Service(db).GetCountingPreviewAsync(
            1, ProliferationSource.Abw515, 2026, null, ProliferationRecordKind.Granular, 3, null, CancellationToken.None);

        Assert.Equal(6, preview.CurrentTotal);
        Assert.Equal(6, preview.PreviewTotal);
        Assert.False(preview.QuantityCounted);
        Assert.Equal(YearPreferenceMode.UseYearly, preview.PreviewMode);
    }

    [Fact]
    public async Task EditingApprovedRecord_ReplacesItsQuantity()
    {
        await using var db = CreateContext();
        var approved = Yearly(6, ApprovalStatus.Approved);
        await SeedAsync(db, approved);

        var preview = await Service(db).GetCountingPreviewAsync(
            1, ProliferationSource.Abw515, 2026, null, ProliferationRecordKind.Yearly, 8, approved.Id, CancellationToken.None);

        Assert.Equal(6, preview.CurrentTotal);
        Assert.Equal(8, preview.PreviewTotal);
        Assert.True(preview.QuantityCounted);
    }

    [Fact]
    public async Task EditingPendingAmendment_ReplacesTheApprovedRecordItAmends()
    {
        await using var db = CreateContext();
        var approved = Yearly(6, ApprovalStatus.Approved);
        var amendment = Yearly(8, ApprovalStatus.Pending);
        amendment.AmendsId = approved.Id;
        await SeedAsync(db, approved, amendment);

        var preview = await Service(db).GetCountingPreviewAsync(
            1, ProliferationSource.Abw515, 2026, null, ProliferationRecordKind.Yearly, 9, amendment.Id, CancellationToken.None);

        Assert.Equal(6, preview.CurrentTotal);
        Assert.Equal(9, preview.PreviewTotal);
    }

    [Fact]
    public async Task ProposedRule_IsAppliedToCurrentFigures()
    {
        await using var db = CreateContext();
        db.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Abw515,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2026, 3, 1),
            Quantity = 3,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "seed",
            CreatedOnUtc = Now,
            LastUpdatedOnUtc = Now,
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        await SeedAsync(db, Yearly(6, ApprovalStatus.Approved));

        var preview = await Service(db).GetCountingPreviewAsync(
            1, ProliferationSource.Abw515, 2026, YearPreferenceMode.UseYearlyAndGranular, null, null, null, CancellationToken.None);

        Assert.Equal(6, preview.CurrentTotal);
        Assert.Equal(9, preview.PreviewTotal);
        Assert.True(preview.HasBothRecordTypes);
        Assert.Null(preview.QuantityCounted);
    }

    private static ProliferationAggregateReadService Service(ApplicationDbContext db) => new(db);

    private static ProliferationYearly Yearly(int quantity, ApprovalStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = 1,
        Source = ProliferationSource.Abw515,
        Year = 2026,
        TotalQuantity = quantity,
        ApprovalStatus = status,
        SubmittedByUserId = "seed",
        CreatedOnUtc = Now,
        LastUpdatedOnUtc = Now,
        RowVersion = Guid.NewGuid().ToByteArray()
    };

    private static async Task SeedAsync(ApplicationDbContext db, params ProliferationYearly[] records)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Simulator",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        db.ProliferationYearlies.AddRange(records);
        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
