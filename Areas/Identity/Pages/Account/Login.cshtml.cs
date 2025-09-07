using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
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

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _signInManager.UserManager.FindByNameAsync(Input.UserName);
            if (user == null)
            {
                _logger.LogWarning("Login failed for non-existent user {UserName}", Input.UserName);
                ModelState.AddModelError(string.Empty, "User does not exist.");
                return Page();
            }
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Login attempt for disabled user {UserName}", Input.UserName);
                ModelState.AddModelError(string.Empty, "Account is disabled.");
                return Page();
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, Input.RememberMe);
                _logger.LogInformation("User logged in.");
                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            _logger.LogWarning("Invalid password for {UserName}", Input.UserName);
            ModelState.AddModelError(string.Empty, "Invalid password.");
            return Page();
        }
    }
}
