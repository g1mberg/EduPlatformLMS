namespace EduPlatform.Application.Abstractions;

/// <summary>
/// Абстракция над real-time доставкой уведомлений на клиент.
/// Реализация (Infrastructure) использует SignalR-хаб.
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, NotificationPayload payload, CancellationToken ct = default);
}

public record NotificationPayload(Guid Id, string Title, string Message, DateTime At, bool IsRead);
