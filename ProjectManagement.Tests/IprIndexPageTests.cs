using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests;

public sealed class IprIndexPageTests
{
    // A Command desk drill-down sets a date window (from/to on the chosen basis). On the Insights
    // and Action list tabs the chips are server-rendered, so their remove and clear-all links must
    // drop the window, and the basis stays only while a year filter still needs it.
    // A removed value stays in the dictionary as null, which asp-all-route-data leaves out of the URL.
    private static bool Dropped(IDictionary<string, string?> route, string key)
        => !route.TryGetValue(key, out var value) || value is null;

    [Fact]
    public void ChipRoutes_DropTheDateWindow_AndKeepTheBasisOnlyForAYear()
    {
        using var db = CreateDbContext();
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new StubIprReadService(), new StubIprWriteService(), new DenyAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            From = new DateOnly(2026, 9, 26),
            To = new DateOnly(2026, 10, 25),
            DateBasis = IprDateBasis.Protected,
            Statuses = new List<IprStatus> { IprStatus.Granted }
        };

        var withoutRange = page.BuildRemoveFilterRoute(new IndexModel.ActiveFilterChip("range", "Granted 26 Sep – 25 Oct 2026"));
        Assert.True(Dropped(withoutRange, "from"));
        Assert.True(Dropped(withoutRange, "to"));
        Assert.True(Dropped(withoutRange, "dateBasis"));
        Assert.Equal("Granted", withoutRange["statuses[0]"]);

        page.Year = 2026;
        var withoutRangeKeepingYear = page.BuildRemoveFilterRoute(new IndexModel.ActiveFilterChip("range", "Granted 26 Sep – 25 Oct 2026"));
        Assert.Equal("Protected", withoutRangeKeepingYear["dateBasis"]);
        Assert.Equal("2026", withoutRangeKeepingYear["year"]);

        var withoutYear = page.BuildRemoveFilterRoute(new IndexModel.ActiveFilterChip("year", "Grant / registration year: 2026"));
        Assert.True(Dropped(withoutYear, "year"));
        Assert.Equal("Protected", withoutYear["dateBasis"]);
        Assert.Equal("2026-09-26", withoutYear["from"]);

        var cleared = page.BuildClearFiltersRoute();
        Assert.True(Dropped(cleared, "from"));
        Assert.True(Dropped(cleared, "to"));
        Assert.True(Dropped(cleared, "dateBasis"));
    }

    [Fact]
    public async Task OnGetAsync_WhenUserCannotEdit_DisablesEditing()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new DenyAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions());
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));
        page.Mode = "create";

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.CanEdit);
        Assert.Null(page.Mode);
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenAuthorizationFails_ReturnsForbid()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var writeService = new StubIprWriteService();
        var authorizationService = new DenyAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
        {
            Input = new IndexModel.RecordInput
            {
                FilingNumber = "IPR-500",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            }
        };

        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnPostCreateAsync(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenAuthorizedAndValid_OpensTheSavedRecord()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-600",
                    Title = "Copyright training package",
                    Type = IprType.Copyright,
                    Status = IprStatus.Granted,
                    FiledBy = "  Analyst  ",
                    FiledOn = new DateOnly(2024, 2, 1),
                    GrantedOn = new DateOnly(2024, 2, 20)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./Index", redirect.PageName);
            Assert.False(redirect.RouteValues!.ContainsKey("mode"));
            Assert.Equal("records", redirect.RouteValues["tab"]);
            var createdId = Assert.IsType<int>(redirect.RouteValues["selectedRecordId"]);
            Assert.Equal("IPR record created.", page.TempData["ToastMessage"]);

            var created = await db.IprRecords.AsNoTracking().SingleAsync(r => r.Id == createdId);
            Assert.Equal("IPR-600", created.IprFilingNumber);
            Assert.Equal(IprType.Copyright, created.Type);
            Assert.Equal(IprStatus.Granted, created.Status);
            Assert.Equal("Analyst", created.FiledBy);
            Assert.Equal(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), created.FiledAtUtc);
            Assert.Equal(new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.Zero), created.GrantedAtUtc);
        }
        finally
        {
            CleanupRoot(root);
        }
    }


    [Fact]
    public async Task OnPostEditAsync_AssignsProjectAndIgnoresUnrelatedCommandValidation()
    {
        await using var db = CreateDbContext();
        var readService = new IprReadService(db);
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var project = new Project
            {
                Name = "VR based Tac RP",
                CreatedByUserId = "editor",
                LifecycleStatus = ProjectLifecycleStatus.Active
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var created = await writeService.CreateAsync(new IprRecord
            {
                IprFilingNumber = "30102/26",
                Title = "Remote Pilot Aircraft Simulator",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledBy = "Lt Col Suresh Kumar Dash",
                FiledAtUtc = new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero)
            });

            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
            {
                Tab = "records",
                Input = new IndexModel.RecordInput
                {
                    Id = created.Id,
                    FilingNumber = created.IprFilingNumber,
                    Title = created.Title,
                    Type = created.Type,
                    Status = created.Status,
                    FiledBy = created.FiledBy,
                    FiledOn = new DateOnly(2024, 4, 2),
                    ProjectId = project.Id,
                    RowVersion = Convert.ToBase64String(created.RowVersion)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));
            page.ModelState.AddModelError("DeleteRequest.RowVersion", "Unrelated command validation error.");
            page.ModelState.AddModelError("RemoveAttachment.RowVersion", "Unrelated command validation error.");

            var result = await page.OnPostEditAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./Index", redirect.PageName);
            Assert.False(redirect.RouteValues!.ContainsKey("mode"));
            Assert.Equal(created.Id, Assert.IsType<int>(redirect.RouteValues["selectedRecordId"]));
            Assert.Equal("IPR record updated.", page.TempData["ToastMessage"]);
            Assert.DoesNotContain(page.ModelState.Keys, key => key.StartsWith("DeleteRequest", StringComparison.Ordinal));
            Assert.DoesNotContain(page.ModelState.Keys, key => key.StartsWith("RemoveAttachment", StringComparison.Ordinal));

            db.ChangeTracker.Clear();
            var stored = await db.IprRecords.AsNoTracking().SingleAsync(record => record.Id == created.Id);
            Assert.Equal(project.Id, stored.ProjectId);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostEditAsync_WhenProjectWasDeleted_RetainsRegisterAndAddsProjectFieldError()
    {
        await using var db = CreateDbContext();
        var readService = new IprReadService(db);
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var deletedProject = new Project
            {
                Name = "Deleted project",
                CreatedByUserId = "editor",
                IsDeleted = true
            };
            db.Projects.Add(deletedProject);
            await db.SaveChangesAsync();

            var created = await writeService.CreateAsync(new IprRecord
            {
                IprFilingNumber = "IPR-INVALID-PROJECT",
                Title = "Invalid project test",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero)
            });

            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
            {
                Tab = "records",
                Input = new IndexModel.RecordInput
                {
                    Id = created.Id,
                    FilingNumber = created.IprFilingNumber,
                    Title = created.Title,
                    Type = created.Type,
                    Status = created.Status,
                    FiledOn = new DateOnly(2024, 4, 2),
                    ProjectId = deletedProject.Id,
                    RowVersion = Convert.ToBase64String(created.RowVersion)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostEditAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);
            Assert.Equal("edit", page.Mode);
            Assert.Equal(1, page.Kpis.Total);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(deletedProject.Id, page.Input.ProjectId);
            Assert.DoesNotContain(page.ProjectPickerOptions, project => project.Id == deletedProject.Id);

            var key = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.ProjectId)}";
            Assert.True(page.ModelState.TryGetValue(key, out var entry));
            Assert.Equal(
                "The selected project is no longer available. Select another project.",
                Assert.Single(entry.Errors).ErrorMessage);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenFiledStatusMissingFiledOn_AddsFieldError()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db);
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-700",
                    Title = "Filed patent",
                    Type = IprType.Patent,
                    Status = IprStatus.Filed
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);

            var key = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.FiledOn)}";
            Assert.True(page.ModelState.TryGetValue(key, out var entry));
            var message = Assert.Single(entry.Errors).ErrorMessage;
            Assert.Equal("Filed date is required.", message);
            Assert.False(page.ModelState.TryGetValue(string.Empty, out _));
            Assert.Equal("create", page.Mode);
            Assert.Equal("IPR-700", page.Input.FilingNumber);
            Assert.Equal("Filed patent", page.Input.Title);
            Assert.Equal(IprType.Patent, page.Input.Type);
            Assert.Equal(IprStatus.Filed, page.Input.Status);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostCreateAsync_WhenGrantDateBeforeFiledDate_AddsErrorsToBothFields()
    {
        await using var db = CreateDbContext();
        var readService = new StubIprReadService();
        var (writeService, root) = CreateWriteService(db);
        var authorizationService = new AllowAuthorizationService();
        using var userManager = CreateUserManager(db);
        var exportService = new StubIprExportService();

        try
        {
            var page = new IndexModel(db, readService, writeService, authorizationService, userManager, exportService, CreateAttachmentOptions())
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-710",
                    Title = "Granted patent",
                    Type = IprType.Patent,
                    Status = IprStatus.Granted,
                    FiledOn = new DateOnly(2024, 4, 10),
                    GrantedOn = new DateOnly(2024, 3, 15)
                }
            };

            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            Assert.IsType<PageResult>(result);

            var filedKey = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.FiledOn)}";
            Assert.True(page.ModelState.TryGetValue(filedKey, out var filedEntry));
            var filedMessage = Assert.Single(filedEntry.Errors).ErrorMessage;
            Assert.Equal("Grant / registration date cannot be earlier than the filing date.", filedMessage);

            var grantedKey = $"{nameof(IndexModel.Input)}.{nameof(IndexModel.RecordInput.GrantedOn)}";
            Assert.True(page.ModelState.TryGetValue(grantedKey, out var grantedEntry));
            var grantedMessage = Assert.Single(grantedEntry.Errors).ErrorMessage;
            Assert.Equal("Grant / registration date cannot be earlier than the filing date.", grantedMessage);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnGetAsync_BuildsPatentAndCopyrightBreakdownForFiledProtectedAndPending()
    {
        await using var db = CreateDbContext();
        db.IprRecords.AddRange(
            new IprRecord
            {
                IprFilingNumber = "PAT-PENDING",
                Title = "Pending patent",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new IprRecord
            {
                IprFilingNumber = "PAT-GRANTED",
                Title = "Granted patent",
                Type = IprType.Patent,
                Status = IprStatus.Granted,
                FiledAtUtc = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
                GrantedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new IprRecord
            {
                IprFilingNumber = "CR-PENDING",
                Title = "Pending copyright",
                Type = IprType.Copyright,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new IprRecord
            {
                IprFilingNumber = "CR-REGISTERED",
                Title = "Registered copyright",
                Type = IprType.Copyright,
                Status = IprStatus.Granted,
                FiledAtUtc = new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero),
                GrantedAtUtc = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)
            });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(
            db,
            new IprReadService(db),
            new StubIprWriteService(),
            new AllowAuthorizationService(),
            userManager,
            new StubIprExportService(),
            CreateAttachmentOptions());
        ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(4, page.Kpis.Total);
        Assert.Equal(2, page.Kpis.Granted);
        Assert.Equal(2, page.Kpis.Filed);

        var patent = page.TypeBreakdown.Single(item => item.Type == IprType.Patent);
        Assert.Equal("Patent", patent.TypeLabel);
        Assert.Equal(2, patent.Filed);
        Assert.Equal(1, patent.Protected);
        Assert.Equal(1, patent.Pending);
        Assert.Equal("granted", patent.ProtectedVerb);

        var copyright = page.TypeBreakdown.Single(item => item.Type == IprType.Copyright);
        Assert.Equal("Copyright", copyright.TypeLabel);
        Assert.Equal(2, copyright.Filed);
        Assert.Equal(1, copyright.Protected);
        Assert.Equal(1, copyright.Pending);
        Assert.Equal("registered", copyright.ProtectedVerb);
    }

    [Fact]
    public async Task OnPostEditAsync_SavesWithdrawnStatus()
    {
        await using var db = CreateDbContext();
        var readService = new IprReadService(db);
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        using var userManager = CreateUserManager(db);

        try
        {
            var created = await writeService.CreateAsync(new IprRecord
            {
                IprFilingNumber = "IPR-WITHDRAW",
                Title = "Withdrawn filing",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero)
            });

            var page = new IndexModel(db, readService, writeService, new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
            {
                Tab = "records",
                Input = new IndexModel.RecordInput
                {
                    Id = created.Id,
                    FilingNumber = created.IprFilingNumber,
                    Title = created.Title,
                    Type = created.Type,
                    Status = IprStatus.Withdrawn,
                    FiledOn = new DateOnly(2024, 4, 2),
                    RowVersion = Convert.ToBase64String(created.RowVersion)
                }
            };
            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostEditAsync(CancellationToken.None);

            Assert.IsType<RedirectToPageResult>(result);
            db.ChangeTracker.Clear();
            var stored = await db.IprRecords.AsNoTracking().SingleAsync(record => record.Id == created.Id);
            Assert.Equal(IprStatus.Withdrawn, stored.Status);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnGetAsync_LinkToClosedRecord_ShowsItUnderItsStatusAndKeepsCountsPositive()
    {
        await using var db = CreateDbContext();
        var filed = new IprRecord
        {
            IprFilingNumber = "IPR-OPEN",
            Title = "Open filing",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            FiledAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var rejected = new IprRecord
        {
            IprFilingNumber = "IPR-REJECTED",
            Title = "Rejected filing",
            Type = IprType.Patent,
            Status = IprStatus.Rejected,
            FiledAtUtc = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        db.IprRecords.AddRange(filed, rejected);
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(
            db,
            new IprReadService(db),
            new StubIprWriteService(),
            new AllowAuthorizationService(),
            userManager,
            new StubIprExportService(),
            CreateAttachmentOptions())
        {
            Tab = "records",
            SelectedRecordId = rejected.Id
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(new[] { IprStatus.Rejected }, page.Statuses);
        Assert.Equal(rejected.Id, page.SelectedRecordId);
        Assert.Equal(new[] { filed.Id, rejected.Id }.OrderBy(id => id), page.Records.Select(record => record.Id).OrderBy(id => id));
        Assert.True(page.Records.Single(record => record.Id == rejected.Id).IsClosed);
        Assert.Equal(1, page.Kpis.Total);
        Assert.Equal(1, page.Kpis.Rejected);
        Assert.Equal(0, page.Kpis.Withdrawn);
    }

    [Fact]
    public async Task OnGetAsync_ClosedStatusScope_KeepsRejectedAndWithdrawnTogether()
    {
        await using var db = CreateDbContext();
        db.IprRecords.AddRange(
            new IprRecord { IprFilingNumber = "IPR-SCOPE-FILED", Title = "Filed", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-SCOPE-REJECTED", Title = "Rejected", Type = IprType.Patent, Status = IprStatus.Rejected, FiledAtUtc = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-SCOPE-WITHDRAWN", Title = "Withdrawn", Type = IprType.Patent, Status = IprStatus.Withdrawn, FiledAtUtc = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "records",
            StatusScope = "Closed"
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(new[] { IprStatus.Rejected, IprStatus.Withdrawn }, page.Statuses.OrderBy(status => status).ToArray());
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task OnGetAsync_RetiredProjectsTab_OpensRegisterGroupedByProject()
    {
        await using var db = CreateDbContext();
        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "project"
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("records", page.Tab);
        Assert.Equal("project", page.Group);
    }

    [Fact]
    public async Task OnGetAsync_Register_ListsEveryRecordWithFilteringFacts()
    {
        await using var db = CreateDbContext();
        db.IprRecords.AddRange(
            new IprRecord { IprFilingNumber = "IPR-FACT-P", Title = "Pending", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-FACT-G", Title = "Granted", Type = IprType.Copyright, Status = IprStatus.Granted, FiledAtUtc = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero), GrantedAtUtc = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-FACT-W", Title = "Withdrawn", Type = IprType.Patent, Status = IprStatus.Withdrawn, FiledAtUtc = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "records"
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(3, page.Records.Count);
        var pending = page.Records.Single(record => record.ApplicationNumber == "IPR-FACT-P");
        Assert.True(pending.IsPending);
        Assert.True(pending.IsCounted);
        Assert.False(string.IsNullOrEmpty(pending.PendingAge));
        Assert.Equal("Filed", pending.StatusKey);
        Assert.Equal("Patent", pending.TypeKey);
        var granted = page.Records.Single(record => record.ApplicationNumber == "IPR-FACT-G");
        Assert.Equal(2021, granted.FiledYear);
        Assert.Equal(2022, granted.GrantedYear);
        Assert.Null(granted.PendingAge);
        var withdrawn = page.Records.Single(record => record.ApplicationNumber == "IPR-FACT-W");
        Assert.True(withdrawn.IsClosed);
        Assert.False(withdrawn.IsCounted);
        Assert.Equal(2, page.Kpis.Total);
        Assert.Equal(1, page.Kpis.Withdrawn);
    }

    [Fact]
    public async Task OnGetAsync_AnalyticsFollowsItsScopeWhileSummaryStaysWhole()
    {
        await using var db = CreateDbContext();
        var project = new Project { Name = "Scoped project", CreatedByUserId = "creator" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.IprRecords.AddRange(
            new IprRecord { IprFilingNumber = "IPR-SC-1", Title = "In scope, no evidence", Type = IprType.Patent, Status = IprStatus.Filed, ProjectId = project.Id, FiledAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-SC-2", Title = "Elsewhere", Type = IprType.Copyright, Status = IprStatus.Granted, FiledAtUtc = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), GrantedAtUtc = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-SC-3", Title = "Elsewhere too", Type = IprType.Patent, Status = IprStatus.Granted, FiledAtUtc = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero), GrantedAtUtc = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "analytics",
            ProjectId = project.Id
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(3, page.Kpis.Total);
        Assert.Equal(1, page.AnalyticsSummary.Total);
        Assert.Equal(1, page.AnalyticsSummary.MissingAttachments);
        Assert.Equal(0, page.AnalyticsSummary.GrantRatePercent);
        var patents = page.AnalyticsSummary.TypeRows.Single(row => row.Type == IprType.Patent);
        Assert.Equal(1, patents.Filed);
        Assert.Equal(0, patents.Protected);
        Assert.Equal(0, page.AnalyticsSummary.TypeRows.Single(row => row.Type == IprType.Copyright).Filed);
    }

    [Fact]
    public async Task OnPostCreateAsync_WithEvidencePdf_SavesRecordAndAttachmentInOneSubmit()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = "editor", UserName = "editor" });
        await db.SaveChangesAsync();
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        using var userManager = CreateUserManager(db);

        try
        {
            await using var pdf = IprWriteServiceTests.CreateValidPdfStream();
            var page = new IndexModel(db, new StubIprReadService(), writeService, new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-PDF-1",
                    Title = "Filed with receipt",
                    Type = IprType.Patent,
                    Status = IprStatus.Filed,
                    FiledOn = new DateOnly(2024, 2, 1),
                    Evidence = CreateFormFile(pdf, "receipt.pdf", "application/pdf")
                }
            };
            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            var createdId = Assert.IsType<int>(redirect.RouteValues?["selectedRecordId"]);
            Assert.Equal("IPR record created. PDF attached.", page.TempData["ToastMessage"]);
            var attachment = await db.IprAttachments.AsNoTracking().SingleAsync();
            Assert.Equal(createdId, attachment.IprRecordId);
            Assert.Equal("receipt.pdf", attachment.OriginalFileName);
            Assert.Equal("editor", attachment.UploadedByUserId);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnPostCreateAsync_WithInvalidEvidence_KeepsRecordAndReopensEditorWithReason()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = "editor", UserName = "editor" });
        await db.SaveChangesAsync();
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        using var userManager = CreateUserManager(db);

        try
        {
            await using var notPdf = new MemoryStream(Encoding.ASCII.GetBytes("plain text, not a PDF"));
            var page = new IndexModel(db, new StubIprReadService(), writeService, new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
            {
                Input = new IndexModel.RecordInput
                {
                    FilingNumber = "IPR-PDF-2",
                    Title = "Filed with a bad file",
                    Type = IprType.Patent,
                    Status = IprStatus.Filed,
                    FiledOn = new DateOnly(2024, 2, 1),
                    Evidence = CreateFormFile(notPdf, "receipt.pdf", "application/pdf")
                }
            };
            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostCreateAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("edit", redirect.RouteValues?["mode"]);
            var createdId = Assert.IsType<int>(redirect.RouteValues?["id"]);
            Assert.Single(await db.IprRecords.AsNoTracking().Where(record => record.Id == createdId).ToListAsync());
            Assert.Empty(await db.IprAttachments.AsNoTracking().ToListAsync());
            Assert.StartsWith("Record saved, but the PDF was not attached:", page.TempData["ToastError"] as string);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnGetAsync_ActionList_ListsEachFilingOnceAndCountsEveryIssue()
    {
        await using var db = CreateDbContext();
        db.IprRecords.AddRange(
            // Long pending, unlinked and without evidence: one row, under "overdue".
            new IprRecord { IprFilingNumber = "IPR-ACT-1", Title = "Old filing", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            // Recent, unlinked, no evidence: one row, under "linkage".
            new IprRecord { IprFilingNumber = "IPR-ACT-2", Title = "New filing", Type = IprType.Copyright, Status = IprStatus.Filed, FiledAtUtc = DateTimeOffset.UtcNow.AddMonths(-2) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "followup"
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        await page.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, page.FollowUpTotalCount);
        Assert.Equal(2, page.VisibleAttentionRecordCount);
        Assert.Equal(new[] { "overdue", "linkage" }, page.AttentionGroups.Select(group => group.Key).ToArray());
        Assert.Equal("Long-pending filings", page.AttentionGroups[0].Title);
        Assert.Equal(1, page.AttentionIssueCounts["overdue"]);
        Assert.Equal(2, page.AttentionIssueCounts["linkage"]);
        Assert.Equal(2, page.AttentionIssueCounts["evidence"]);
        Assert.Equal(0, page.AttentionIssueCounts["data"]);
    }

    [Fact]
    public async Task OnPostEditAsync_FromActionList_ReturnsToTheActionList()
    {
        await using var db = CreateDbContext();
        var (writeService, root) = CreateWriteService(db, new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        using var userManager = CreateUserManager(db);

        try
        {
            var created = await writeService.CreateAsync(new IprRecord
            {
                IprFilingNumber = "IPR-ACT-EDIT",
                Title = "Fix from the list",
                Type = IprType.Patent,
                Status = IprStatus.Filed,
                FiledAtUtc = new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero)
            });

            var page = new IndexModel(db, new IprReadService(db), writeService, new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
            {
                Tab = "followup",
                Input = new IndexModel.RecordInput
                {
                    Id = created.Id,
                    FilingNumber = created.IprFilingNumber,
                    Title = created.Title,
                    Type = created.Type,
                    Status = created.Status,
                    FiledOn = new DateOnly(2024, 4, 2),
                    RowVersion = Convert.ToBase64String(created.RowVersion)
                }
            };
            ConfigurePageContext(page, CreatePrincipal("editor", Policies.Ipr.EditAllowedRoles[0]));

            var result = await page.OnPostEditAsync(CancellationToken.None);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("followup", redirect.RouteValues?["tab"]);
            Assert.False(redirect.RouteValues!.ContainsKey("mode"));
            Assert.False(redirect.RouteValues.ContainsKey("selectedRecordId"));
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task OnGetAsync_Insights_BuildsCohortsAndAgeBandsThatMatchTheRegisterFilters()
    {
        await using var db = CreateDbContext();
        db.IprRecords.AddRange(
            new IprRecord { IprFilingNumber = "IPR-INS-1", Title = "2020 granted", Type = IprType.Patent, Status = IprStatus.Granted, FiledAtUtc = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero), GrantedAtUtc = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-INS-2", Title = "2020 pending", Type = IprType.Patent, Status = IprStatus.Filed, FiledAtUtc = new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero) },
            new IprRecord { IprFilingNumber = "IPR-INS-3", Title = "Withdrawn", Type = IprType.Patent, Status = IprStatus.Withdrawn, FiledAtUtc = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();

        using var userManager = CreateUserManager(db);
        var page = new IndexModel(db, new IprReadService(db), new StubIprWriteService(), new AllowAuthorizationService(), userManager, new StubIprExportService(), CreateAttachmentOptions())
        {
            Tab = "analytics"
        };
        ConfigurePageContext(page, CreatePrincipal("viewer", "Viewer"));

        await page.OnGetAsync(CancellationToken.None);

        var cohort = Assert.Single(page.FilingCohorts);
        Assert.Equal(2020, cohort.Year);
        Assert.Equal(2, cohort.Filed);
        Assert.Equal(1, cohort.Granted);
        Assert.Equal(50, cohort.GrantRatePercent);
        Assert.Equal(
            new[] { IprPendingAge.UnderOneYear, IprPendingAge.OneToThreeYears, IprPendingAge.ThreeToFiveYears, IprPendingAge.OverFiveYears },
            page.AwaitingAgeBands.Select(band => band.Band).ToArray());
        Assert.Equal(1, page.AwaitingAgeBands.Single(band => band.Band == IprPendingAge.OverFiveYears).Count);
    }

    private static IFormFile CreateFormFile(Stream content, string fileName, string contentType)
        => new FormFile(content, 0, content.Length, "Input.Evidence", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void ConfigurePageContext(PageModel page, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new(ClaimTypes.Role, role)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
            .BuildServiceProvider();

        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static IOptions<IprAttachmentOptions> CreateAttachmentOptions()
    {
        return Options.Create(new IprAttachmentOptions
        {
            MaxFileSizeBytes = 1024 * 1024,
            AllowedContentTypes = new List<string> { "application/pdf" },
            AllowedExtensions = new List<string> { ".pdf" }
        });
    }

    private static (IIprWriteService Service, string RootPath) CreateWriteService(ApplicationDbContext db, DateTimeOffset? now = null)
    {
        var clock = FakeClock.AtUtc(now ?? new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var options = CreateAttachmentOptions();

        var uploadRootProvider = new TestUploadRootProvider(root);
        var pathResolver = new UploadPathResolver(uploadRootProvider);
        var storage = new IprAttachmentStorage(uploadRootProvider, pathResolver, options);
        var ingestion = new StubDocRepoIngestionService();
        var service = new IprWriteService(db, clock, storage, options, ingestion, NullLogger<IprWriteService>.Instance);
        return (service, root);
    }

    private static void CleanupRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup for temp directories
        }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }

    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(Stream pdfStream, string originalFileName, string sourceModule, string sourceItemId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class StubIprReadService : IIprReadService
    {
        public Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<IprListRowDto>(Array.Empty<IprListRowDto>(), 0, filter.Page, filter.PageSize));

        public Task<IReadOnlyList<IprListRowDto>> ListRegisterAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IprListRowDto>>(Array.Empty<IprListRowDto>());

        public Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprKpis(0, 0, 0, 0, 0, 0));

        public Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IprExportRowDto>>(Array.Empty<IprExportRowDto>());
    }

    private sealed class StubIprWriteService : IIprWriteService
    {
        public Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(record);

        public Task<IprRecord?> UpdateAsync(IprRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult<IprRecord?>(null);

        public Task<bool> DeleteAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IprAttachment> AddAttachmentAsync(int iprRecordId, System.IO.Stream content, string originalFileName, string? contentType, string uploadedByUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IprAttachment());

        public Task<bool> DeleteAttachmentAsync(int attachmentId, byte[] rowVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class StubIprExportService : IIprExportService
    {
        public Task<IprExportFile> ExportAsync(IprFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new IprExportFile("export.xlsx", "application/octet-stream", Array.Empty<byte>()));
    }

    private sealed class DenyAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class AllowAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }
}
