using System.Security.Claims;
using EduPlatform.Infrastructure.Mongo;
using Microsoft.AspNetCore.Http;

namespace EduPlatform.Infrastructure.Audit;

/// <summary>
/// Удобная обёртка для логирования действий пользователя из контроллеров.
/// Берёт текущего юзера из HttpContext, не нужно передавать вручную.
/// </summary>
public class UserActionLogger
{
    private readonly MongoLogService _mongo;
    private readonly IHttpContextAccessor _http;

    public UserActionLogger(MongoLogService mongo, IHttpContextAccessor http)
    {
        _mongo = mongo;
        _http = http;
    }

    public Task LogAsync(string action, string? targetType = null, string? targetId = null, string? details = null)
    {
        var user = _http.HttpContext?.User;
        return _mongo.LogActionAsync(new UserActionRecord
        {
            Action = action,
            UserName = user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null,
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            TargetType = targetType,
            TargetId = targetId,
            Details = details
        });
    }
}
