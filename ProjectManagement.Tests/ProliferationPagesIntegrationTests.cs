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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Xunit;

using AdminIndexModel = ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Admin.IndexModel;
using GranularIndexModel = ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Granular.IndexModel;
using ReconciliationIndexModel = ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Reconciliation.IndexModel;
using YearlyIndexModel = ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation.Yearly.IndexModel;

namespace ProjectManagement.Tests;

public sealed class ProliferationPagesIntegrationTests
{
    [Fact]
    public async Task YearlyPage_SubmitDisplaysWarningWhenGranularPreferred()
    {
        await using var db = CreateContext();
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new FakeAudit();
        var submissionService = new ProliferationSubmissionService(
            db,
            clock,
            audit,
            NullLogger<ProliferationSubmissionService>.Instance);
        var auth = StubAuthorizationService.AllowAll();
        var userManager = CreateUserManager(db);

        var unit = new SponsoringUnit
        {
            Id = 1,
            Name = "Unit A",
            IsActive = true,
            SortOrder = 1
        };

        var project = new Project
        {
            Id = 1,
            Name = "Completed Project",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            SponsoringUnitId = unit.Id,
            LeadPoUserId = "sim-user"
        };

        db.SponsoringUnits.Add(unit);
        db.Projects.Add(project);
        db.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Internal,
            Year = 2024,
            Granularity = ProliferationGranularity.Monthly,
            Period = 1,
            Metrics = new ProliferationMetrics
            {
                DirectBeneficiaries = 25,
                IndirectBeneficiaries = 40,
                InvestmentValue = 1200m
            },
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        });

        db.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Internal,
            UserId = "user-1",
            Year = 2024,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "user-1",
            LastModifiedAtUtc = clock.UtcNow,
            LastModifiedByUserId = "user-1",
            RowVersion = Guid.NewGuid().ToByteArray()
        });

        await db.SaveChangesAsync();

        var page = new YearlyIndexModel(
            db,
            submissionService,
            auth,
            userManager,
            NullLogger<YearlyIndexModel>.Instance);

        ConfigurePage(page, CreateUserPrincipal("user-1"));

        page.Input = new YearlyIndexModel.YearlyInput
        {
            ProjectId = project.Id,
            Source = ProliferationSource.Internal,
            Year = 2024,
            DirectBeneficiaries = 100,
            IndirectBeneficiaries = 200,
            InvestmentValue = 5000m,
            Notes = "Updated yearly totals"
        };

        var result = await page.OnPostSubmitAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Yearly totals submitted for approval.", page.TempData["ToastMessage"]);
        Assert.Equal(
            "Yearly totals were submitted, but granular data is currently preferred. Update the preference if yearly metrics should take precedence.",
            page.TempData["ToastWarning"]);
    }

    [Fact]
    public async Task GranularPage_DecideReturnsForbidWhenUserCannotApprove()
    {
        await using var db = CreateContext();
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new FakeAudit();
        var submissionService = new ProliferationSubmissionService(
            db,
            clock,
            audit,
            NullLogger<ProliferationSubmissionService>.Instance);
        var auth = StubAuthorizationService.DenyPolicy(ProjectOfficeReportsPolicies.ApproveProliferationTracker);
        var userManager = CreateUserManager(db);

        var project = new Project
        {
            Id = 7,
            Name = "Granular Project",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.Add(project);
        db.ProliferationGranularRequests.Add(new ProliferationGranularRequest
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Internal,
            Year = 2024,
            Granularity = ProliferationGranularity.Quarterly,
            Period = 2,
            Metrics = new ProliferationMetrics
            {
                DirectBeneficiaries = 30,
                IndirectBeneficiaries = 45,
                InvestmentValue = 1500m
            },
            SubmittedAtUtc = clock.UtcNow,
            SubmittedByUserId = "submitter",
            DecisionState = ProliferationRequestDecisionState.Pending,
            RowVersion = Guid.NewGuid().ToByteArray()
        });

        await db.SaveChangesAsync();

        var page = new GranularIndexModel(
            db,
            submissionService,
            auth,
            userManager,
            NullLogger<GranularIndexModel>.Instance)
        {
            Decision = new GranularIndexModel.DecisionInput
            {
                RequestId = db.ProliferationGranularRequests.Single().Id,
                Approve = true,
                ProjectId = project.Id,
                Source = ProliferationSource.Internal,
                Year = 2024,
                Granularity = ProliferationGranularity.Quarterly,
                Period = 2,
                RowVersion = Convert.ToBase64String(db.ProliferationGranularRequests.Single().RowVersion)
            }
        };

        ConfigurePage(page, CreateUserPrincipal("approver"));

        var result = await page.OnPostDecideAsync(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(page.CanApprove);
    }

    [Fact]
    public async Task ReconciliationPage_ReflectsUpdatedPreferenceYear()
    {
        await using var db = CreateContext();
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var audit = new FakeAudit();

        var project = new Project
        {
            Id = 11,
            Name = "Reconciliation Project",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LeadPoUserId = "sim-user"
        };

        db.Projects.Add(project);
        db.ProliferationYearlies.Add(new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Internal,
            Year = 2024,
            Metrics = new ProliferationMetrics
            {
                DirectBeneficiaries = 70,
                IndirectBeneficiaries = 140,
                InvestmentValue = 6000m
            },
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed",
            LastModifiedAtUtc = clock.UtcNow,
            LastModifiedByUserId = "seed",
            RowVersion = Guid.NewGuid().ToByteArray()
        });

        await db.SaveChangesAsync();

        var preferenceService = new ProliferationPreferenceService(db, clock, audit);

        var createResult = await preferenceService.SetPreferenceAsync(
            project.Id,
            ProliferationSource.Internal,
            2023,
            "user-2",
            null,
            CancellationToken.None);

        var updateResult = await preferenceService.SetPreferenceAsync(
            project.Id,
            ProliferationSource.Internal,
            2024,
            "user-2",
            createResult.Preference?.RowVersion,
            CancellationToken.None);

        Assert.True(updateResult.IsSuccess);

        var tracker = new ProliferationTrackerReadService(db);
        var userContext = new StubUserContext("user-2");
        var page = new ReconciliationIndexModel(tracker, userContext);

        ConfigurePage(page, CreateUserPrincipal("user-2"));

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var row = Assert.Single(page.Rows);
        Assert.Equal(2024, row.Preference.PreferredYear);
        Assert.Equal(ProliferationPreferenceMode.UseYearly, row.Preference.Mode);
        Assert.True(row.Preference.MatchesPreferredYear);
        Assert.Contains(2024, page.YearOptions);
    }

    [Fact]
    public async Task AdminPage_ImportStoresRejectionFileForDownload()
    {
        var importResult = new ProliferationImportResult(
            ProcessedRows: 3,
            ImportedRows: 1,
            Errors: new[]
            {
                new ProliferationImportRowError(2, "Missing beneficiaries"),
                new ProliferationImportRowError(3, "Invalid investment value")
            },
            RejectionFile: new ProliferationImportFile(
                "rejections.csv",
                Encoding.UTF8.GetBytes("row,error"),
                ProliferationImportFile.CsvContentType));

        var yearlyImport = new StubYearlyImportService(importResult);
        var granularImport = new StubGranularImportService(importResult);
        var exportService = new StubExportService(ProliferationExportResult.Failure("not used"));
        var cache = new MemoryCache(new MemoryCacheOptions());
        await using var db = CreateContext();
        var userManager = CreateUserManager(db);

        var page = new AdminIndexModel(
            yearlyImport,
            granularImport,
            exportService,
            cache,
            userManager,
            NullLogger<AdminIndexModel>.Instance);

        ConfigurePage(page, CreateUserPrincipal("import-user"));

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        page.Yearly = new AdminIndexModel.YearlyImportInput
        {
            Source = ProliferationSource.Internal,
            File = new FormFile(stream, 0, stream.Length, "file", "yearly.csv")
        };

        var result = await page.OnPostImportYearlyAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Imported 1 row(s) from yearly.csv.", page.TempData["ToastMessage"]);
        Assert.Equal("2 row(s) from Internal could not be imported.", page.TempData["ToastWarning"]);

        var token = Assert.IsType<string>(page.TempData["ImportRejectionToken"]);
        Assert.Equal("rejections.csv", page.TempData["ImportRejectionFileName"]);
        Assert.NotNull(page.PendingRejectionDownload);
        Assert.Equal(token, page.PendingRejectionDownload?.Token);

        Assert.True(cache.TryGetValue("ProliferationImport:" + token, out var cached));
        var rejection = Assert.IsType<ProliferationImportFile>(cached);
        Assert.Equal("rejections.csv", rejection.FileName);
        Assert.Equal("row,error", Encoding.UTF8.GetString(rejection.Content));
    }

    [Fact]
    public async Task AdminPage_ExportReturnsGeneratedWorkbook()
    {
        var exportFile = new ProliferationExportFile(
            "tracker.xlsx",
            Encoding.UTF8.GetBytes("excel-bytes"),
            ProliferationExportFile.ExcelContentType);
        var exportResult = ProliferationExportResult.FromFile(exportFile);

        var yearlyImport = new StubYearlyImportService(new ProliferationImportResult(0, 0, Array.Empty<ProliferationImportRowError>(), null));
        var granularImport = new StubGranularImportService(new ProliferationImportResult(0, 0, Array.Empty<ProliferationImportRowError>(), null));
        var exportService = new StubExportService(exportResult);
        var cache = new MemoryCache(new MemoryCacheOptions());
        await using var db = CreateContext();
        var userManager = CreateUserManager(db);

        var page = new AdminIndexModel(
            yearlyImport,
            granularImport,
            exportService,
            cache,
            userManager,
            NullLogger<AdminIndexModel>.Instance)
        {
            Export = new AdminIndexModel.ExportInput
            {
                Source = ProliferationSource.Internal,
                YearFrom = 2022,
                YearTo = 2024,
                SearchTerm = "solar"
            }
        };

        ConfigurePage(page, CreateUserPrincipal("export-user"));

        var result = await page.OnPostExportAsync(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal(ProliferationExportFile.ExcelContentType, fileResult.ContentType);
        Assert.Equal("tracker.xlsx", fileResult.FileDownloadName);
        Assert.Equal("excel-bytes", Encoding.UTF8.GetString(fileResult.FileContents));
        Assert.Equal("Tracker export generated.", page.TempData["ToastMessage"]);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
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

    private static ClaimsPrincipal CreateUserPrincipal(string userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "Test");

        return new ClaimsPrincipal(identity);
    }

    private static void ConfigurePage(PageModel page, ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user ?? CreateUserPrincipal("page-user")
        };

        var services = new ServiceCollection()
            .AddSingleton<ITempDataProvider, InMemoryTempDataProvider>()
            .BuildServiceProvider();

        httpContext.RequestServices = services;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        page.PageContext = new PageContext(actionContext)
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        var tempDataProvider = services.GetRequiredService<ITempDataProvider>();
        page.TempData = new TempDataDictionary(httpContext, tempDataProvider);
        page.Url = new SimpleUrlHelper(page.PageContext);
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly Dictionary<string, AuthorizationResult> _policyResults;

        private StubAuthorizationService(Dictionary<string, AuthorizationResult> policyResults)
        {
            _policyResults = policyResults;
        }

        public static StubAuthorizationService AllowAll()
            => new(new Dictionary<string, AuthorizationResult>());

        public static StubAuthorizationService DenyPolicy(string policy)
            => new(new Dictionary<string, AuthorizationResult>
            {
                [policy] = AuthorizationResult.Failed()
            });

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(AuthorizationResult.Success());
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            if (policyName is not null && _policyResults.TryGetValue(policyName, out var result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(AuthorizationResult.Success());
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(string userId) => UserId = userId;

        public string? UserId { get; }
        public string? UserName => UserId;
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
        public bool IsInRole(string role) => false;
    }

    private sealed class StubYearlyImportService : IProliferationYearlyImportService
    {
        private readonly ProliferationImportResult _result;

        public StubYearlyImportService(ProliferationImportResult result)
        {
            _result = result;
        }

        public Task<ProliferationImportResult> ImportAsync(
            ProliferationYearlyImportRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class StubGranularImportService : IProliferationGranularImportService
    {
        private readonly ProliferationImportResult _result;

        public StubGranularImportService(ProliferationImportResult result)
        {
            _result = result;
        }

        public Task<ProliferationImportResult> ImportAsync(
            ProliferationGranularImportRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class StubExportService : IProliferationExportService
    {
        private readonly ProliferationExportResult _result;

        public StubExportService(ProliferationExportResult result)
        {
            _result = result;
        }

        public Task<ProliferationExportResult> ExportAsync(
            ProliferationExportRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.OrdinalIgnoreCase);

        public IDictionary<string, object?> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase);
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            _data.Clear();
            foreach (var pair in values)
            {
                _data[pair.Key] = pair.Value;
            }
        }
    }
}
