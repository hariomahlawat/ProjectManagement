using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------- Database (PostgreSQL) ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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
    opt.SlidingExpiration = true;
    opt.Cookie.HttpOnly = true;
    opt.Cookie.SameSite = SameSiteMode.Lax;
    opt.Cookie.SecurePolicy = CookieSecurePolicy.None; // change to Always if you terminate TLS
});

builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddHttpContextAccessor();

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
    options.Conventions.AuthorizeFolder("/");
})
    .AddViewOptions(options =>
    {
        options.HtmlHelperOptions.ClientValidationEnabled = true;
    })
    .AddMvcOptions(o => o.Filters.Add<EnforcePasswordChangeFilter>());

var app = builder.Build();

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

// If you're on a sealed LAN and testing without TLS, you can temporarily disable HTTPS redirection.
// Otherwise, keep it on (recommended if you have a cert or a reverse proxy).
app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    ctx.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    ctx.Response.Headers.TryAdd("Content-Security-Policy",
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self';");
    await next();
});

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // <-- required for Identity
app.UseAuthorization();

app.MapRazorPages();

// seed roles and first admin
using (var scope = app.Services.CreateScope())
    await ProjectManagement.Data.IdentitySeeder.SeedAsync(scope.ServiceProvider);

app.Run();
