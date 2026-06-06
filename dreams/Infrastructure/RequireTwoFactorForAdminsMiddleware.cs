using EduPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace dreams.Infrastructure;

/// <summary>
/// Гонит залогиненных админов без включённой 2FA на /account/2fa-setup,
/// пока не настроят. ТЗ требует обязательной 2FA для администраторов.
/// </summary>
public class RequireTwoFactorForAdminsMiddleware
{
    private readonly RequestDelegate _next;

    // Пути, на которых можно быть даже без 2FA: настройка самой 2FA, выход,
    // страницы ошибок, статика, SignalR-хабы, смена культуры.
    private static readonly string[] AllowedPrefixes =
    {
        "/account/2fa-setup",
        "/account/logout",
        "/account/security",
        "/account/profile",
        "/error",
        "/hubs",
        "/lib",
        "/assets",
        "/css",
        "/js",
        "/favicon"
    };

    public RequireTwoFactorForAdminsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, UserManager<ApplicationUser> users)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true && ctx.User.IsInRole("Admin"))
        {
            var path = ctx.Request.Path.Value ?? "";
            bool allowed = false;
            foreach (var p in AllowedPrefixes)
            {
                if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { allowed = true; break; }
            }

            if (!allowed)
            {
                var user = await users.GetUserAsync(ctx.User);
                if (user is not null && !await users.GetTwoFactorEnabledAsync(user))
                {
                    ctx.Response.Redirect("/account/2fa-setup?required=1");
                    return;
                }
            }
        }
        await _next(ctx);
    }
}
