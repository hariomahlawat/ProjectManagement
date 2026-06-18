using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Data;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [EnableRateLimiting("login")]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger, ApplicationDbContext db)
        {
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
            _userManager = signInManager.UserManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            [Display(Name = "User Name")]
            public string UserName { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public void OnGet() { }

        private const string GenericLoginError = "Invalid username or password.";

        // SECTION: Role-aware landing keeps Project Officer daily work front-and-center.
        private async Task<string> GetDefaultLandingUrlAsync(ApplicationUser user)
        {
            if (await _userManager.IsInRoleAsync(user, RoleNames.ProjectOfficer))
            {
                return Url.Content("~/Workspace");
            }

            return Url.Content("~/Dashboard/Index");
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            var result = await _signInManager.PasswordSignInAsync(Input.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var user = await _signInManager.UserManager.FindByNameAsync(Input.UserName);
                if (user != null)
                {
                    var when = DateTimeOffset.UtcNow;
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var ua = Request.Headers.UserAgent.ToString();

                    user.LastLoginUtc = DateTime.UtcNow;
                    user.LoginCount = user.LoginCount + 1;
                    await _signInManager.UserManager.UpdateAsync(user);
                    await HttpContext.RequestServices.GetRequiredService<IAuditService>()
                        .LogAsync("LoginSuccess", userName: user.UserName, userId: user.Id, http: HttpContext);

                    _db.AuthEvents.Add(new AuthEvent
                    {
                        UserId = user.Id,
                        WhenUtc = when,
                        Event = "LoginSucceeded",
                        Ip = ip,
                        UserAgent = ua
                    });
                    await _db.SaveChangesAsync();
                }
                if (user is not null && (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)))
                {
                    returnUrl = await GetDefaultLandingUrlAsync(user);
                }

                if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
                {
                    returnUrl = Url.Content("~/Dashboard/Index");
                }

                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }

            ModelState.AddModelError(string.Empty, GenericLoginError);
            if (result.IsLockedOut)
            {
                _logger.LogWarning("Login failed. Account locked out for user: {User}", Input.UserName);
                await HttpContext.RequestServices.GetRequiredService<IAuditService>()
                    .LogAsync("LoginLockedOut", message: Input.UserName, level: "Warning", userName: Input.UserName, http: HttpContext);
            }
            else if (result.IsNotAllowed)
            {
                _logger.LogWarning("Login failed. Not allowed for user: {User}", Input.UserName);
                await HttpContext.RequestServices.GetRequiredService<IAuditService>()
                    .LogAsync("LoginFailed", message: $"Not allowed for {Input.UserName}", level: "Warning", userName: Input.UserName, http: HttpContext);
            }
            else
            {
                _logger.LogWarning("Login failed. Invalid credentials for user: {User}", Input.UserName);
                await HttpContext.RequestServices.GetRequiredService<IAuditService>()
                    .LogAsync("LoginFailed", message: $"Invalid credentials for {Input.UserName}", level: "Warning", userName: Input.UserName, http: HttpContext);
            }

            return Page();
        }
    }
}
