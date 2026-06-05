using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace dreams.Controllers;

[Route("language")]
public class LanguageController : Controller
{
    [HttpGet("set")]
    public IActionResult Set(string culture, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(culture)) culture = "ru";
        if (culture != "ru" && culture != "en") culture = "ru";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = false
            });

        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }
}
