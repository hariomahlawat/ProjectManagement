using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Data;
using ProjectManagement.Services.Navigation;

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
        private readonly DefaultLandingPageResolver _landingPageResolver;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            ILogger<LoginModel> logger,
            ApplicationDbContext db,
            DefaultLandingPageResolver landingPageResolver)
        {
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
            _userManager = signInManager.UserManager;
            _landingPageResolver = landingPageResolver;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            [Display(Name = "Username")]
            public string UserName { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me")]
            public bool RememberMe { get; set; }
        }

        public void OnGet() { }

        private const string GenericLoginError = "Invalid username or password.";

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
                    var identityUpdate = await _signInManager.UserManager.UpdateAsync(user);
                    if (!identityUpdate.Succeeded)
                    {
                        _logger.LogWarning(
                            "Login succeeded for {User}, but login metadata could not be updated: {Errors}",
                            user.UserName,
                            string.Join("; ", identityUpdate.Errors.Select(error => error.Description)));
                    }

                    // AuthEvents is the authoritative authentication event stream. Persist it before
                    // the secondary general-purpose audit entry so an AuditLogs storage problem
                    // can never turn valid credentials into an HTTP 500 or suppress the sign-in event.
                    _db.AuthEvents.Add(new AuthEvent
                    {
                        UserId = user.Id,
                        WhenUtc = when,
                        Event = AuthenticationEventNames.LoginSucceeded,
                        Ip = ip,
                        UserAgent = ua
                    });
                    await _db.SaveChangesAsync();

                    await TryWriteAuthenticationAuditAsync(
                        AuthenticationEventNames.AuditLoginSuccess,
                        userName: user.UserName,
                        userId: user.Id);
                }
                if (user is not null && (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)))
                {
                    var landingPage = await _landingPageResolver.ResolveAsync(user);
                    returnUrl = Url.Page(landingPage) ?? Url.Content("~/Dashboard");
                }

                if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
                {
                    returnUrl = Url.Page("/Dashboard/Index") ?? Url.Content("~/Dashboard");
                }

                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }

            ModelState.AddModelError(string.Empty, GenericLoginError);
            if (result.IsLockedOut)
            {
                _logger.LogWarning("Login failed. Account locked out for user: {User}", Input.UserName);
                await TryWriteAuthenticationAuditAsync(
                    AuthenticationEventNames.AuditLoginLockedOut,
                    message: Input.UserName,
                    level: "Warning",
                    userName: Input.UserName);
            }
            else if (result.IsNotAllowed)
            {
                _logger.LogWarning("Login failed. Not allowed for user: {User}", Input.UserName);
                await TryWriteAuthenticationAuditAsync(
                    AuthenticationEventNames.AuditLoginFailed,
                    message: $"Not allowed for {Input.UserName}",
                    level: "Warning",
                    userName: Input.UserName);
            }
            else
            {
                _logger.LogWarning("Login failed. Invalid credentials for user: {User}", Input.UserName);
                await TryWriteAuthenticationAuditAsync(
                    AuthenticationEventNames.AuditLoginFailed,
                    message: $"Invalid credentials for {Input.UserName}",
                    level: "Warning",
                    userName: Input.UserName);
            }

            return Page();
        }

        private async Task TryWriteAuthenticationAuditAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null)
        {
            try
            {
                await HttpContext.RequestServices
                    .GetRequiredService<IAuditService>()
                    .LogAsync(
                        action,
                        message: message,
                        level: level,
                        userId: userId,
                        userName: userName,
                        http: HttpContext);
            }
            catch (DbUpdateException exception)
            {
                // Authentication must remain available even when the secondary AuditLogs store
                // is temporarily unhealthy. Successful sign-ins are already recorded in AuthEvents.
                _logger.LogError(
                    exception,
                    "Authentication audit persistence failed for action {Action} and user {UserName}. Authentication processing will continue.",
                    action,
                    userName);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unexpected authentication audit failure for action {Action} and user {UserName}. Authentication processing will continue.",
                    action,
                    userName);
            }
        }
    }
}
