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
using ProjectManagement.Infrastructure;
using Microsoft.Extensions.Logging;
using ProjectManagement.Helpers;
using Markdig;
using Ganss.Xss;
using System.Text.Json.Serialization;

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

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Identity/Account/Login";
    opt.AccessDeniedPath = "/Identity/Account/AccessDenied";
    opt.Cookie.Name = "__Host-PMAuth";
    opt.Cookie.Path = "/";
    opt.Cookie.HttpOnly = true;
    opt.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opt.Cookie.SameSite = SameSiteMode.Strict;
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
builder.Services.Configure<UserLifecycleOptions>(
    builder.Configuration.GetSection("UserLifecycle"));
builder.Services.AddScoped<IUserLifecycleService, UserLifecycleService>();
builder.Services.AddHostedService<UserPurgeWorker>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ITodoService, TodoService>();
builder.Services.Configure<TodoOptions>(
    builder.Configuration.GetSection("Todo"));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddScoped<ILoginAnalyticsService, LoginAnalyticsService>();
builder.Services.AddHostedService<LoginAggregationWorker>();
builder.Services.AddHostedService<TodoPurgeWorker>();

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

var markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
var htmlSanitizer = new HtmlSanitizer();

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
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data:; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self' data:; " +
        "connect-src 'self';";
    await next();
});

app.UseStaticFiles();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/calendar/events", async (DateTimeOffset start, DateTimeOffset end, ApplicationDbContext db) =>
{
    var items = await db.Events.AsNoTracking()
        .Where(e => !e.IsDeleted && e.EndUtc > start && e.StartUtc < end)
        .Select(e => new
        {
            id = e.Id,
            title = e.Title,
            start = e.StartUtc,
            end = e.EndUtc,
            allDay = e.IsAllDay,
            category = e.Category.ToString(),
            location = e.Location
        }).ToListAsync();
    return Results.Json(items);
}).RequireAuthorization();

app.MapGet("/calendar/events/{id:guid}", async (Guid id, ApplicationDbContext db) =>
{
    var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    if (ev == null) return Results.NotFound();
    var html = htmlSanitizer.Sanitize(Markdown.ToHtml(ev.Description ?? string.Empty, markdownPipeline));
    return Results.Json(new
    {
        id = ev.Id,
        title = ev.Title,
        start = ev.StartUtc,
        end = ev.EndUtc,
        allDay = ev.IsAllDay,
        category = ev.Category.ToString(),
        location = ev.Location,
        description = ev.Description,
        descriptionHtml = html
    });
}).RequireAuthorization();


app.MapPost("/calendar/events", async (EventRequest req, HttpContext ctx, ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(ctx);
    if (req.EndUtc <= req.StartUtc) return Results.BadRequest();
    var uid = users.GetUserId(ctx.User);
    if (uid == null) return Results.Unauthorized();
    var now = clock.UtcNow;
    var ev = new CalendarEvent
    {
        Id = Guid.NewGuid(),
        Title = req.Title,
        Description = req.Description,
        Category = req.Category,
        Location = req.Location,
        StartUtc = req.StartUtc,
        EndUtc = req.EndUtc,
        IsAllDay = req.IsAllDay,
        CreatedById = uid,
        UpdatedById = uid,
        CreatedAt = now,
        UpdatedAt = now
    };
    db.Events.Add(ev);
    await db.SaveChangesAsync();
    return Results.Json(new { id = ev.Id });
}).RequireAuthorization(policy => policy.RequireRole("Admin", "TA", "HOD"));

app.MapPut("/calendar/events/{id:guid}", async (Guid id, EventUpdateRequest req, HttpContext ctx, ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(ctx);
    var ev = await db.Events.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    if (ev == null) return Results.NotFound();
    var uid = users.GetUserId(ctx.User);
    if (uid == null) return Results.Unauthorized();
    if (req.EndUtc <= req.StartUtc) return Results.BadRequest();
    if (req.Title != null) ev.Title = req.Title;
    if (req.Description != null) ev.Description = req.Description;
    if (req.Category.HasValue) ev.Category = req.Category.Value;
    if (req.Location != null) ev.Location = req.Location;
    ev.StartUtc = req.StartUtc;
    ev.EndUtc = req.EndUtc;
    ev.IsAllDay = req.IsAllDay;
    ev.UpdatedById = uid;
    ev.UpdatedAt = clock.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization(policy => policy.RequireRole("Admin", "TA", "HOD"));

app.MapDelete("/calendar/events/{id:guid}", async (Guid id, HttpContext ctx, ApplicationDbContext db, UserManager<ApplicationUser> users, IClock clock, IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(ctx);
    var ev = await db.Events.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    if (ev == null) return Results.NotFound();
    var uid = users.GetUserId(ctx.User);
    if (uid == null) return Results.Unauthorized();
    ev.IsDeleted = true;
    ev.UpdatedById = uid;
    ev.UpdatedAt = clock.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization(policy => policy.RequireRole("Admin", "TA", "HOD"));

app.MapPost("/calendar/events/{id:guid}/task", async (Guid id, HttpContext ctx, ApplicationDbContext db, ITodoService todo, UserManager<ApplicationUser> users, IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(ctx);
    var uid = users.GetUserId(ctx.User);
    if (uid == null) return Results.Unauthorized();
    var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    if (ev == null) return Results.NotFound();
    await todo.CreateAsync(uid, ev.Title, ev.StartUtc);
    return Results.Ok();
}).RequireAuthorization();


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
    if (app.Environment.IsDevelopment())
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "select current_database(), version()";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                app.Logger.LogInformation("Connected to database {Database}; {Version}", reader.GetString(0), reader.GetString(1));
            }
        }
        await conn.CloseAsync();
    }

    await db.Database.MigrateAsync();
    var migrations = await db.Database.GetAppliedMigrationsAsync();
    if (!migrations.Contains("20250909153316_UseXminForTodoItem"))
    {
        app.Logger.LogWarning("Migration 20250909153316_UseXminForTodoItem not applied. TodoItems may still have a RowVersion column.");
    }

    await ProjectManagement.Data.IdentitySeeder.SeedAsync(services);
    var cutoff = DateTime.UtcNow.AddDays(-90);
    db.AuditLogs.Where(a => a.TimeUtc < cutoff).ExecuteDelete();
}

app.Run();
