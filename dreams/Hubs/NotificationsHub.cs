using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace dreams.Hubs;

/// <summary>
/// Хаб для real-time уведомлений на персональную ленту пользователя.
/// Группа = userId. На клиент шлём событие "Notify" с DTO {id, title, message, at, unread}.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public override Task OnConnectedAsync()
    {
        var uid = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(uid))
            Groups.AddToGroupAsync(Context.ConnectionId, GroupName(uid));
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(uid))
            Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(uid));
        return base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(string userId) => $"user-{userId}";
    public static string GroupName(Guid userId) => GroupName(userId.ToString());
}
