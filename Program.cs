using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

var builder = WebApplication.CreateBuilder(args);

// ---------- Database (PostgreSQL) ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Optional: smooth over DateTime quirks if you use legacy semantics
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

// ---------- Identity (no email verification, username-first) ----------
builder.Services
    .AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;   // no email confirm
        options.User.RequireUniqueEmail = false;          // email is optional
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequiredLength = 6;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Identity/Account/Login";
    opt.AccessDeniedPath = "/Identity/Account/AccessDenied";
    opt.SlidingExpiration = true;
    // For pure HTTP on a closed LAN in dev, you could also set:
    // opt.Cookie.SecurePolicy = CookieSecurePolicy.None;
});

builder.Services.AddRazorPages();

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

// If you’re on a sealed LAN and testing without TLS, you can temporarily disable HTTPS redirection.
// Otherwise, keep it on (recommended if you have a cert or a reverse proxy).
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // <-- required for Identity
app.UseAuthorization();

app.MapRazorPages();

app.Run();
