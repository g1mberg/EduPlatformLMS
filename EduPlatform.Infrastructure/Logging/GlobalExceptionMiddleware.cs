using System.Diagnostics;
using EduPlatform.Infrastructure.Mongo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EduPlatform.Infrastructure.Logging;

/// <summary>
/// Глобальный перехватчик необработанных исключений: пишет полный stacktrace
/// в Mongo (с траектидом), логирует через ILogger и редиректит на /error/500
/// (стандартный StatusCodePagesWithReExecute дальше отрисует нашу страницу).
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly MongoLogService _mongo;

    public GlobalExceptionMiddleware(RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        MongoLogService mongo)
    {
        _next = next;
        _logger = logger;
        _mongo = mongo;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;
            _logger.LogError(ex, "Unhandled exception. Path={Path} TraceId={Trace}",
                ctx.Request.Path, traceId);

            // В Mongo как user_actions с типом error — удобно искать в одной коллекции
            _ = _mongo.LogActionAsync(new UserActionRecord
            {
                Action = "error.unhandled",
                UserName = ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null,
                UserId = ctx.User?.FindFirst("sub")?.Value,
                TargetType = "Request",
                TargetId = ctx.Request.Path,
                Details = $"{ex.GetType().Name}: {ex.Message} | trace={traceId}"
            });

            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = 500;
                // ловит StatusCodePagesWithReExecute и рендерит наш /error/500
            }
        }
    }
}
