using System.Text;
using System.Text.Encodings.Web;
using dreams.Models.Account;
using EduPlatform.Domain.Entities;
using EduPlatform.Application.Abstractions;
using EduPlatform.Application.Services;
using EduPlatform.Infrastructure.Audit;
using EduPlatform.Infrastructure.Chat;
using EduPlatform.Infrastructure.Email;
using EduPlatform.Infrastructure.Logging;
using EduPlatform.Infrastructure.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace dreams.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly UrlEncoder _urlEncoder;
    private readonly UserActionLogger _audit;
    private readonly MongoLogService _mongo;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender email,
        IConfiguration config,
        UrlEncoder urlEncoder,
        UserActionLogger audit,
        MongoLogService mongo)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _email = email;
        _config = config;
        _urlEncoder = urlEncoder;
        _audit = audit;
        _mongo = mongo;
    }

    // ---------- LOGIN ----------

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Пользователь с таким email не найден");
            return View(vm);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user, vm.Password, vm.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            await _audit.LogAsync("login.success", "User", user.Id.ToString(), user.Email);
            return LocalRedirect(returnUrl ?? "/");
        }

        if (result.RequiresTwoFactor)
        {
            await _audit.LogAsync("login.requires_2fa", "User", user.Id.ToString(), user.Email);
            return RedirectToAction(nameof(LoginTwoFactor), new { returnUrl, rememberMe = vm.RememberMe });
        }

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Аккаунт заблокирован. Попробуйте позже");
        else if (result.IsNotAllowed)
        {
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                ViewData["NeedsConfirmation"] = true;
                ViewData["UnconfirmedEmail"] = user.Email;
                ModelState.AddModelError(string.Empty, "Подтвердите email — мы отправили вам письмо со ссылкой.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Вход запрещён.");
            }
        }
        else
            ModelState.AddModelError(string.Empty, "Неверный пароль");

        return View(vm);
    }

    [HttpGet("login-2fa")]
    public async Task<IActionResult> LoginTwoFactor(string? returnUrl = null, bool rememberMe = false)
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToAction(nameof(Login));

        return View(new TwoFactorLoginViewModel { ReturnUrl = returnUrl, RememberMe = rememberMe });
    }

    [HttpPost("login-2fa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginTwoFactor(TwoFactorLoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToAction(nameof(Login));

        var code = vm.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            code, vm.RememberMe, rememberClient: false);

        if (result.Succeeded)
            return LocalRedirect(vm.ReturnUrl ?? "/");

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Аккаунт временно заблокирован");
        else
            ModelState.AddModelError(string.Empty, "Неверный код");
        return View(vm);
    }

    // ---------- REGISTER ----------

    [HttpGet("register")]
    public IActionResult Register() => View();

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FullName = vm.FullName,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return View(vm);
        }

        await _userManager.AddToRoleAsync(user, "Student");
        await SendConfirmationEmailAsync(user);
        await _audit.LogAsync("register", "User", user.Id.ToString(), user.Email);

        return RedirectToAction(nameof(ConfirmEmailSent), new { email = user.Email });
    }

    [HttpGet("confirm-email-sent")]
    public IActionResult ConfirmEmailSent(string email)
    {
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return View("ConfirmEmailResult", (false, "Некорректная ссылка"));

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return View("ConfirmEmailResult", (false, "Пользователь не найден"));

        var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        var result = await _userManager.ConfirmEmailAsync(user, decoded);
        return View("ConfirmEmailResult",
            result.Succeeded
                ? (true,  "Email подтверждён. Теперь можно войти.")
                : (false, "Ссылка устарела или некорректна."));
    }

    [HttpPost("resend-confirmation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null && !await _userManager.IsEmailConfirmedAsync(user))
            await SendConfirmationEmailAsync(user);

        TempData["Toast"] = "Если такой пользователь существует, мы отправили новое письмо.";
        return RedirectToAction(nameof(ConfirmEmailSent), new { email });
    }

    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var link = $"{baseUrl}/account/confirm-email?userId={user.Id}&token={encoded}";

        var html = $"""
            <h2>Подтверждение email</h2>
            <p>Здравствуйте, {System.Net.WebUtility.HtmlEncode(user.FullName ?? user.Email!)}!</p>
            <p>Подтвердите email, перейдя по ссылке:</p>
            <p><a href="{link}">{link}</a></p>
            <p>Если вы не регистрировались — просто проигнорируйте это письмо.</p>
            """;
        await _email.SendAsync(user.Email!, "EduPlatform LMS — подтверждение email", html);
    }

    // ---------- LOGOUT ----------

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ---------- 2FA ----------

    [HttpGet("security")]
    [Authorize]
    public async Task<IActionResult> Security()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        ViewData["TwoFactorEnabled"] = await _userManager.GetTwoFactorEnabledAsync(user);
        ViewData["EmailConfirmed"] = await _userManager.IsEmailConfirmedAsync(user);
        return View();
    }

    [HttpGet("2fa-setup")]
    [Authorize]
    public async Task<IActionResult> TwoFactorSetup()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        var issuer = "EduPlatform LMS";
        var uri = $"otpauth://totp/{_urlEncoder.Encode(issuer)}:{_urlEncoder.Encode(user.Email!)}" +
                  $"?secret={key}&issuer={_urlEncoder.Encode(issuer)}&digits=6";

        return View(new TwoFactorSetupViewModel
        {
            SharedKey = FormatKey(key!),
            AuthenticatorUri = uri
        });
    }

    [HttpPost("2fa-setup")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorSetup(TwoFactorSetupViewModel vm)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var code = vm.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var ok = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!ok)
        {
            ModelState.AddModelError(nameof(vm.Code), "Неверный код. Попробуйте ещё раз.");
            // нужно перезаполнить ключ для повторного отображения
            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            vm.SharedKey = FormatKey(key!);
            vm.AuthenticatorUri = $"otpauth://totp/{_urlEncoder.Encode("EduPlatform LMS")}:{_urlEncoder.Encode(user.Email!)}" +
                                  $"?secret={key}&issuer={_urlEncoder.Encode("EduPlatform LMS")}&digits=6";
            return View(vm);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        TempData["Toast"] = "Двухфакторная аутентификация включена.";
        return RedirectToAction(nameof(Security));
    }

    [HttpPost("2fa-disable")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorDisable()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        TempData["Toast"] = "2FA отключена.";
        return RedirectToAction(nameof(Security));
    }

    private static string FormatKey(string key) =>
        string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));

    // ---------- FORGOT PASSWORD ----------

    [HttpGet("forgot")]
    public IActionResult Forgot() => View();

    [HttpPost("forgot")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Forgot(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ViewData["Toast"] = "Введите email.";
            return View();
        }

        // Безопасный ответ: всегда говорим «если такой email есть — мы прислали письмо»
        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null && await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
            var link = $"{baseUrl}/account/reset?userId={user.Id}&token={encoded}";

            var html = $"""
                <h2>Сброс пароля</h2>
                <p>Здравствуйте, {System.Net.WebUtility.HtmlEncode(user.FullName ?? user.Email ?? "")}.</p>
                <p>Чтобы задать новый пароль, перейдите по ссылке (действительна 24 часа):</p>
                <p><a href="{link}">{link}</a></p>
                <p>Если вы не запрашивали сброс — просто проигнорируйте письмо.</p>
                """;
            await _email.SendAsync(user.Email!, "EduPlatform LMS — сброс пароля", html);
            await _audit.LogAsync("account.password.reset.request", "User", user.Id.ToString());
        }

        ViewData["Sent"] = true;
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet("reset")]
    public IActionResult Reset(string? userId, string? token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            return View(new ResetPasswordViewModel { Error = "Некорректная ссылка." });
        return View(new ResetPasswordViewModel { UserId = userId, Token = token });
    }

    [HttpPost("reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(ResetPasswordViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.UserId) || string.IsNullOrEmpty(vm.Token))
        {
            vm.Error = "Некорректная ссылка.";
            return View(vm);
        }
        if (string.IsNullOrEmpty(vm.Password) || vm.Password.Length < 8)
        {
            vm.Error = "Пароль должен быть не короче 8 символов.";
            return View(vm);
        }
        if (vm.Password != vm.ConfirmPassword)
        {
            vm.Error = "Пароли не совпадают.";
            return View(vm);
        }
        var user = await _userManager.FindByIdAsync(vm.UserId);
        if (user is null)
        {
            vm.Error = "Пользователь не найден.";
            return View(vm);
        }
        var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(vm.Token));
        var res = await _userManager.ResetPasswordAsync(user, decoded, vm.Password);
        if (!res.Succeeded)
        {
            vm.Error = "Ссылка устарела или некорректна. " + string.Join("; ", res.Errors.Select(e => e.Description));
            return View(vm);
        }

        if (user.LockoutEnd.HasValue)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }
        await _audit.LogAsync("account.password.reset.complete", "User", user.Id.ToString());
        vm.Success = true;
        return View(vm);
    }

    // ---------- PROFILE ----------

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var roles = await _userManager.GetRolesAsync(user);
        ViewData["Roles"] = roles;
        ViewData["Is2FA"] = await _userManager.GetTwoFactorEnabledAsync(user);
        ViewData["MongoEnabled"] = _mongo.IsEnabled;
        ViewData["Actions"] = await _mongo.GetUserActionsAsync(user.Id.ToString(), 30);
        return View(new ProfileViewModel
        {
            Email = user.Email ?? "",
            FullName = user.FullName,
            Bio = user.Bio,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt,
            Credits = user.Credits
        });
    }

    [HttpPost("profile")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel vm)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        user.FullName = string.IsNullOrWhiteSpace(vm.FullName) ? user.FullName : vm.FullName.Trim();
        user.Bio = string.IsNullOrWhiteSpace(vm.Bio) ? null : vm.Bio.Trim();
        if (!string.IsNullOrWhiteSpace(vm.AvatarUrl)) user.AvatarUrl = vm.AvatarUrl.Trim();

        var res = await _userManager.UpdateAsync(user);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError(string.Empty, e.Description);
            var roles = await _userManager.GetRolesAsync(user);
            ViewData["Roles"] = roles;
            ViewData["Is2FA"] = await _userManager.GetTwoFactorEnabledAsync(user);
            vm.Email = user.Email ?? "";
            vm.CreatedAt = user.CreatedAt;
            vm.Credits = user.Credits;
            return View(vm);
        }

        TempData["Toast"] = "Профиль обновлён.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("password-change")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            TempData["Toast"] = "Новый пароль должен быть не короче 8 символов.";
            return RedirectToAction(nameof(Profile));
        }
        if (newPassword != confirmPassword)
        {
            TempData["Toast"] = "Пароли не совпадают.";
            return RedirectToAction(nameof(Profile));
        }

        var res = await _userManager.ChangePasswordAsync(user, currentPassword ?? "", newPassword);
        if (!res.Succeeded)
        {
            TempData["Toast"] = "Не удалось сменить пароль: " + string.Join("; ", res.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Profile));
        }

        await _signInManager.RefreshSignInAsync(user);
        await _audit.LogAsync("account.password.change", "User", user.Id.ToString());
        TempData["Toast"] = "Пароль изменён.";
        return RedirectToAction(nameof(Profile));
    }
}

public class ProfileViewModel
{
    public string Email { get; set; } = "";
    public string? FullName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Credits { get; set; }
}

public class ResetPasswordViewModel
{
    public string? UserId { get; set; }
    public string? Token { get; set; }
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
