using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests;

public class ProliferationControllerPreferenceTests
{
    [Fact]
    public async Task GetPreferenceOverrides_ReturnsFilteredResults()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Gamma",
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            RowVersion = Guid.NewGuid().ToByteArray(),
            CaseFileNumber = "PG-1"
        });

        context.Users.Add(new ApplicationUser { Id = "user-override", FullName = "Override Owner" });

        var now = DateTime.UtcNow;
        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = 2025,
            Mode = YearPreferenceMode.UseGranular,
            SetByUserId = "user-override",
            SetOnUtc = now
        });

        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2025, 6, 1),
            Quantity = 20,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "submitter",
            ApprovedByUserId = "approver",
            ApprovedOnUtc = now,
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var readService = new ProliferationTrackerReadService(new ProliferationAggregateReadService(context));
        var overviewService = new ProliferationOverviewService(context, readService);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: overviewService,
            aggregateSvc: new ProliferationAggregateReadService(context),
            exportService: new StubProliferationExportService(),
            dataQualityService: null!,
            logger: NullLogger<ProliferationController>.Instance);

        var result = await controller.GetPreferenceOverrides(
            new ProliferationPreferenceOverrideQueryDto { Source = ProliferationSource.Sdd },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<ProliferationPreferenceOverrideDto>>(ok.Value);
        var row = Assert.Single(payload);
        Assert.Equal("Project Gamma", row.ProjectName);
        Assert.Equal(YearPreferenceMode.UseGranular, row.EffectiveMode);
        Assert.True(row.HasGranular);
        Assert.Equal(20, row.EffectiveTotal);
    }


    [Fact]
    public async Task ProjectLookupAndGroupedEntries_ReturnSelectedProjectAndLazyDetails()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        context.Projects.Add(new Project
        {
            Id = 77,
            Name = "VR Multi Mode Simulator",
            CaseFileNumber = "VRM-77",
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        context.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 77,
            Source = ProliferationSource.Sdd,
            UnitName = "20 JAT",
            ProliferationDate = new DateOnly(2025, 7, 9),
            Quantity = 2,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "submitter",
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        });
        await context.SaveChangesAsync();

        var aggregate = new ProliferationAggregateReadService(context);
        var readService = new ProliferationTrackerReadService(aggregate);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: new ProliferationOverviewService(context, readService),
            aggregateSvc: aggregate,
            exportService: new StubProliferationExportService(),
            dataQualityService: null!,
            logger: NullLogger<ProliferationController>.Instance);

        var projectResult = await controller.GetEligibleProjectById(77, null, null, CancellationToken.None);
        var projectOk = Assert.IsType<OkObjectResult>(projectResult.Result);
        var project = Assert.IsType<ProliferationProjectLookupDto>(projectOk.Value);
        Assert.Equal("VRM-77", project.Code);

        var groupResult = await controller.GetGroupedRecords(
            new ProliferationGroupedQueryDto { ProjectId = 77, Page = 1, PageSize = 25 },
            CancellationToken.None);
        var groupOk = Assert.IsType<OkObjectResult>(groupResult.Result);
        var groups = Assert.IsType<ProliferationGroupedResponseDto>(groupOk.Value);
        var group = Assert.Single(groups.Items);
        Assert.Equal(2, group.DetailedQuantity);
        Assert.Empty(group.DetailedEntries);

        var entryResult = await controller.GetGroupedDetailedEntries(
            77,
            ProliferationSource.Sdd,
            2025,
            CancellationToken.None);
        var entryOk = Assert.IsType<OkObjectResult>(entryResult.Result);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<ProliferationGroupedDetailedEntryDto>>(entryOk.Value);
        Assert.Equal("20 JAT", Assert.Single(entries).UnitName);
    }

    [Fact]
    public async Task GetEligibleProjects_RanksExactAcronymBeforeNameMatches_AndIgnoresPunctuation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "VR based Multi Mode Dvg Sml (VRMMDS)",
                CaseFileNumber = "30102/VRMMDS/SDD/24",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            },
            new Project
            {
                Id = 2,
                Name = "VR Multi Mode Display Study",
                CaseFileNumber = "VR-MMDS-02",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                RowVersion = Guid.NewGuid().ToByteArray()
            },
            new Project
            {
                Id = 3,
                Name = "Active project must not be returned",
                CaseFileNumber = "VRMMDS",
                CreatedByUserId = "creator",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                RowVersion = Guid.NewGuid().ToByteArray()
            });
        await context.SaveChangesAsync();

        var aggregate = new ProliferationAggregateReadService(context);
        var readService = new ProliferationTrackerReadService(aggregate);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: new ProliferationOverviewService(context, readService),
            aggregateSvc: aggregate,
            exportService: new StubProliferationExportService(),
            dataQualityService: null!,
            logger: NullLogger<ProliferationController>.Instance);

        var result = await controller.GetEligibleProjects("vr mmds", null, null, 200, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<ProliferationProjectLookupResponseDto>(ok.Value);

        Assert.Equal(2, payload.Total);
        Assert.Equal(1, payload.Items[0].Id);
        Assert.Equal("VRMMDS", payload.Items[0].Acronym);
        Assert.Equal("30102/VRMMDS/SDD/24", payload.Items[0].Code);
        Assert.DoesNotContain(payload.Items, item => item.Id == 3);
    }

    [Fact]
    public void GetPreferenceOverrides_IsProtectedByManagePolicy()
    {
        var method = typeof(ProliferationController)
            .GetMethod(nameof(ProliferationController.GetPreferenceOverrides));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(ProjectOfficeReportsPolicies.ManageProliferationPreferences, authorize!.Policy);
    }

    [Fact]
    public async Task ExportPreferenceOverrides_ReturnsCsv()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Delta",
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            RowVersion = Guid.NewGuid().ToByteArray(),
            CaseFileNumber = "PD-1"
        });

        context.Users.Add(new ApplicationUser { Id = "user-override", FullName = "Override Owner" });

        var now = DateTime.UtcNow;
        context.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            Mode = YearPreferenceMode.UseYearly,
            SetByUserId = "user-override",
            SetOnUtc = now
        });

        context.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            TotalQuantity = 120,
            ApprovalStatus = ApprovalStatus.Approved,
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        });

        await context.SaveChangesAsync();

        var readService = new ProliferationTrackerReadService(new ProliferationAggregateReadService(context));
        var overviewService = new ProliferationOverviewService(context, readService);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: overviewService,
            aggregateSvc: new ProliferationAggregateReadService(context),
            exportService: new StubProliferationExportService(),
            dataQualityService: null!,
            logger: NullLogger<ProliferationController>.Instance);

        var result = await controller.ExportPreferenceOverrides(
            new ProliferationPreferenceOverrideQueryDto { Source = ProliferationSource.Sdd },
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("proliferation-counting-exceptions-", file.FileDownloadName);

        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Project,Project Code,Source,Year", csv);
        Assert.Contains("Project Delta", csv);
        Assert.Contains("Yes", csv); // Has Approved Yearly
    }

    [Fact]
    public void ExportPreferenceOverrides_IsProtectedByManagePolicy()
    {
        var method = typeof(ProliferationController)
            .GetMethod(nameof(ProliferationController.ExportPreferenceOverrides));
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(ProjectOfficeReportsPolicies.ManageProliferationPreferences, authorize!.Policy);
    }

    private sealed class StubProliferationExportService : IProliferationExportService
    {
        public Task<ProliferationExportResult> ExportAsync(ProliferationExportRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
