using System.Diagnostics;
using System.Security.Claims;
using EduPlatform.Infrastructure.Mongo;
using Microsoft.AspNetCore.Http;

namespace EduPlatform.Infrastructure.Logging;

/// <summary>
/// Логирует каждый входящий HTTP-запрос в Mongo (http_logs).
/// Игнорирует статику (assets), favicon, hot-reload браузерные опросы.
/// </summary>
public class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MongoLogService _mongo;

    public HttpLoggingMiddleware(RequestDelegate next, MongoLogService mongo)
    {
        _next = next;
        _mongo = mongo;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var sw = Stopwatch.StartNew();
        Exception? thrown = null;
        try { await _next(ctx); }
        catch (Exception ex) { thrown = ex; throw; }
        finally
        {
            sw.Stop();
            if (ShouldLog(ctx.Request.Path))
            {
                _ = _mongo.LogHttpAsync(new HttpLogRecord
                {
                    Method = ctx.Request.Method,
                    Path = ctx.Request.Path.Value ?? "",
                    QueryString = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null,
                    Status = thrown is null ? ctx.Response.StatusCode : 500,
                    DurationMs = sw.ElapsedMilliseconds,
                    UserName = ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null,
                    UserId = ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier),
                    RemoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = ctx.Request.Headers.UserAgent.ToString()
                });
            }
        }
    }

    private static bool ShouldLog(PathString path)
    {
        var p = path.Value ?? "";
        return !p.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            && !p.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
            && !p.StartsWith("/_vs/", StringComparison.OrdinalIgnoreCase)
            && !p.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }
}
