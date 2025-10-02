using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.IO;
using System.Linq;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Storage;
using ProjectManagement.Infrastructure;
using Markdig;
using Ganss.Xss;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Helpers;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using ProjectManagement.Utilities;
using ProjectManagement.Services.Stages;
using Microsoft.Net.Http.Headers;
using System.Threading;

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
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.Configure<UserLifecycleOptions>(
    builder.Configuration.GetSection("UserLifecycle"));
builder.Services.AddScoped<IUserLifecycleService, UserLifecycleService>();
builder.Services.AddHostedService<UserPurgeWorker>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ITodoService, TodoService>();
builder.Services.Configure<TodoOptions>(
    builder.Configuration.GetSection("Todo"));
builder.Services.AddScoped<ILoginAnalyticsService, LoginAnalyticsService>();
builder.Services.AddHostedService<LoginAggregationWorker>();
builder.Services.AddHostedService<TodoPurgeWorker>();
builder.Services.AddScoped<PlanDraftService>();
builder.Services.AddScoped<PlanApprovalService>();
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
builder.Services.AddScoped<ProjectCommentService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentRequestService, DocumentRequestService>();
builder.Services.AddScoped<IDocumentDecisionService, DocumentDecisionService>();
builder.Services.AddScoped<PlanReadService>();
builder.Services.AddScoped<PlanGenerationService>();
builder.Services.AddScoped<IScheduleEngine, ScheduleEngine>();
builder.Services.AddScoped<IForecastWriter, ForecastWriter>();
builder.Services.AddScoped<ForecastBackfillService>();
builder.Services.AddScoped<ProjectMetaChangeRequestService>();
builder.Services.AddScoped<ProjectMetaChangeDecisionService>();
builder.Services.AddOptions<ProjectPhotoOptions>()
    .Bind(builder.Configuration.GetSection("ProjectPhotos"));
builder.Services.AddOptions<ProjectDocumentOptions>()
    .Bind(builder.Configuration.GetSection("ProjectDocuments"));
builder.Services.AddSingleton<IConfigureOptions<ProjectPhotoOptions>, ProjectPhotoOptionsSetup>();
builder.Services.AddSingleton<IUploadRootProvider, UploadRootProvider>();
builder.Services.AddScoped<IProjectPhotoService, ProjectPhotoService>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Privacy");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Login");
})
    .AddViewOptions(options =>
    {
        options.HtmlHelperOptions.ClientValidationEnabled = true;
    })
    .AddMvcOptions(o => o.Filters.Add<EnforcePasswordChangeFilter>());

var app = builder.Build();

// Ensure the database schema is up to date before handling requests
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

if (runForecastBackfill)
{
    using var scope = app.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<ForecastBackfillService>();
    var updated = await service.BackfillAsync();
    app.Logger.LogInformation("Backfilled forecast dates for {Count} stages.", updated);
    return;
}

app.Logger.LogInformation("Using database {Database} on host {Host}", csb.Database, csb.Host);

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
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";

    var isDocumentViewer = ctx.Request.Path.StartsWithSegments("/Projects/Documents/View", StringComparison.OrdinalIgnoreCase);

    if (isDocumentViewer)
    {
        h["Content-Security-Policy"] = "frame-ancestors 'self'";
    }
    else
    {
        h["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "frame-ancestors 'none'; " +
            "frame-src 'self'; " +
            "img-src 'self' data: blob:; " +
            "script-src 'self'; " +
            "style-src 'self'; " +
            "font-src 'self' data:; " +
            "connect-src 'self';";
    }
    await next();
});

app.UseStaticFiles();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Calendar API endpoints
var eventsApi = app.MapGroup("/calendar/events");

eventsApi.MapGet("", async (ApplicationDbContext db,
                             [FromQuery(Name = "start")] DateTimeOffset start,
                             [FromQuery(Name = "end")]   DateTimeOffset end) =>
{
    // Guard against huge windows
    if ((end - start).TotalDays > 400)
        end = start.AddDays(400);

    var rows = await db.Events
        .Where(e => !e.IsDeleted &&
               (e.RecurrenceRule != null || (e.StartUtc < end && e.EndUtc > start)))
        .ToListAsync();

    var list = new List<object>();
    foreach (var ev in rows)
    {
        IEnumerable<RecurrenceExpander.Occ> occs;
        try { occs = RecurrenceExpander.Expand(ev, start, end); }
        catch { occs = Array.Empty<RecurrenceExpander.Occ>(); }

        foreach (var o in occs)
        {
            list.Add(new
            {
                id = o.InstanceId,
                seriesId = ev.Id,
                title = ev.Title,
                start = o.Start,
                end   = o.End,
                allDay = ev.IsAllDay,
                category = ev.Category.ToString(),
                location = ev.Location,
                isRecurring = !string.IsNullOrWhiteSpace(ev.RecurrenceRule)
            });
        }
    }

    // sort by start
    return Results.Ok(list.OrderBy(x => ((DateTimeOffset)x.GetType().GetProperty("start")!.GetValue(x)!)));
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

eventsApi.MapPost("/{id:guid}/task", async (Guid id, ApplicationDbContext db, ITodoService todos, UserManager<ApplicationUser> users, ClaimsPrincipal user) =>
{
    var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
    if (ev == null) return Results.NotFound();
    var userId = users.GetUserId(user);
    await todos.CreateAsync(userId!, ev.Title, TimeZoneInfo.ConvertTime(ev.StartUtc, IstClock.TimeZone));
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
    CancellationToken cancellationToken) =>
{
    var userId = userContext.UserId;
    if (string.IsNullOrEmpty(userId))
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

    if (!ProjectAccessGuard.CanViewProject(document.Project, userContext.User, userId))
    {
        return Results.Forbid();
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
}).RequireAuthorization();

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
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ADD COLUMN IF NOT EXISTS "AutoCompletedFromCode" character varying(16);
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ADD COLUMN IF NOT EXISTS "IsAutoCompleted" boolean NOT NULL DEFAULT FALSE;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ADD COLUMN IF NOT EXISTS "RequiresBackfill" boolean NOT NULL DEFAULT FALSE;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "ProjectStages"
            SET "IsAutoCompleted" = FALSE
            WHERE "IsAutoCompleted" IS NULL;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "ProjectStages"
            SET "RequiresBackfill" = FALSE
            WHERE "RequiresBackfill" IS NULL;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ALTER COLUMN "IsAutoCompleted" SET DEFAULT FALSE;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ALTER COLUMN "RequiresBackfill" SET DEFAULT FALSE;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ALTER COLUMN "ActualStart" DROP NOT NULL;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ALTER COLUMN "CompletedOn" DROP NOT NULL;
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";
        """);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "ProjectStages"
            ADD CONSTRAINT "CK_ProjectStages_CompletedHasDate"
            CHECK ("Status" <> 'Completed' OR ("CompletedOn" IS NOT NULL AND "ActualStart" IS NOT NULL) OR "RequiresBackfill" IS TRUE);
        """);
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

app.Run();

public partial class Program { }
