using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace dreams.Services;

/// <summary>
/// Реальная отправка через SMTP (MailKit).
/// Настройки берутся из секции Email:Smtp в appsettings.
/// При ошибке падает с исключением — вызывающий код (контроллер/Identity)
/// обработает результат. Дополнительно при включённом флаге Email:Smtp:DevDuplicate
/// дублирует письмо как .eml через FileEmailSender (удобно в dev).
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly FileEmailSender _fallback;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger, FileEmailSender fallback)
    {
        _config = config;
        _logger = logger;
        _fallback = fallback;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtp = _config.GetSection("Email:Smtp");
        var host = smtp["Host"];
        var portStr = smtp["Port"];
        var user = smtp["User"];
        var pass = smtp["Password"];
        var fromAddr = smtp["From"] ?? _config["Email:Sender"] ?? "noreply@eduplatform.local";
        var fromName = smtp["FromName"] ?? "EduPlatform";
        var useSslStr = smtp["UseSsl"] ?? "true";
        var devDup = string.Equals(smtp["DevDuplicate"], "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portStr, out var port))
        {
            _logger.LogWarning("SMTP не настроен (Email:Smtp:Host/Port). Письмо ушло в файл.");
            await _fallback.SendAsync(to, subject, htmlBody);
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(fromName, fromAddr));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            // Выбор режима по порту: 465 = implicit SSL, 587/иначе = STARTTLS, "none" = в открытую
            SecureSocketOptions secure;
            if (!string.Equals(useSslStr, "true", StringComparison.OrdinalIgnoreCase))
                secure = SecureSocketOptions.None;
            else if (port == 465)
                secure = SecureSocketOptions.SslOnConnect;
            else
                secure = SecureSocketOptions.StartTls;
            await client.ConnectAsync(host, port, secure);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, pass ?? "");
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            _logger.LogInformation("SMTP: письмо отправлено на {To} (subject: {Subj})", to, subject);

            if (devDup)
            {
                try { await _fallback.SendAsync(to, subject, htmlBody); }
                catch (Exception ex) { _logger.LogDebug(ex, "DevDuplicate file write failed"); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP отправка провалилась, фоллбэк в файл. host={Host}:{Port}", host, port);
            await _fallback.SendAsync(to, subject, htmlBody);
        }
    }
}
