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

        var readService = new ProliferationTrackerReadService(context);
        var overviewService = new ProliferationOverviewService(context, readService);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: overviewService,
            exportService: new StubProliferationExportService(),
            logger: NullLogger<ProliferationController>.Instance);

        var result = await controller.GetPreferenceOverrides(
            new ProliferationPreferenceOverrideQueryDto { Source = ProliferationSource.Sdd },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<ProliferationPreferenceOverrideDto>>(ok.Value);
        var row = Assert.Single(payload);
        Assert.Equal("Project Gamma", row.ProjectName);
        Assert.Equal(YearPreferenceMode.UseGranular, row.EffectiveMode);
        Assert.True(row.HasApprovedGranular);
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

        var readService = new ProliferationTrackerReadService(context);
        var overviewService = new ProliferationOverviewService(context, readService);
        var controller = new ProliferationController(
            context,
            readService,
            submitSvc: null!,
            manageSvc: new ProliferationManageService(context),
            overviewSvc: overviewService,
            exportService: new StubProliferationExportService(),
            logger: NullLogger<ProliferationController>.Instance);

        var result = await controller.ExportPreferenceOverrides(
            new ProliferationPreferenceOverrideQueryDto { Source = ProliferationSource.Sdd },
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("proliferation-preference-overrides-", file.FileDownloadName);

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
