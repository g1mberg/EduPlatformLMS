using EduPlatform.Application.Abstractions;
using EduPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduPlatform.Application.Services;

/// <summary>
/// Создаёт Notification в БД и пушит её через INotificationPusher
/// (реализация в Infrastructure использует SignalR).
/// </summary>
public class NotificationService
{
    private readonly IApplicationDbContext _db;
    private readonly INotificationPusher _pusher;

    public NotificationService(IApplicationDbContext db, INotificationPusher pusher)
    {
        _db = db;
        _pusher = pusher;
    }

    public async Task NotifyAsync(Guid userId, string title, string message, CancellationToken ct = default)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync(ct);

        await _pusher.PushAsync(userId,
            new NotificationPayload(n.Id, n.Title, n.Message, n.CreatedAt, n.IsRead), ct);
    }

    public async Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default)
        => await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
}
