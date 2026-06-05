using System.Text;
using System.Text.Encodings.Web;
using dreams.Models.Account;
using dreams.Models.Entities;
using dreams.Services;
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

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender email,
        IConfiguration config,
        UrlEncoder urlEncoder,
        UserActionLogger audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _email = email;
        _config = config;
        _urlEncoder = urlEncoder;
        _audit = audit;
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
}
