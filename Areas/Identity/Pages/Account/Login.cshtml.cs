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

namespace ProjectManagement.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [EnableRateLimiting("login")]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
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

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/Dashboard/Index");
            if (!ModelState.IsValid) return Page();

            var result = await _signInManager.PasswordSignInAsync(Input.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var user = await _signInManager.UserManager.FindByNameAsync(Input.UserName);
                if (user != null)
                {
                    user.LastLoginUtc = DateTime.UtcNow;
                    user.LoginCount = user.LoginCount + 1;
                    await _signInManager.UserManager.UpdateAsync(user);
                    await HttpContext.RequestServices.GetRequiredService<IAuditService>()
                        .LogAsync("LoginSuccess", userName: user.UserName, userId: user.Id, http: HttpContext);
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
