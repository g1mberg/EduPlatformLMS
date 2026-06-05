using dreams.Hubs;
using EduPlatform.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace dreams.Infrastructure;

/// <summary>
/// Web-side implementation of INotificationPusher. Лежит в Web проекте,
/// потому что зависит от SignalR-хаба (а хабы — это уже UI/edge слой).
/// </summary>
public class SignalRNotificationPusher : INotificationPusher
{
    private readonly IHubContext<NotificationsHub> _hub;
    public SignalRNotificationPusher(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task PushAsync(Guid userId, NotificationPayload payload, CancellationToken ct = default)
    {
        return _hub.Clients.Group(NotificationsHub.GroupName(userId)).SendAsync("Notify", new
        {
            id = payload.Id,
            title = payload.Title,
            message = payload.Message,
            at = payload.At,
            isRead = payload.IsRead
        }, ct);
    }
}
