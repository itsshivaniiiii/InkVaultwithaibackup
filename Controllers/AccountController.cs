using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using InkVault.Models;
using InkVault.ViewModels;
using InkVault.Services;

namespace InkVault.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailService _emailService;
        private readonly IOTPService _otpService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailService emailService,
            IOTPService otpService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailService = emailService;
            _otpService = otpService;
        }

        [HttpGet]
        public IActionResult Welcome()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Welcome(WelcomeViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                if (string.IsNullOrEmpty(model.Username) || string.IsNullOrEmpty(model.Password))
                {
                    ModelState.AddModelError(string.Empty, "Username and password are required.");
                    return View(model);
                }

                var user = await _userManager.FindByNameAsync(model.Username);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "Invalid username or password.");
                    return View(model);
                }

                // Sign in with persistent cookie support
                var result = await _signInManager.PasswordSignInAsync(
                    user,
                    model.Password,
                    isPersistent: model.RememberMe,
                    lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    if (!user.EmailVerified)
                    {
                        await _signInManager.SignOutAsync();
                        TempData["Warning"] = "Please verify your email first.";
                        return RedirectToAction("VerifyOTP", new { email = user.Email });
                    }

                    // Update last login time
                    user.LastLoginAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);

                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Welcome (Login) Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                ModelState.AddModelError(string.Empty, "An error occurred during login. Please try again.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                // Check if username exists and is verified
                var existingUserByUsername = await _userManager.FindByNameAsync(model.Username);
                if (existingUserByUsername != null && existingUserByUsername.EmailVerified)
                {
                    ModelState.AddModelError(nameof(model.Username), "Username is already in use.");
                    return View(model);
                }

                // Check if there's an unverified account with the same email for the same username
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                
                ApplicationUser user;

                // If unverified user exists with same email and username, delete and recreate
                if (existingUser != null && !existingUser.EmailVerified && existingUser.UserName == model.Username)
                {
                    // Delete the previous unverified registration
                    await _userManager.DeleteAsync(existingUser);
                    user = new ApplicationUser
                    {
                        UserName = model.Username,
                        Email = model.Email,
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                        PhoneNumber = model.PhoneNumber,
                        Gender = model.Gender,
                        DateOfBirth = model.DateOfBirth,
                        EmailVerified = false,
                        CreatedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    // Create new user (allow same email with different username)
                    user = new ApplicationUser
                    {
                        UserName = model.Username,
                        Email = model.Email,
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                        PhoneNumber = model.PhoneNumber,
                        Gender = model.Gender,
                        DateOfBirth = model.DateOfBirth,
                        EmailVerified = false,
                        CreatedAt = DateTime.UtcNow
                    };
                }

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    var otp = _otpService.GenerateOTP();
                    user.OTP = otp;
                    user.OTPExpiration = DateTime.UtcNow.AddMinutes(10);
                    await _userManager.UpdateAsync(user);

                    // Send OTP email asynchronously (non-blocking)
                    _ = _emailService.SendOTPAsync(model.Email, otp);

                    TempData["Email"] = model.Email;
                    return RedirectToAction("VerifyOTP", new { email = model.Email });
                }

                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Register Error: {ex.Message}");
                ModelState.AddModelError(string.Empty, "An error occurred during registration. Please try again.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult VerifyOTP(string email, string purpose = "Registration")
        {
            var model = new VerifyOTPViewModel
            {
                Email = email,
                Purpose = purpose
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOTP(VerifyOTPViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "User not found.");
                    return View(model);
                }

                if (string.IsNullOrEmpty(user.OTP) || user.OTP != model.OTP)
                {
                    ModelState.AddModelError(nameof(model.OTP), "Invalid OTP.");
                    return View(model);
                }

                if (user.OTPExpiration < DateTime.UtcNow)
                {
                    ModelState.AddModelError(nameof(model.OTP), "OTP has expired. Please request a new one.");
                    return View(model);
                }

                if (model.Purpose == "Registration")
                {
                    user.EmailVerified = true;
                    user.OTP = null;
                    user.OTPExpiration = null;
                    await _userManager.UpdateAsync(user);
                    TempData["Success"] = "Email verified successfully. You can now login.";
                    return RedirectToAction("Welcome");
                }
                else if (model.Purpose == "PasswordReset")
                {
                    HttpContext.Session.SetString("ResetEmail", model.Email);
                    return RedirectToAction("ResetPassword");
                }

                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VerifyOTP Error: {ex.Message}");
                ModelState.AddModelError(string.Empty, "An error occurred. Please try again.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    ModelState.AddModelError(nameof(model.Email), "Email not found.");
                    return View(model);
                }

                var otp = _otpService.GenerateOTP();
                user.OTP = otp;
                user.OTPExpiration = DateTime.UtcNow.AddMinutes(10);
                await _userManager.UpdateAsync(user);

                try
                {
                    await _emailService.SendOTPAsync(model.Email, otp);
                }
                catch
                {
                    ModelState.AddModelError(string.Empty, "Error sending OTP. Please try again.");
                    return View(model);
                }

                return RedirectToAction("VerifyOTP", new { email = model.Email, purpose = "PasswordReset" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForgotPassword Error: {ex.Message}");
                ModelState.AddModelError(string.Empty, "An error occurred. Please try again.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            var email = HttpContext.Session.GetString("ResetEmail");
            if (string.IsNullOrEmpty(email))
                return RedirectToAction("ForgotPassword");

            var model = new ResetPasswordViewModel { Email = email };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "User not found.");
                    return View(model);
                }

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

                if (result.Succeeded)
                {
                    user.OTP = null;
                    user.OTPExpiration = null;
                    await _userManager.UpdateAsync(user);

                    HttpContext.Session.Remove("ResetEmail");
                    TempData["Success"] = "Password reset successfully. You can now login.";
                    return RedirectToAction("Welcome");
                }

                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetPassword Error: {ex.Message}");
                ModelState.AddModelError(string.Empty, "An error occurred. Please try again.");
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var user = await _userManager.FindByNameAsync(model.Username);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "Invalid username or password.");
                    return View(model);
                }

                // Check if email is verified
                if (!user.EmailVerified)
                {
                    ModelState.AddModelError(string.Empty, "Please verify your email first.");
                    return View(model);
                }

                // Sign in with persistent cookie support
                var result = await _signInManager.PasswordSignInAsync(
                    user,
                    model.Password,
                    isPersistent: model.RememberMe,
                    lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    // Update last login time
                    user.LastLoginAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                    
                    return RedirectToAction("Index", "Home");
                }

                if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "Account is locked. Please try again later.");
                    return View(model);
                }

                if (result.RequiresTwoFactor)
                {
                    return RedirectToAction("LoginWith2fa", new { rememberMe = model.RememberMe });
                }

                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login Error: {ex.Message}");
                ModelState.AddModelError(string.Empty, "An error occurred during login. Please try again.");
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Welcome");
        }
    }
}
