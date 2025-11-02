using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Globalization;
using System.IO;
using System.Linq;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Services.ProjectOfficeReports.Training;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.Storage;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Infrastructure;
using ProjectManagement.Infrastructure.Activities;
using Markdig;
using Ganss.Xss;
using ProjectManagement.Configuration;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Helpers;
using ProjectManagement.Contracts;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Utilities.Reporting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using ProjectManagement.Utilities;
using ProjectManagement.Services.Stages;
using Microsoft.Net.Http.Headers;
using System.Threading;
using ProjectManagement.Features.Analytics;
using ProjectManagement.Features.Remarks;
using ProjectManagement.Features.Users;
using ProjectManagement.Hubs;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using Microsoft.AspNetCore.StaticFiles;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var runForecastBackfill = args.Any(a => string.Equals(a, "--backfill-forecast", StringComparison.OrdinalIgnoreCase));

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Model.Validation", LogLevel.Warning);
builder.Logging.AddFilter("ProjectManagement.Services.TodoService", LogLevel.None);
builder.Logging.AddFilter("ProjectManagement.Services.TodoPurgeWorker", LogLevel.Warning);

var keysDir = Environment.GetEnvironmentVariable("DP_KEYS_DIR");
if (string.IsNullOrWhiteSpace(keysDir))
{
    keysDir = builder.Environment.IsDevelopment()
        ? Path.Combine(AppContext.BaseDirectory, "keys")
        : "/var/pm/keys";
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("ProjectManagement_SDD");

builder.Services.AddMetrics();

builder.Services.AddSignalR();

builder.Services.AddScoped<IsoCountrySeeder>();

// ---------- Database (PostgreSQL) ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

var csb = new NpgsqlConnectionStringBuilder(connectionString);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(csb.ConnectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Optional: smooth over DateTime quirks if you use legacy semantics
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

// ---------- Identity (username/password without email confirmation) ----------
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.SignIn.RequireConfirmedAccount = false;
        o.User.RequireUniqueEmail = false;
        o.Password.RequireDigit = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Project.Create", policy =>
        policy.RequireRole("Admin", "HoD"));
    options.AddPolicy("Checklist.View", policy =>
        policy.RequireAuthenticatedUser());
    options.AddPolicy("Checklist.Edit", policy =>
        policy.RequireRole("MCO", "HoD"));
    options.AddPolicy(ProjectOfficeReportsPolicies.ViewVisits, policy =>
        policy.RequireAuthenticatedUser());
    options.AddPolicy(ProjectOfficeReportsPolicies.ManageVisits, policy =>
        policy.RequireProjectOfficeManager());
    options.AddPolicy(ProjectOfficeReportsPolicies.ManageSocialMediaEvents, policy =>
        policy.RequireProjectOfficeManager());
    options.AddPolicy(ProjectOfficeReportsPolicies.ViewTotTracker, policy =>
        policy.RequireTotTrackerViewer());
    options.AddPolicy(ProjectOfficeReportsPolicies.ManageTotTracker, policy =>
        policy.RequireTotTrackerSubmitter());
    options.AddPolicy(ProjectOfficeReportsPolicies.ApproveTotTracker, policy =>
        policy.RequireTotTrackerApprover());
    options.AddPolicy(ProjectOfficeReportsPolicies.ViewTrainingTracker, policy =>
        policy.RequireTrainingTrackerViewer());
    options.AddPolicy(ProjectOfficeReportsPolicies.ManageTrainingTracker, policy =>
        policy.RequireTrainingTrackerManager());
    options.AddPolicy(ProjectOfficeReportsPolicies.ApproveTrainingTracker, policy =>
        policy.RequireTrainingTrackerApprover());
    options.AddPolicy(ProjectOfficeReportsPolicies.ViewProliferationTracker, policy =>
        policy.RequireProliferationViewer());
    options.AddPolicy(ProjectOfficeReportsPolicies.SubmitProliferationTracker, policy =>
        policy.RequireProliferationSubmitter());
    options.AddPolicy(ProjectOfficeReportsPolicies.ApproveProliferationTracker, policy =>
        policy.RequireProliferationApprover());
    options.AddPolicy(ProjectOfficeReportsPolicies.ManageProliferationPreferences, policy =>
        policy.RequireProliferationPreferenceManager());
    options.AddPolicy(Policies.Ipr.View, policy =>
        policy.RequireRole(Policies.Ipr.ViewAllowedRoles));
    options.AddPolicy(Policies.Ipr.Edit, policy =>
        policy.RequireRole(Policies.Ipr.EditAllowedRoles));
    options.AddPolicy("DocRepo.View", policy =>
        policy.RequireRole("Admin", "HoD", "Project Office", "ProjectOffice"));
    options.AddPolicy("DocRepo.Upload", policy =>
        policy.RequireRole("Admin", "HoD", "Project Office", "ProjectOffice"));
    options.AddPolicy("DocRepo.ManageCategories", policy =>
        policy.RequireRole("Admin"));
});

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Identity/Account/Login";
    opt.AccessDeniedPath = "/Identity/Account/AccessDenied";
    opt.Cookie.Name = "__Host-PMAuth";
    opt.Cookie.Path = "/";
    opt.Cookie.HttpOnly = true;
    opt.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opt.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
    opt.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    opt.SlidingExpiration = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(5);
        o.QueueLimit = 0;
        o.AutoReplenishment = true;
    });
});

builder.Services.AddHsts(o =>
{
    o.Preload = true;
    o.IncludeSubDomains = true;
    o.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
builder.Services.Configure<DocRepoOptions>(builder.Configuration.GetSection("DocRepo"));
builder.Services.AddSingleton<IDocStorage, LocalDocStorageService>();
builder.Services.AddScoped<IActivityTypeRepository, ActivityTypeRepository>();
builder.Services.AddScoped<IActivityInputValidator, ActivityInputValidator>();
builder.Services.AddScoped<IActivityTypeValidator, ActivityTypeValidator>();
builder.Services.AddScoped<IActivityAttachmentValidator, ActivityAttachmentValidator>();
builder.Services.AddScoped<IActivityAttachmentStorage, FileSystemActivityAttachmentStorage>();
builder.Services.AddScoped<IActivityAttachmentManager, ActivityAttachmentManager>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IActivityDeleteRequestService, ActivityDeleteRequestService>();
builder.Services.AddScoped<IActivityNotificationService, ActivityNotificationService>();
builder.Services.AddScoped<IActivityTypeService, ActivityTypeService>();
builder.Services.AddScoped<IActivityExportService, ActivityExportService>();
builder.Services.Configure<UserLifecycleOptions>(
    builder.Configuration.GetSection("UserLifecycle"));
builder.Services.Configure<IprAttachmentOptions>(
    builder.Configuration.GetSection("IprAttachments"));
builder.Services.Configure<FfcAttachmentOptions>(
    builder.Configuration.GetSection("FfcAttachments"));
builder.Services.AddSingleton<IprAttachmentStorage>();
builder.Services.AddScoped<IFileSecurityValidator, FileSecurityValidator>();
builder.Services.AddScoped<IFfcAttachmentStorage, FfcAttachmentStorage>();
builder.Services.AddScoped<IIprReadService, IprReadService>();
builder.Services.AddScoped<IIprWriteService, IprWriteService>();
builder.Services.AddScoped<IUserLifecycleService, UserLifecycleService>();
builder.Services.AddHostedService<UserPurgeWorker>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ITodoService, TodoService>();
builder.Services.Configure<TodoOptions>(
    builder.Configuration.GetSection("Todo"));
builder.Services.AddScoped<ILoginAnalyticsService, LoginAnalyticsService>();
builder.Services.AddHostedService<LoginAggregationWorker>();
builder.Services.AddHostedService<TodoPurgeWorker>();
builder.Services.AddHostedService<ProjectRetentionWorker>();
builder.Services.AddScoped<PlanDraftService>();
builder.Services.AddScoped<PlanApprovalService>();
builder.Services.AddScoped<INavigationProvider, RoleBasedNavigationProvider>();
builder.Services.AddScoped<ProliferationOverviewService>();
builder.Services.AddScoped<IProliferationSummaryReadService, ProliferationSummaryReadService>();
builder.Services.AddScoped<ProliferationSubmissionService>();
builder.Services.AddScoped<ProliferationManageService>();
builder.Services.AddScoped<StageRulesService>();
builder.Services.AddScoped<StageProgressService>();
builder.Services.AddScoped<IStageValidationService, StageValidationService>();
builder.Services.AddScoped<StageRequestService>();
builder.Services.AddScoped<StageDirectApplyService>();
builder.Services.AddScoped<StageBackfillService>();
builder.Services.AddScoped<StageDecisionService>();
builder.Services.AddScoped<PlanSnapshotService>();
builder.Services.AddScoped<PlanCompareService>();
builder.Services.AddScoped<ProjectFactsService>();
builder.Services.AddScoped<ProjectFactsReadService>();
builder.Services.AddScoped<ProjectProcurementReadService>();
builder.Services.AddScoped<ProjectTimelineReadService>();
builder.Services.AddScoped<ProjectLifecycleService>();
builder.Services.AddScoped<ProjectTotService>();
builder.Services.AddScoped<ProjectTotTrackerReadService>();
builder.Services.AddScoped<ProliferationTrackerReadService>();
builder.Services.AddScoped<ProjectCommentService>();
builder.Services.AddScoped<ProjectRemarksPanelService>();
builder.Services.AddScoped<ProjectMediaAggregator>();
builder.Services.AddScoped<ProjectModerationService>();
builder.Services.AddScoped<IRemarkService, RemarkService>();
builder.Services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
builder.Services.AddScoped<IRemarkNotificationService, RemarkNotificationService>();
builder.Services.AddScoped<IPlanNotificationService, PlanNotificationService>();
builder.Services.AddScoped<IStageNotificationService, StageNotificationService>();
builder.Services.AddScoped<IDocumentNotificationService, DocumentNotificationService>();
builder.Services.AddScoped<IRoleNotificationService, RoleNotificationService>();
builder.Services.AddSingleton<IRemarkMetrics, RemarkMetrics>();
builder.Services.AddScoped<INotificationPublisher, NotificationPublisher>();
builder.Services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddOptions<NotificationRetentionOptions>()
    .Bind(builder.Configuration.GetSection("Notifications:Retention"));
builder.Services.AddHostedService<NotificationRetentionService>();
builder.Services.AddScoped<UserNotificationService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IDocumentPreviewTokenService, DocumentPreviewTokenService>();
builder.Services.AddScoped<IDocumentRequestService, DocumentRequestService>();
builder.Services.AddScoped<IDocumentDecisionService, DocumentDecisionService>();
builder.Services.AddScoped<PlanReadService>();
builder.Services.AddScoped<PlanGenerationService>();
builder.Services.AddScoped<IScheduleEngine, ScheduleEngine>();
builder.Services.AddScoped<IForecastWriter, ForecastWriter>();
builder.Services.AddScoped<ForecastBackfillService>();
builder.Services.AddScoped<ProjectMetaChangeRequestService>();
builder.Services.AddScoped<ProjectMetaChangeDecisionService>();
builder.Services.AddScoped<ProjectCategoryHierarchyService>();
builder.Services.AddScoped<IProjectImportService, ProjectImportService>();
builder.Services.AddScoped<IProjectAnalyticsService, ProjectAnalyticsService>();
builder.Services.AddSingleton<IProjectTotExcelWorkbookBuilder, ProjectTotExcelWorkbookBuilder>();
builder.Services.AddSingleton<IVisitExcelWorkbookBuilder, VisitExcelWorkbookBuilder>();
builder.Services.AddSingleton<IVisitPdfReportBuilder, VisitPdfReportBuilder>();
builder.Services.AddSingleton<ISocialMediaExcelWorkbookBuilder, SocialMediaExcelWorkbookBuilder>();
builder.Services.AddSingleton<ISocialMediaPdfReportBuilder, SocialMediaPdfReportBuilder>();
builder.Services.AddSingleton<IProliferationExcelWorkbookBuilder, ProliferationExcelWorkbookBuilder>();
builder.Services.AddSingleton<IIprExcelWorkbookBuilder, IprExcelWorkbookBuilder>();
builder.Services.AddScoped<VisitTypeService>();
builder.Services.AddScoped<SocialMediaEventTypeService>();
builder.Services.AddScoped<SocialMediaPlatformService>();
builder.Services.AddScoped<VisitService>();
builder.Services.AddScoped<SocialMediaEventService>();
builder.Services.AddScoped<IProjectTotExportService, ProjectTotExportService>();
builder.Services.AddScoped<IProliferationExportService, ProliferationExportService>();
builder.Services.AddScoped<IVisitExportService, VisitExportService>();
builder.Services.AddScoped<ISocialMediaExportService, SocialMediaExportService>();
builder.Services.AddScoped<IIprExportService, IprExportService>();
builder.Services.AddScoped<IVisitPhotoService, VisitPhotoService>();
builder.Services.AddScoped<ISocialMediaEventPhotoService, SocialMediaEventPhotoService>();
builder.Services.AddScoped<TrainingTrackerReadService>();
builder.Services.AddScoped<TrainingWriteService>();
builder.Services.AddScoped<ITrainingNotificationService, TrainingNotificationService>();
builder.Services.AddScoped<ITrainingExportService, TrainingExportService>();
builder.Services.AddSingleton<ITrainingExcelWorkbookBuilder, TrainingExcelWorkbookBuilder>();
builder.Services.AddOptions<ProjectPhotoOptions>()
    .Bind(builder.Configuration.GetSection("ProjectPhotos"));
builder.Services.AddOptions<VisitPhotoOptions>()
    .Bind(builder.Configuration.GetSection("ProjectOfficeReports:VisitPhotos"));
builder.Services.AddOptions<SocialMediaPhotoOptions>()
    .Bind(builder.Configuration.GetSection("ProjectOfficeReports:SocialMediaPhotos"));
builder.Services.AddOptions<TrainingTrackerOptions>()
    .Bind(builder.Configuration.GetSection("ProjectOfficeReports:TrainingTracker"));
builder.Services.AddOptions<ProjectDocumentOptions>()
    .Bind(builder.Configuration.GetSection("ProjectDocuments"));
builder.Services.AddOptions<ProjectVideoOptions>()
    .Bind(builder.Configuration.GetSection("ProjectVideos"));
builder.Services.AddSingleton<IConfigureOptions<ProjectPhotoOptions>, ProjectPhotoOptionsSetup>();
builder.Services.AddSingleton<IUploadRootProvider, UploadRootProvider>();
builder.Services.AddScoped<IProjectPhotoService, ProjectPhotoService>();
builder.Services.AddScoped<IProjectVideoService, ProjectVideoService>();
builder.Services.AddOptions<ProjectRetentionOptions>()
    .Bind(builder.Configuration.GetSection("Projects:Retention"));

var enumConverter = new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(enumConverter);
});

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(enumConverter);
});

// Register email sender
if (!string.IsNullOrWhiteSpace(builder.Configuration["Email:Smtp:Host"]))
{
    builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, ProjectManagement.Services.NoOpEmailSender>();
}

builder.Services.AddScoped<EnforcePasswordChangeFilter>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Dashboard");
    options.Conventions.AuthorizeAreaFolder("Admin", "/");
    options.Conventions.AuthorizeAreaFolder(
        "ProjectOfficeReports",
        "/Visits",
        ProjectOfficeReportsPolicies.ViewVisits);
    options.Conventions.AuthorizeAreaFolder(
        "ProjectOfficeReports",
        "/Training",
        ProjectOfficeReportsPolicies.ViewTrainingTracker);
    options.Conventions.AuthorizeAreaPage(
        "ProjectOfficeReports",
        "/Visits/New",
        ProjectOfficeReportsPolicies.ManageVisits);
    options.Conventions.AuthorizeAreaPage(
        "ProjectOfficeReports",
        "/Visits/Edit",
        ProjectOfficeReportsPolicies.ManageVisits);
    options.Conventions.AuthorizeAreaPage(
        "ProjectOfficeReports",
        "/SocialMedia/Create",
        ProjectOfficeReportsPolicies.ManageSocialMediaEvents);
    options.Conventions.AuthorizeAreaPage(
        "ProjectOfficeReports",
        "/SocialMedia/Edit",
        ProjectOfficeReportsPolicies.ManageSocialMediaEvents);
    options.Conventions.AuthorizeAreaPage(
        "ProjectOfficeReports",
        "/SocialMedia/Delete",
        ProjectOfficeReportsPolicies.ManageSocialMediaEvents);
    options.Conventions.AddPageRoute(
        "/ProjectOfficeReports/Ipr/Index",
        "/ProjectOfficeReports/Patent");
    options.Conventions.AddPageRoute(
        "/ProjectOfficeReports/Ipr/Manage",
        "/ProjectOfficeReports/Patent/Manage");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Login");
})
    .AddViewOptions(options =>
    {
        options.HtmlHelperOptions.ClientValidationEnabled = true;
    })
    .AddMvcOptions(o => o.Filters.Add<EnforcePasswordChangeFilter>());

var connectSrcDirective = BuildConnectSrcDirective(builder.Configuration);

var developmentLoopbackOrigins = builder.Environment.IsDevelopment()
    ? ResolveDevelopmentLoopbackOrigins(builder.Configuration)
    : Array.Empty<string>();

var app = builder.Build();

// Ensure the database schema is up to date before handling requests
string? latestAppliedMigration = null;
List<string> pendingMigrations = new();
var databaseIsRelational = false;

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
    {
        databaseIsRelational = true;
        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        latestAppliedMigration = applied.Count > 0 ? applied[^1] : "(none)";

        pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pendingMigrations.Count > 0)
        {
            var message = $"Database has {pendingMigrations.Count} pending migration(s): {string.Join(", ", pendingMigrations)}";
            app.Logger.LogCritical(message);
            throw new InvalidOperationException(message);
        }
    }

    var isoSeeder = scope.ServiceProvider.GetRequiredService<IsoCountrySeeder>();
    await isoSeeder.RunAsync();
}

if (runForecastBackfill)
{
    using var scope = app.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<ForecastBackfillService>();
    var updated = await service.BackfillAsync();
    app.Logger.LogInformation("Backfilled forecast dates for {Count} stages.", updated);
    return;
}

var migrationLabel = latestAppliedMigration ?? (databaseIsRelational ? "(none)" : "(not available)");
app.Logger.LogInformation(
    "Using database {Database} on host {Host}; latest migration {Migration}",
    csb.Database,
    csb.Host,
    migrationLabel);

if (databaseIsRelational && pendingMigrations.Count == 0)
{
    app.Logger.LogInformation("Database schema is up to date.");
}

app.UseForwardedHeaders();

// ---------- HTTP pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), browsing-topics=()";

    var isDocumentViewer = ctx.Request.Path.StartsWithSegments("/Projects/Documents/View", StringComparison.OrdinalIgnoreCase);
    var isDocumentPreview = ctx.Request.Path.StartsWithSegments("/Projects/Documents/Preview", StringComparison.OrdinalIgnoreCase);
    var isAttachmentResponse = ctx.Request.Path.StartsWithSegments("/files", StringComparison.OrdinalIgnoreCase);

    var isDocumentPreviewContext = isDocumentViewer || isDocumentPreview || isAttachmentResponse;

    if (isDocumentPreviewContext)
    {
        // Chrome's PDF viewer ignores content when the response is isolated with
        // COOP. Allow the inline preview to render by opting out for this route.
        h["Cross-Origin-Opener-Policy"] = "unsafe-none";
        // Relax CORP so the browser's extension-based PDF viewers can access the stream.
        h["Cross-Origin-Resource-Policy"] = "cross-origin";
        h["Content-Security-Policy"] = "frame-ancestors 'self'";
    }
    else
    {
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        var devSourcesSuffix = app.Environment.IsDevelopment() && developmentLoopbackOrigins.Length > 0
            ? " " + string.Join(' ', developmentLoopbackOrigins)
            : string.Empty;
        var styleUnsafeInline = app.Environment.IsDevelopment() ? " 'unsafe-inline'" : string.Empty;

        h["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "frame-ancestors 'none'; " +
            "frame-src 'self'; " +
            $"img-src 'self' data: blob:{devSourcesSuffix}; " +
            $"script-src 'self'{devSourcesSuffix}; " +
            $"style-src 'self'{styleUnsafeInline}{devSourcesSuffix}; " +
            $"font-src 'self' data:{devSourcesSuffix}; " +
            $"connect-src {connectSrcDirective};";
    }
    await next();
});

var uploadRoot = app.Services.GetRequiredService<IUploadRootProvider>();

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".geojson"] = "application/geo+json";
contentTypeProvider.Mappings[".topojson"] = "application/json";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadRoot.RootPath),
    RequestPath = "/files",
    ServeUnknownFileTypes = true,
});

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<NotificationsHub>("/hubs/notifications")
    .RequireAuthorization();

app.MapControllers();

// Calendar API endpoints
var eventsApi = app.MapGroup("/calendar/events");

static IEnumerable<CalendarEventVm> BuildCelebrationOccurrences(
    IEnumerable<Celebration> celebrations,
    DateTimeOffset windowStart,
    DateTimeOffset windowEnd)
{
    var tz = IstClock.TimeZone;
    var startLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(windowStart, tz).DateTime);

    foreach (var celebration in celebrations)
    {
        var occurrenceDate = CelebrationHelpers.NextOccurrenceLocal(celebration, startLocalDate);
        var attempts = 0;

        while (attempts < 3)
        {
            var startLocal = CelebrationHelpers.ToLocalDateTime(occurrenceDate);
            var endLocal = CelebrationHelpers.ToLocalDateTime(occurrenceDate.AddDays(1));
            var startUtc = startLocal.ToUniversalTime();
            var endUtc = endLocal.ToUniversalTime();

            if (startUtc >= windowEnd)
            {
                break;
            }

            if (endUtc > windowStart)
            {
                var titlePrefix = celebration.EventType switch
                {
                    CelebrationType.Birthday => "Birthday",
                    CelebrationType.Anniversary => "Anniversary",
                    _ => celebration.EventType.ToString()
                };

                yield return new CalendarEventVm(
                    Id: $"celebration-{celebration.Id:N}-{occurrenceDate:yyyyMMdd}",
                    SeriesId: celebration.Id,
                    Title: $"{titlePrefix}: {CelebrationHelpers.DisplayName(celebration)}",
                    Start: startUtc,
                    End: endUtc,
                    AllDay: true,
                    Category: "Celebration",
                    Location: null,
                    IsRecurring: true,
                    IsCelebration: true,
                    CelebrationId: celebration.Id,
                    TaskUrl: $"/calendar/events/celebrations/{celebration.Id}/task");
            }

            var nextSearch = occurrenceDate.AddDays(1);
            occurrenceDate = CelebrationHelpers.NextOccurrenceLocal(celebration, nextSearch);
            attempts++;
        }
    }
}

eventsApi.MapGet("", async (ApplicationDbContext db,
                             UserManager<ApplicationUser> users,
                             ClaimsPrincipal user,
                             [FromQuery(Name = "start")] DateTimeOffset start,
                             [FromQuery(Name = "end")]   DateTimeOffset end,
                             [FromQuery(Name = "includeCelebrations")] bool? includeCelebrations) =>
{
    // Guard against huge windows
    if ((end - start).TotalDays > 400)
        end = start.AddDays(400);

    var rows = await db.Events
        .Where(e => !e.IsDeleted &&
               (e.RecurrenceRule != null || (e.StartUtc < end && e.EndUtc > start)))
        .ToListAsync();

    var list = new List<CalendarEventVm>();
    foreach (var ev in rows)
    {
        IEnumerable<RecurrenceExpander.Occ> occs;
        try { occs = RecurrenceExpander.Expand(ev, start, end); }
        catch { occs = Array.Empty<RecurrenceExpander.Occ>(); }

        foreach (var o in occs)
        {
            list.Add(new CalendarEventVm(
                Id: o.InstanceId,
                SeriesId: ev.Id,
                Title: ev.Title,
                Start: o.Start,
                End: o.End,
                AllDay: ev.IsAllDay,
                Category: ev.Category.ToString(),
                Location: ev.Location,
                IsRecurring: !string.IsNullOrWhiteSpace(ev.RecurrenceRule),
                IsCelebration: false,
                CelebrationId: null,
                TaskUrl: $"/calendar/events/{ev.Id}/task"));
        }
    }

    bool shouldIncludeCelebrations;
    if (includeCelebrations.HasValue)
    {
        shouldIncludeCelebrations = includeCelebrations.Value;
    }
    else
    {
        var currentUser = await users.GetUserAsync(user);
        shouldIncludeCelebrations = currentUser?.ShowCelebrationsInCalendar ?? true;
    }

    if (shouldIncludeCelebrations)
    {
        var celebrations = await db.Celebrations
            .AsNoTracking()
            .Where(c => c.DeletedUtc == null)
            .ToListAsync();
        list.AddRange(BuildCelebrationOccurrences(celebrations, start, end));
    }

    // sort by start
    var ordered = list
        .OrderBy(x => x.Start)
        .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return Results.Ok(ordered);
}).RequireAuthorization();

eventsApi.MapGet("/holidays", async (
        ApplicationDbContext db,
        [FromQuery(Name = "start")] DateTimeOffset start,
        [FromQuery(Name = "end")] DateTimeOffset end) =>
{
    if (end < start)
    {
        return Results.BadRequest("End must be on or after start.");
    }

    if ((end - start).TotalDays > 400)
    {
        end = start.AddDays(400);
    }

    var tz = IstClock.TimeZone;

    var localStart = TimeZoneInfo.ConvertTime(start, tz);
    var localEnd = TimeZoneInfo.ConvertTime(end, tz);

    var startDate = DateOnly.FromDateTime(localStart.Date);
    var endDate = DateOnly.FromDateTime(localEnd.Date);

    var holidays = await db.Holidays
        .AsNoTracking()
        .Where(h => h.Date >= startDate && h.Date <= endDate)
        .OrderBy(h => h.Date)
        .ToListAsync();

    var items = holidays
        .Select(h =>
        {
            var holidayStartLocal = h.Date.ToDateTime(TimeOnly.MinValue);
            var holidayEndLocal = h.Date.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var startLocalOffset = new DateTimeOffset(holidayStartLocal, tz.GetUtcOffset(holidayStartLocal));
            var endLocalOffset = new DateTimeOffset(holidayEndLocal, tz.GetUtcOffset(holidayEndLocal));

            return new CalendarHolidayVm(
                Date: h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Name: h.Name,
                SkipWeekends: null,
                StartUtc: startLocalOffset.ToUniversalTime(),
                EndUtc: endLocalOffset.ToUniversalTime());
        })
        .ToList();

    return Results.Ok(items);
}).RequireAuthorization();

eventsApi.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) =>
{
    var sanitizer = new HtmlSanitizer();
    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
    if (ev == null) return Results.NotFound();
    var html = ev.Description == null ? null : sanitizer.Sanitize(Markdown.ToHtml(ev.Description));
    return Results.Ok(new
    {
        id = ev.Id,
        title = ev.Title,
        start = ev.StartUtc,
        end = ev.EndUtc,
        allDay = ev.IsAllDay,
        category = ev.Category.ToString(),
        location = ev.Location,
        description = html,
        rawDescription = ev.Description,
        recurrenceRule = ev.RecurrenceRule,
        recurrenceUntilUtc = ev.RecurrenceUntilUtc
    });
}).RequireAuthorization();

eventsApi.MapPost("", async (ApplicationDbContext db,
                           UserManager<ApplicationUser> users,
                           HttpContext ctx,
                           [FromBody] CalendarEventDto dto) =>
{
    if (dto.EndUtc <= dto.StartUtc)
        return Results.BadRequest("EndUtc must be after StartUtc.");

    var cat = CategoryParser.ParseOrOther(dto.Category);

    var uid = users.GetUserId(ctx.User) ?? "";

    var ev = new Event
    {
        Id = Guid.NewGuid(),
        Title = dto.Title.Trim(),
        Description = dto.Description,
        Category = cat,
        Location = dto.Location,
        StartUtc = dto.StartUtc.ToUniversalTime(),
        EndUtc   = dto.EndUtc.ToUniversalTime(),
        IsAllDay = dto.IsAllDay,
        RecurrenceRule = string.IsNullOrWhiteSpace(dto.RecurrenceRule) ? null : dto.RecurrenceRule,
        RecurrenceUntilUtc = dto.RecurrenceUntilUtc?.ToUniversalTime(),
        CreatedById = uid,
        UpdatedById = uid,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    db.Events.Add(ev);
    await db.SaveChangesAsync();
    return Results.Created($"/calendar/events/{ev.Id}", new { id = ev.Id });
}).RequireAuthorization(new AuthorizeAttribute { Roles = "Admin,TA,HoD" });

eventsApi.MapPut("/{id:guid}", async (ApplicationDbContext db,
                                   UserManager<ApplicationUser> users,
                                   HttpContext ctx,
                                   Guid id,
                                   [FromBody] CalendarEventDto dto) =>
{
    var ev = await db.Events.FirstOrDefaultAsync(x => x.Id == id);
    if (ev is null) return Results.NotFound();
    if (dto.EndUtc <= dto.StartUtc) return Results.BadRequest("EndUtc must be after StartUtc.");

    var cat = CategoryParser.ParseOrOther(dto.Category);

    var uid = users.GetUserId(ctx.User) ?? "";

    ev.Title = dto.Title.Trim();
    if (dto.Description != null) ev.Description = dto.Description;
    ev.Category = cat;
    ev.Location = dto.Location;
    ev.StartUtc = dto.StartUtc.ToUniversalTime();
    ev.EndUtc   = dto.EndUtc.ToUniversalTime();
    ev.IsAllDay = dto.IsAllDay;
    ev.RecurrenceRule = string.IsNullOrWhiteSpace(dto.RecurrenceRule) ? null : dto.RecurrenceRule;
    ev.RecurrenceUntilUtc = dto.RecurrenceUntilUtc?.ToUniversalTime();
    ev.UpdatedById = uid;
    ev.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute { Roles = "Admin,TA,HoD" });

eventsApi.MapDelete("/{id:guid}", async (Guid id, ApplicationDbContext db, IClock clock, UserManager<ApplicationUser> users, ClaimsPrincipal user) =>
{
    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
    if (ev == null) return Results.NotFound();
    ev.IsDeleted = true;
    ev.UpdatedAt = clock.UtcNow;
    ev.UpdatedById = users.GetUserId(user);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization(new AuthorizeAttribute { Roles = "Admin,TA,HoD" });

eventsApi.MapPost("/preferences/show-celebrations", async (UserManager<ApplicationUser> users,
                                                           ClaimsPrincipal user,
                                                           [FromBody] ShowCelebrationsPreferenceRequest request) =>
{
    if (request is null)
    {
        return Results.BadRequest();
    }

    var appUser = await users.GetUserAsync(user);
    if (appUser is null)
    {
        return Results.Unauthorized();
    }

    appUser.ShowCelebrationsInCalendar = request.ShowCelebrations;
    var result = await users.UpdateAsync(appUser);
    if (!result.Succeeded)
    {
        var errors = result.Errors
            .GroupBy(e => e.Code ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        return Results.ValidationProblem(errors);
    }

    return Results.Ok(new { showCelebrations = appUser.ShowCelebrationsInCalendar });
}).RequireAuthorization();

var notificationsApi = app.MapGroup("/api/notifications")
    .RequireAuthorization();

notificationsApi.MapGet("", async ([AsParameters] NotificationListRequest request,
                                     HttpContext httpContext,
                                     UserNotificationService notifications,
                                     CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var options = new NotificationListOptions
    {
        Limit = request.Limit,
        OnlyUnread = request.UnreadOnly ?? false,
        ProjectId = request.ProjectId,
    };

    var items = await notifications.ListAsync(httpContext.User, userId, options, cancellationToken);
    return Results.Ok(items);
});

notificationsApi.MapGet("/count", async (HttpContext httpContext,
                                         UserNotificationService notifications,
                                         CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var unread = await notifications.CountUnreadAsync(httpContext.User, userId, cancellationToken);
    return Results.Ok(new NotificationCountDto(unread));
});

notificationsApi.MapPost("/{id:int}/read", async (int id,
                                                   HttpContext httpContext,
                                                   UserNotificationService notifications,
                                                   IHubContext<NotificationsHub, INotificationsClient> hubContext,
                                                   CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await notifications.MarkReadAsync(httpContext.User, userId, id, cancellationToken);

    return await HandleNotificationOperationResultAsync(result, httpContext.User, userId, notifications, hubContext, cancellationToken);
});

notificationsApi.MapDelete("/{id:int}/read", async (int id,
                                                      HttpContext httpContext,
                                                      UserNotificationService notifications,
                                                      IHubContext<NotificationsHub, INotificationsClient> hubContext,
                                                      CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await notifications.MarkUnreadAsync(httpContext.User, userId, id, cancellationToken);

    return await HandleNotificationOperationResultAsync(result, httpContext.User, userId, notifications, hubContext, cancellationToken);
});

notificationsApi.MapPost("/projects/{projectId:int}/mute", async (int projectId,
                                                                   HttpContext httpContext,
                                                                   UserNotificationService notifications,
                                                                   CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await notifications.SetProjectMuteAsync(httpContext.User, userId, projectId, true, cancellationToken);

    return result switch
    {
        NotificationOperationResult.Success => Results.NoContent(),
        NotificationOperationResult.NotFound => Results.NotFound(),
        NotificationOperationResult.Forbidden => Results.Forbid(),
        _ => Results.BadRequest()
    };
});

notificationsApi.MapDelete("/projects/{projectId:int}/mute", async (int projectId,
                                                                      HttpContext httpContext,
                                                                      UserNotificationService notifications,
                                                                      CancellationToken cancellationToken) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await notifications.SetProjectMuteAsync(httpContext.User, userId, projectId, false, cancellationToken);

    return result switch
    {
        NotificationOperationResult.Success => Results.NoContent(),
        NotificationOperationResult.NotFound => Results.NotFound(),
        NotificationOperationResult.Forbidden => Results.Forbid(),
        _ => Results.BadRequest()
    };
});

var projectsApi = app.MapGroup("/api/projects").RequireAuthorization();

projectsApi.MapPost("/{id:int}/archive", async (
    int id,
    HttpContext httpContext,
    ProjectModerationService moderation,
    CancellationToken cancellationToken) =>
{
    if (!IsProjectArchiveActor(httpContext.User))
    {
        return Results.Forbid();
    }

    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await moderation.ArchiveAsync(id, userId, cancellationToken);
    return MapProjectModerationResult(result);
});

projectsApi.MapPost("/{id:int}/restore-archive", async (
    int id,
    HttpContext httpContext,
    ProjectModerationService moderation,
    CancellationToken cancellationToken) =>
{
    if (!IsProjectArchiveActor(httpContext.User))
    {
        return Results.Forbid();
    }

    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await moderation.RestoreFromArchiveAsync(id, userId, cancellationToken);
    return MapProjectModerationResult(result);
});

projectsApi.MapPost("/{id:int}/trash", async (
    int id,
    HttpContext httpContext,
    ProjectModerationService moderation,
    TrashProjectRequest request,
    CancellationToken cancellationToken) =>
{
    if (!IsProjectArchiveActor(httpContext.User))
    {
        return Results.Forbid();
    }

    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await moderation.MoveToTrashAsync(id, userId, request.Reason, cancellationToken);
    return MapProjectModerationResult(result);
});

projectsApi.MapPost("/{id:int}/restore-trash", async (
    int id,
    HttpContext httpContext,
    ProjectModerationService moderation,
    CancellationToken cancellationToken) =>
{
    if (!httpContext.User.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await moderation.RestoreFromTrashAsync(id, userId, cancellationToken);
    return MapProjectModerationResult(result);
});

projectsApi.MapPost("/{id:int}/purge", async (
    int id,
    HttpContext httpContext,
    ProjectModerationService moderation,
    PurgeProjectRequest request,
    CancellationToken cancellationToken) =>
{
    if (!httpContext.User.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var result = await moderation.PurgeAsync(id, userId, request.RemoveAssets, cancellationToken);
    return MapProjectModerationResult(result);
});

var processFlowApi = app.MapGroup("/api/processes/{version}/flow")
    .RequireAuthorization("Checklist.View");

processFlowApi.MapGet("", async (
    string version,
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    var normalizedVersion = version?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(normalizedVersion))
    {
        return Results.BadRequest("Process version is required.");
    }

    var stages = await db.StageTemplates
        .AsNoTracking()
        .Where(s => s.Version == normalizedVersion)
        .OrderBy(s => s.Sequence)
        .ThenBy(s => s.Code)
        .ToListAsync(cancellationToken);

    if (stages.Count == 0)
    {
        return Results.NotFound();
    }

    var dependencies = await db.StageDependencyTemplates
        .AsNoTracking()
        .Where(d => d.Version == normalizedVersion)
        .ToListAsync(cancellationToken);

    var dependencyLookup = dependencies
        .GroupBy(d => d.FromStageCode, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            g => g.Key,
            g => g.Select(d => d.DependsOnStageCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    var nodes = stages
        .Select(stage => new ProcessFlowNodeDto(
            stage.Code,
            stage.Name,
            stage.Sequence,
            stage.Optional,
            stage.ParallelGroup,
            dependencyLookup.TryGetValue(stage.Code, out var depends)
                ? depends
                : Array.Empty<string>()))
        .ToList();

    var edges = dependencies
        .Where(dep => !string.IsNullOrWhiteSpace(dep.DependsOnStageCode) && !string.IsNullOrWhiteSpace(dep.FromStageCode))
        .Select(dep => new ProcessFlowEdgeDto(dep.DependsOnStageCode, dep.FromStageCode))
        .Distinct()
        .ToList();

    return Results.Ok(new ProcessFlowDto(normalizedVersion, nodes, edges));
});

var stageChecklistApi = app.MapGroup("/api/processes/{version}/stages/{stageCode}/checklist")
    .RequireAuthorization("Checklist.View");

stageChecklistApi.MapGet("", async (
    string version,
    string stageCode,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    HttpContext httpContext,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    if (!TryNormalizeStageRoute(version, stageCode, out var normalizedVersion, out var normalizedStageCode, out var error))
    {
        return Results.BadRequest(error);
    }

    var template = await EnsureChecklistTemplateAsync(db, normalizedVersion, normalizedStageCode,
        users.GetUserId(httpContext.User), clock.UtcNow, true, cancellationToken);

    if (template is not StageChecklistTemplate checklist)
    {
        return Results.NotFound();
    }

    return Results.Ok(ToDto(checklist));
});

stageChecklistApi.MapPost("", async (
    string version,
    string stageCode,
    [FromBody] StageChecklistItemCreateRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    HttpContext httpContext,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    if (!TryNormalizeStageRoute(version, stageCode, out var normalizedVersion, out var normalizedStageCode, out var error))
    {
        return Results.BadRequest(error);
    }

    var template = await EnsureChecklistTemplateAsync(db, normalizedVersion, normalizedStageCode,
        users.GetUserId(httpContext.User), clock.UtcNow, false, cancellationToken);

    if (template is not StageChecklistTemplate checklist)
    {
        return Results.NotFound();
    }

    if (request.TemplateRowVersion is null || request.TemplateRowVersion.Length == 0)
    {
        return Results.BadRequest("Template row version is required.");
    }

    if (!MatchesRowVersion(request.TemplateRowVersion, checklist.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist template has been modified by another user." });
    }

    var text = request.Text?.Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
        return Results.BadRequest("Checklist item text is required.");
    }

    if (text.Length > 512)
    {
        return Results.BadRequest("Checklist item text cannot exceed 512 characters.");
    }

    var userId = users.GetUserId(httpContext.User);
    var now = clock.UtcNow;

    var sequence = request.Sequence ?? (checklist.Items.Count == 0 ? 1 : checklist.Items.Max(i => i.Sequence) + 1);
    sequence = Math.Max(1, sequence);

    if (checklist.Items.Any(i => i.Sequence == sequence))
    {
        foreach (var existing in checklist.Items
                     .Where(i => i.Sequence >= sequence)
                     .OrderByDescending(i => i.Sequence))
        {
            existing.Sequence += 1;
            existing.UpdatedByUserId = userId;
            existing.UpdatedOn = now;
        }
    }

    var item = new StageChecklistItemTemplate
    {
        Template = checklist,
        Text = text,
        Sequence = sequence,
        UpdatedByUserId = userId,
        UpdatedOn = now
    };

    checklist.Items.Add(item);
    checklist.UpdatedByUserId = userId;
    checklist.UpdatedOn = now;

    db.StageChecklistAudits.Add(new StageChecklistAudit
    {
        Template = checklist,
        Item = item,
        Action = "ItemCreated",
        PayloadJson = JsonSerializer.Serialize(new { item.Text, item.Sequence }),
        PerformedByUserId = userId,
        PerformedOn = now
    });

    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToDto(checklist));
}).RequireAuthorization("Checklist.Edit");

stageChecklistApi.MapPut("/{itemId:int}", async (
    string version,
    string stageCode,
    int itemId,
    [FromBody] StageChecklistItemUpdateRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    HttpContext httpContext,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    if (!TryNormalizeStageRoute(version, stageCode, out var normalizedVersion, out var normalizedStageCode, out var error))
    {
        return Results.BadRequest(error);
    }

    var template = await EnsureChecklistTemplateAsync(db, normalizedVersion, normalizedStageCode,
        users.GetUserId(httpContext.User), clock.UtcNow, false, cancellationToken);

    if (template is not StageChecklistTemplate checklist)
    {
        return Results.NotFound();
    }

    if (request.TemplateRowVersion is null || request.TemplateRowVersion.Length == 0)
    {
        return Results.BadRequest("Template row version is required.");
    }

    if (!MatchesRowVersion(request.TemplateRowVersion, checklist.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist template has been modified by another user." });
    }

    var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
    if (item is null)
    {
        return Results.NotFound();
    }

    if (request.ItemRowVersion is null || request.ItemRowVersion.Length == 0)
    {
        return Results.BadRequest("Checklist item row version is required.");
    }

    if (!MatchesRowVersion(request.ItemRowVersion, item.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist item has been modified by another user." });
    }

    var text = request.Text?.Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
        return Results.BadRequest("Checklist item text is required.");
    }

    if (text.Length > 512)
    {
        return Results.BadRequest("Checklist item text cannot exceed 512 characters.");
    }

    var userId = users.GetUserId(httpContext.User);
    var now = clock.UtcNow;
    var previousText = item.Text;

    item.Text = text;
    item.UpdatedByUserId = userId;
    item.UpdatedOn = now;

    checklist.UpdatedByUserId = userId;
    checklist.UpdatedOn = now;

    db.StageChecklistAudits.Add(new StageChecklistAudit
    {
        Template = checklist,
        Item = item,
        Action = "ItemUpdated",
        PayloadJson = JsonSerializer.Serialize(new { item.Id, before = previousText, after = text }),
        PerformedByUserId = userId,
        PerformedOn = now
    });

    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToDto(checklist));
}).RequireAuthorization("Checklist.Edit");

stageChecklistApi.MapDelete("/{itemId:int}", async (
    string version,
    string stageCode,
    int itemId,
    [FromBody] StageChecklistItemDeleteRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    HttpContext httpContext,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    if (!TryNormalizeStageRoute(version, stageCode, out var normalizedVersion, out var normalizedStageCode, out var error))
    {
        return Results.BadRequest(error);
    }

    var template = await EnsureChecklistTemplateAsync(db, normalizedVersion, normalizedStageCode,
        users.GetUserId(httpContext.User), clock.UtcNow, false, cancellationToken);

    if (template is not StageChecklistTemplate checklist)
    {
        return Results.NotFound();
    }

    if (request.TemplateRowVersion is null || request.TemplateRowVersion.Length == 0)
    {
        return Results.BadRequest("Template row version is required.");
    }

    if (!MatchesRowVersion(request.TemplateRowVersion, checklist.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist template has been modified by another user." });
    }

    var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
    if (item is null)
    {
        return Results.NotFound();
    }

    if (request.ItemRowVersion is null || request.ItemRowVersion.Length == 0)
    {
        return Results.BadRequest("Checklist item row version is required.");
    }

    if (!MatchesRowVersion(request.ItemRowVersion, item.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist item has been modified by another user." });
    }

    var userId = users.GetUserId(httpContext.User);
    var now = clock.UtcNow;

    checklist.Items.Remove(item);
    db.StageChecklistItemTemplates.Remove(item);

    checklist.UpdatedByUserId = userId;
    checklist.UpdatedOn = now;

    db.StageChecklistAudits.Add(new StageChecklistAudit
    {
        Template = checklist,
        Item = item,
        Action = "ItemDeleted",
        PayloadJson = JsonSerializer.Serialize(new { item.Id, item.Text, item.Sequence }),
        PerformedByUserId = userId,
        PerformedOn = now
    });

    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToDto(checklist));
}).RequireAuthorization("Checklist.Edit");

stageChecklistApi.MapPost("/reorder", async (
    string version,
    string stageCode,
    [FromBody] StageChecklistReorderRequest request,
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    HttpContext httpContext,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    if (!TryNormalizeStageRoute(version, stageCode, out var normalizedVersion, out var normalizedStageCode, out var error))
    {
        return Results.BadRequest(error);
    }

    if (request.Items is null || request.Items.Count == 0)
    {
        return Results.BadRequest("At least one checklist item is required for reordering.");
    }

    var template = await EnsureChecklistTemplateAsync(db, normalizedVersion, normalizedStageCode,
        users.GetUserId(httpContext.User), clock.UtcNow, false, cancellationToken);

    if (template is not StageChecklistTemplate checklist)
    {
        return Results.NotFound();
    }

    if (request.TemplateRowVersion is null || request.TemplateRowVersion.Length == 0)
    {
        return Results.BadRequest("Template row version is required.");
    }

    if (!MatchesRowVersion(request.TemplateRowVersion, checklist.RowVersion))
    {
        return Results.Conflict(new { message = "The checklist template has been modified by another user." });
    }

    if (request.Items.Count != checklist.Items.Count)
    {
        return Results.BadRequest("All checklist items must be included in the reorder request.");
    }

    if (request.Items.Select(i => i.ItemId).Distinct().Count() != request.Items.Count)
    {
        return Results.BadRequest("Duplicate checklist item identifiers detected in reorder request.");
    }

    if (request.Items.Select(i => i.Sequence).Distinct().Count() != request.Items.Count)
    {
        return Results.BadRequest("Duplicate checklist sequence positions detected in reorder request.");
    }

    var itemsById = checklist.Items.ToDictionary(i => i.Id);
    var orderedDtos = request.Items
        .OrderBy(i => i.Sequence)
        .ThenBy(i => i.ItemId)
        .ToList();

    var userId = users.GetUserId(httpContext.User);
    var now = clock.UtcNow;

    var orderedItems = new List<StageChecklistItemTemplate>(orderedDtos.Count);

    foreach (var dto in orderedDtos)
    {
        if (!itemsById.TryGetValue(dto.ItemId, out var item))
        {
            return Results.BadRequest($"Checklist item {dto.ItemId} was not found in the template.");
        }

        if (dto.RowVersion is null || dto.RowVersion.Length == 0)
        {
            return Results.BadRequest($"Checklist item row version is required for item {dto.ItemId}.");
        }

        if (!MatchesRowVersion(dto.RowVersion, item.RowVersion))
        {
            return Results.Conflict(new { message = $"Checklist item {dto.ItemId} has been modified by another user." });
        }

        item.UpdatedByUserId = userId;
        item.UpdatedOn = now;
        orderedItems.Add(item);
    }

    var auditPayload = orderedDtos
        .Select((dto, index) => new { dto.ItemId, Sequence = index + 1 })
        .ToList();

    var maxExistingSequence = checklist.Items.Count == 0
        ? 0
        : checklist.Items.Max(i => i.Sequence);

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    try
    {
        for (var index = 0; index < orderedItems.Count; index++)
        {
            orderedItems[index].Sequence = maxExistingSequence + 1 + index;
        }

        checklist.UpdatedByUserId = userId;
        checklist.UpdatedOn = now;

        db.StageChecklistAudits.Add(new StageChecklistAudit
        {
            Template = checklist,
            Action = "ItemsReordered",
            PayloadJson = JsonSerializer.Serialize(auditPayload),
            PerformedByUserId = userId,
            PerformedOn = now
        });

        await db.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < orderedItems.Count; index++)
        {
            orderedItems[index].Sequence = index + 1;
        }

        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }

    return Results.Ok(ToDto(checklist));
}).RequireAuthorization("Checklist.Edit");

var proliferationEffectiveApi = app.MapGroup("/api/proliferation/effective")
    .RequireAuthorization(new AuthorizeAttribute { Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker });

proliferationEffectiveApi.MapGet("", async (
    int projectId,
    ProliferationSource source,
    int year,
    ProliferationTrackerReadService readService,
    CancellationToken cancellationToken) =>
{
    var total = await readService.GetEffectiveTotalAsync(projectId, source, year, cancellationToken);
    return Results.Ok(new { projectId, source, year, total });
});

var lookupApi = app.MapGroup("/api/lookups")
    .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin,HoD,Project Officer" });

lookupApi.MapGet("/sponsoring-units", async (
    ApplicationDbContext db,
    [FromQuery(Name = "q")] string? query,
    [FromQuery] int page,
    [FromQuery] int pageSize) =>
{
    var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    var currentPage = page < 1 ? 1 : page;
    var size = pageSize < 1 ? 20 : Math.Min(pageSize, 50);

    var units = db.SponsoringUnits
        .AsNoTracking()
        .Where(u => u.IsActive);

    if (!string.IsNullOrEmpty(term))
    {
        units = units.Where(u => EF.Functions.ILike(u.Name, $"%{term}%"));
    }

    var total = await units.CountAsync();
    var items = await units
        .OrderBy(u => u.SortOrder)
        .ThenBy(u => u.Name)
        .Skip((currentPage - 1) * size)
        .Take(size)
        .Select(u => new { id = u.Id, name = u.Name })
        .ToListAsync();

    return Results.Ok(new
    {
        items,
        total,
        page = currentPage,
        pageSize = size
    });
});

lookupApi.MapGet("/projects", async (
    ApplicationDbContext db,
    [FromQuery(Name = "q")] string? query,
    [FromQuery] int page,
    [FromQuery] int pageSize,
    CancellationToken cancellationToken) =>
{
    var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    var currentPage = page < 1 ? 1 : page;
    var size = pageSize < 1 ? 20 : Math.Min(pageSize, 50);

    var projects = db.Projects
        .AsNoTracking()
        .Where(p => !p.IsDeleted && !p.IsArchived);

    if (!string.IsNullOrEmpty(term))
    {
        projects = projects.Where(p => EF.Functions.ILike(p.Name, $"%{term}%"));
    }

    var total = await projects.CountAsync(cancellationToken);
    var items = await projects
        .OrderBy(p => p.Name)
        .Skip((currentPage - 1) * size)
        .Take(size)
        .Select(p => new { id = p.Id, name = p.Name })
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        items,
        total,
        page = currentPage,
        pageSize = size
    });
});

lookupApi.MapGet("/line-directorates", async (
    ApplicationDbContext db,
    [FromQuery(Name = "q")] string? query,
    [FromQuery] int page,
    [FromQuery] int pageSize) =>
{
    var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    var currentPage = page < 1 ? 1 : page;
    var size = pageSize < 1 ? 20 : Math.Min(pageSize, 50);

    var directorates = db.LineDirectorates
        .AsNoTracking()
        .Where(l => l.IsActive);

    if (!string.IsNullOrEmpty(term))
    {
        directorates = directorates.Where(l => EF.Functions.ILike(l.Name, $"%{term}%"));
    }

    var total = await directorates.CountAsync();
    var items = await directorates
        .OrderBy(l => l.SortOrder)
        .ThenBy(l => l.Name)
        .Skip((currentPage - 1) * size)
        .Take(size)
        .Select(l => new { id = l.Id, name = l.Name })
        .ToListAsync();

    return Results.Ok(new
    {
        items,
        total,
        page = currentPage,
        pageSize = size
    });
});

lookupApi.MapGet("/training-types", async (
    ApplicationDbContext db,
    [FromQuery(Name = "q")] string? query,
    [FromQuery] int page,
    [FromQuery] int pageSize,
    CancellationToken cancellationToken) =>
{
    var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    var currentPage = page < 1 ? 1 : page;
    var size = pageSize < 1 ? 20 : Math.Min(pageSize, 50);

    var trainingTypes = db.TrainingTypes
        .AsNoTracking()
        .Where(t => t.IsActive);

    if (!string.IsNullOrEmpty(term))
    {
        trainingTypes = trainingTypes.Where(t => EF.Functions.ILike(t.Name, $"%{term}%"));
    }

    var total = await trainingTypes.CountAsync(cancellationToken);
    var items = await trainingTypes
        .OrderBy(t => t.DisplayOrder)
        .ThenBy(t => t.Name)
        .Skip((currentPage - 1) * size)
        .Take(size)
        .Select(t => new { id = t.Id, name = t.Name })
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        items,
        total,
        page = currentPage,
        pageSize = size
    });
});

eventsApi.MapPost("/{id:guid}/task", async (Guid id, ApplicationDbContext db, ITodoService todos, UserManager<ApplicationUser> users, ClaimsPrincipal user) =>
{
    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
    if (ev == null) return Results.NotFound();
    var userId = users.GetUserId(user);
    await todos.CreateAsync(userId!, ev.Title, TimeZoneInfo.ConvertTime(ev.StartUtc, IstClock.TimeZone));
    return Results.Ok();
}).RequireAuthorization();

eventsApi.MapPost("/celebrations/{id:guid}/task", async (Guid id,
                                                        ApplicationDbContext db,
                                                        ITodoService todos,
                                                        UserManager<ApplicationUser> users,
                                                        ClaimsPrincipal user) =>
{
    var userId = users.GetUserId(user);
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var celebration = await db.Celebrations.FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);
    if (celebration == null) return Results.NotFound();

    var ist = IstClock.TimeZone;
    var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
    var today = DateOnly.FromDateTime(nowLocal.DateTime);
    var next = CelebrationHelpers.NextOccurrenceLocal(celebration, today);
    var dueLocal = CelebrationHelpers.ToLocalDateTime(next);
    await todos.CreateAsync(userId, $"Wish {CelebrationHelpers.DisplayName(celebration)}", dueLocal);
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/categories/children", async (int parentId, ApplicationDbContext db) =>
{
    var items = await db.ProjectCategories
        .Where(c => c.ParentId == parentId && c.IsActive)
        .OrderBy(c => c.SortOrder)
        .ThenBy(c => c.Name)
        .Select(c => new { id = c.Id, name = c.Name })
        .ToListAsync();

    return Results.Ok(items);
}).RequireAuthorization("Project.Create");

app.MapGet("/Projects/Documents/View", async (
    int documentId,
    HttpContext httpContext,
    ApplicationDbContext db,
    IUserContext userContext,
    IDocumentService documentService,
    IDocumentPreviewTokenService previewTokenService,
    [FromQuery(Name = "token")] string? previewToken,
    CancellationToken cancellationToken) =>
{
    var principal = userContext.User;
    var userId = userContext.UserId;
    DocumentPreviewTokenPayload? tokenPayload = null;

    if (string.IsNullOrEmpty(userId))
    {
        if (string.IsNullOrWhiteSpace(previewToken) ||
            !previewTokenService.TryValidate(previewToken, out var payload) ||
            payload.DocumentId != documentId ||
            payload.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return Results.Challenge();
        }

        userId = payload.UserId;
        principal = previewTokenService.CreatePrincipal(payload);
        tokenPayload = payload;
    }

    if (string.IsNullOrEmpty(userId) || principal is null)
    {
        return Results.Challenge();
    }

    var document = await db.ProjectDocuments
        .AsNoTracking()
        .Include(d => d.Project)
        .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    if (document is null || document.Project is null)
    {
        return Results.NotFound();
    }

    if (document.Status != ProjectDocumentStatus.Published || document.IsArchived)
    {
        return Results.NotFound();
    }

    if (!ProjectAccessGuard.CanViewProject(document.Project, principal, userId))
    {
        return Results.Forbid();
    }

    if (tokenPayload is not null && tokenPayload.FileStamp != document.FileStamp)
    {
        return Results.StatusCode(StatusCodes.Status409Conflict);
    }

    var etag = new EntityTagHeaderValue($"\"doc-{document.Id}-v{document.FileStamp}\"");

    var responseHeaders = httpContext.Response.GetTypedHeaders();
    responseHeaders.CacheControl = new CacheControlHeaderValue
    {
        Private = true,
        MaxAge = TimeSpan.FromDays(7)
    };
    responseHeaders.ETag = etag;

    var requestEtags = httpContext.Request.GetTypedHeaders().IfNoneMatch;
    if (requestEtags is not null && requestEtags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Equals(etag)))
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    var streamResult = await documentService.OpenStreamAsync(documentId, cancellationToken);
    if (streamResult is null)
    {
        return Results.NotFound();
    }

    var safeFileName = Path.GetFileName(streamResult.FileName).Replace("\"", string.Empty);
    var disposition = new ContentDispositionHeaderValue("inline")
    {
        FileNameStar = safeFileName,
        FileName = safeFileName
    };

    httpContext.Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
    httpContext.Response.ContentType = "application/pdf";
    httpContext.Response.ContentLength = streamResult.Length;

    return Results.File(streamResult.Stream, contentType: "application/pdf", enableRangeProcessing: true);
}).AllowAnonymous();

app.MapRemarkApi();
app.MapProjectAnalyticsApi();
app.MapMentionApi();
app.MapRazorPages();

// Celebrations endpoints
app.MapGet("/celebrations/upcoming", async (HttpContext ctx, int? window, ApplicationDbContext db) =>
{
    if (!ctx.User.Identity?.IsAuthenticated ?? true) return Results.Unauthorized();
    var ist = IstClock.TimeZone;
    var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
    var today = DateOnly.FromDateTime(nowLocal.DateTime);
    var win = window is 7 or 15 or 30 ? window.Value : 30;
    var items = await db.Celebrations.AsNoTracking().Where(x => x.DeletedUtc == null).ToListAsync();
    var list = items.Select(c =>
    {
        var next = CelebrationHelpers.NextOccurrenceLocal(c, today);
        var daysAway = CelebrationHelpers.DaysAway(today, next);
        return new
        {
            id = c.Id,
            eventType = c.EventType.ToString(),
            name = CelebrationHelpers.DisplayName(c),
            nextOccurrenceLocal = next.ToString("yyyy-MM-dd"),
            daysAway
        };
    }).Where(x => x.daysAway < win).OrderBy(x => x.nextOccurrenceLocal).ThenBy(x => x.name).ToArray();
    return Results.Json(list);
}).RequireAuthorization();

app.MapPost("/celebrations/{id:guid}/task", async (Guid id, HttpContext ctx, ApplicationDbContext db, ITodoService todo, UserManager<ApplicationUser> users, IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(ctx);
    var uid = users.GetUserId(ctx.User);
    if (uid == null) return Results.Unauthorized();
    var celebration = await db.Celebrations.FirstOrDefaultAsync(x => x.Id == id && x.DeletedUtc == null);
    if (celebration == null) return Results.NotFound();
    var ist = IstClock.TimeZone;
    var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
    var today = DateOnly.FromDateTime(nowLocal.DateTime);
    var next = CelebrationHelpers.NextOccurrenceLocal(celebration, today);
    var dueLocal = CelebrationHelpers.ToLocalDateTime(next);
    await todo.CreateAsync(uid, $"Wish {CelebrationHelpers.DisplayName(celebration)}", dueLocal);
    return Results.Ok();
}).RequireAuthorization();

// ensure database is up-to-date, seed roles and purge old audit logs
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    if (app.Environment.IsDevelopment() && db.Database.IsRelational())
    {
        var databaseConnectionString = db.Database.GetConnectionString();
        if (!string.IsNullOrEmpty(databaseConnectionString))
        {
            await using var conn = new NpgsqlConnection(databaseConnectionString);
            await conn.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "select current_database(), version()";
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    app.Logger.LogInformation(
                        "Connected to database {Database}; {Version}",
                        reader.GetString(0),
                        reader.GetString(1));
                }
            }
            await conn.CloseAsync();
        }
    }

    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();

        var projectStagesExists = true;
        if (db.Database.IsNpgsql())
        {
            var maintenanceConnectionString = db.Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(maintenanceConnectionString))
            {
                await using var maintenanceConnection = new NpgsqlConnection(maintenanceConnectionString);
                await maintenanceConnection.OpenAsync();
                await using (var maintenanceCommand = maintenanceConnection.CreateCommand())
                {
                    maintenanceCommand.CommandText = @"select to_regclass('""ProjectStages""') is not null";
                    var maintenanceResult = await maintenanceCommand.ExecuteScalarAsync();
                    projectStagesExists = maintenanceResult is bool exists && exists;
                }
                await maintenanceConnection.CloseAsync();
            }

            if (!projectStagesExists)
            {
                app.Logger.LogWarning("ProjectStages table not found; skipping maintenance SQL for stages.");
            }
        }

        if (projectStagesExists)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ADD COLUMN IF NOT EXISTS ""AutoCompletedFromCode"" character varying(16);
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ADD COLUMN IF NOT EXISTS ""IsAutoCompleted"" boolean NOT NULL DEFAULT FALSE;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ADD COLUMN IF NOT EXISTS ""RequiresBackfill"" boolean NOT NULL DEFAULT FALSE;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE ""ProjectStages""
                SET ""IsAutoCompleted"" = FALSE
                WHERE ""IsAutoCompleted"" IS NULL;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE ""ProjectStages""
                SET ""RequiresBackfill"" = FALSE
                WHERE ""RequiresBackfill"" IS NULL;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ALTER COLUMN ""IsAutoCompleted"" SET DEFAULT FALSE;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ALTER COLUMN ""RequiresBackfill"" SET DEFAULT FALSE;
            ");
        }

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""SocialMediaEvents""
                DROP COLUMN IF EXISTS ""Reach"";
            ");
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF COL_LENGTH(N'dbo.SocialMediaEvents', 'Reach') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[SocialMediaEvents] DROP COLUMN [Reach];
                END
            ");
        }

        if (projectStagesExists)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ALTER COLUMN ""ActualStart"" DROP NOT NULL;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ALTER COLUMN ""CompletedOn"" DROP NOT NULL;
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                DROP CONSTRAINT IF EXISTS ""CK_ProjectStages_CompletedHasDate"";
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""ProjectStages""
                ADD CONSTRAINT ""CK_ProjectStages_CompletedHasDate""
                CHECK (""Status"" <> 'Completed' OR (""CompletedOn"" IS NOT NULL AND ""ActualStart"" IS NOT NULL) OR ""RequiresBackfill"" IS TRUE);
            ");
        }
        var migrations = await db.Database.GetAppliedMigrationsAsync();
        if (!migrations.Contains("20250909153316_UseXminForTodoItem"))
        {
            app.Logger.LogWarning("Migration 20250909153316_UseXminForTodoItem not applied. TodoItems may still have a RowVersion column.");
        }
        var cutoff = DateTime.UtcNow.AddDays(-90);
        db.AuditLogs.Where(a => a.TimeUtc < cutoff).ExecuteDelete();
    }

    await ProjectManagement.Data.StageFlowSeeder.SeedAsync(services);
    await ProjectManagement.Data.IdentitySeeder.SeedAsync(services);
}

static bool TryNormalizeStageRoute(string? version, string? stageCode,
    out string normalizedVersion,
    out string normalizedStageCode,
    out string? error)
{
    normalizedVersion = version?.Trim() ?? string.Empty;
    normalizedStageCode = stageCode?.Trim().ToUpperInvariant() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(normalizedVersion))
    {
        error = "Process version is required.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(normalizedStageCode))
    {
        error = "Stage code is required.";
        return false;
    }

    error = null;
    return true;
}

static bool MatchesRowVersion(byte[]? requestVersion, byte[] entityVersion)
    => requestVersion is { Length: > 0 } candidate &&
       entityVersion is { Length: > 0 } current &&
       candidate.AsSpan().SequenceEqual(current);

static StageChecklistTemplateDto ToDto(StageChecklistTemplate template)
{
    var items = template.Items
        .OrderBy(i => i.Sequence)
        .ThenBy(i => i.Id)
        .Select(i => new StageChecklistItemDto(
            i.Id,
            i.Text,
            i.Sequence,
            i.RowVersion,
            i.UpdatedByUserId,
            i.UpdatedOn))
        .ToList();

    return new StageChecklistTemplateDto(
        template.Id,
        template.Version,
        template.StageCode,
        template.UpdatedByUserId,
        template.UpdatedOn,
        template.RowVersion,
        items);
}

static async Task<StageChecklistTemplate?> EnsureChecklistTemplateAsync(
    ApplicationDbContext db,
    string version,
    string stageCode,
    string? userId,
    DateTimeOffset now,
    bool createIfMissing,
    CancellationToken cancellationToken)
{
    var template = await db.StageChecklistTemplates
        .Include(t => t.Items)
        .FirstOrDefaultAsync(t => t.Version == version && t.StageCode == stageCode, cancellationToken);

    if (template is not null)
    {
        return template;
    }

    if (!createIfMissing)
    {
        return null;
    }

    var stageExists = await db.StageTemplates
        .AsNoTracking()
        .AnyAsync(t => t.Version == version && t.Code == stageCode, cancellationToken);

    if (!stageExists)
    {
        return null;
    }

    template = new StageChecklistTemplate
    {
        Version = version,
        StageCode = stageCode,
        UpdatedByUserId = userId,
        UpdatedOn = now
    };

    db.StageChecklistTemplates.Add(template);

    db.StageChecklistAudits.Add(new StageChecklistAudit
    {
        Template = template,
        Action = "TemplateCreated",
        PayloadJson = JsonSerializer.Serialize(new { template.StageCode, template.Version }),
        PerformedByUserId = userId,
        PerformedOn = now
    });

    await db.SaveChangesAsync(cancellationToken);

    await db.Entry(template).Collection(t => t.Items).LoadAsync(cancellationToken);

    return template;
}

static string[] ResolveDevelopmentLoopbackOrigins(IConfiguration configuration)
{
    static void AddOrigin(HashSet<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            set.Add($"{uri.Scheme}://{uri.Authority}");
        }
    }

    var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "https://localhost:7183"
    };

    var urls = configuration["ASPNETCORE_URLS"];
    if (!string.IsNullOrWhiteSpace(urls))
    {
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddOrigin(origins, url);
        }
    }

    var httpsPort = configuration["ASPNETCORE_HTTPS_PORT"];
    if (!string.IsNullOrWhiteSpace(httpsPort) && int.TryParse(httpsPort, out var port) && port > 0)
    {
        AddOrigin(origins, $"https://localhost:{port}");
        AddOrigin(origins, $"https://127.0.0.1:{port}");
    }

    AddOrigin(origins, configuration["Kestrel:Endpoints:Https:Url"]);

    return origins.ToArray();
}

static string BuildConnectSrcDirective(IConfiguration configuration)
{
    var sources = configuration
        .GetSection("SecurityHeaders:ContentSecurityPolicy:ConnectSources")
        .Get<string[]>() ?? Array.Empty<string>();

    var normalizedSources = sources
        .Select(source => source?.Trim())
        .Where(source => !string.IsNullOrWhiteSpace(source))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    return normalizedSources.Length == 0
        ? "'self'"
        : $"'self' {string.Join(" ", normalizedSources)}";
}

static async Task<IResult> HandleNotificationOperationResultAsync(
    NotificationOperationResult result,
    ClaimsPrincipal principal,
    string userId,
    UserNotificationService notifications,
    IHubContext<NotificationsHub, INotificationsClient> hubContext,
    CancellationToken cancellationToken)
{
    return result switch
    {
        NotificationOperationResult.Success => await SendUnreadCountAsync(principal, userId, notifications, hubContext, cancellationToken),
        NotificationOperationResult.NotFound => Results.NotFound(),
        NotificationOperationResult.Forbidden => Results.Forbid(),
        _ => Results.BadRequest(),
    };
}

static async Task<IResult> SendUnreadCountAsync(
    ClaimsPrincipal principal,
    string userId,
    UserNotificationService notifications,
    IHubContext<NotificationsHub, INotificationsClient> hubContext,
    CancellationToken cancellationToken)
{
    var unread = await notifications.CountUnreadAsync(principal, userId, cancellationToken);
    await hubContext.Clients.User(userId).ReceiveUnreadCount(unread);
    return Results.NoContent();
}

static bool IsProjectArchiveActor(ClaimsPrincipal principal) =>
    principal.IsInRole("Admin") || principal.IsInRole("HoD");

static IResult MapProjectModerationResult(ProjectModerationResult result) => result.Status switch
{
    ProjectModerationStatus.Success => Results.NoContent(),
    ProjectModerationStatus.NotFound => Results.NotFound(),
    ProjectModerationStatus.InvalidState => Results.Conflict(new { error = result.Error }),
    ProjectModerationStatus.ValidationFailed => Results.BadRequest(new { error = result.Error }),
    _ => Results.BadRequest()
};

app.Run();
