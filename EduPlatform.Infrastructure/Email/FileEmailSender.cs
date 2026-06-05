using System.Globalization;
using System.Text;
using EduPlatform.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduPlatform.Infrastructure.Email;

/// <summary>
/// Dev-реализация: пишет письмо как .eml-файл в App_Data/mail/, без реальной отправки.
/// Достаточно для проверки confirmation/2FA-flow в локальной разработке.
/// </summary>
public class FileEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileEmailSender> _logger;

    public FileEmailSender(IConfiguration config, IWebHostEnvironment env, ILogger<FileEmailSender> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var from = _config["Email:Sender"] ?? "noreply@eduplatform.local";
        var dir = Path.Combine(_env.ContentRootPath, "App_Data", "mail");
        Directory.CreateDirectory(dir);

        var file = Path.Combine(dir,
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Sanitize(to)}.eml");

        var sb = new StringBuilder();
        sb.AppendLine($"From: {from}");
        sb.AppendLine($"To: {to}");
        sb.AppendLine($"Subject: {subject}");
        sb.AppendLine($"Date: {DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture)}");
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine("Content-Type: text/html; charset=utf-8");
        sb.AppendLine();
        sb.Append(htmlBody);

        await File.WriteAllTextAsync(file, sb.ToString(), new UTF8Encoding(false));
        _logger.LogInformation("Email saved: {File} (to {To}, subject: {Subject})", file, to, subject);
    }

    private static string Sanitize(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
}
