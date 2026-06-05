using dreams.Data;
using dreams.Models.Entities;
using dreams.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace dreams.Hubs;

[Authorize]
public class CourseChatHub : Hub
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ChatMessageStore _store;

    public CourseChatHub(ApplicationDbContext db, UserManager<ApplicationUser> users, ChatMessageStore store)
    {
        _db = db;
        _users = users;
        _store = store;
    }

    public async Task<bool> JoinCourse(string courseId)
    {
        if (!Guid.TryParse(courseId, out var cid)) return false;
        var user = await GetUserAsync();
        if (user is null) return false;

        if (!await CanAccessAsync(user, cid)) return false;

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(cid));
        return true;
    }

    public async Task LeaveCourse(string courseId)
    {
        if (!Guid.TryParse(courseId, out var cid)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(cid));
    }

    public async Task SendMessage(string courseId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > 2000) text = text[..2000];

        if (!Guid.TryParse(courseId, out var cid)) return;
        var user = await GetUserAsync();
        if (user is null) return;
        if (!await CanAccessAsync(user, cid)) return;

        var isInstructor = await _db.Courses.AnyAsync(c => c.Id == cid && c.InstructorId == user.Id);

        var msg = new ChatMessage
        {
            CourseId = cid,
            UserId = user.Id,
            UserName = user.FullName ?? user.UserName ?? "",
            AvatarUrl = user.AvatarUrl,
            Text = text,
            At = DateTime.UtcNow,
            IsInstructor = isInstructor
        };
        await _store.SaveAsync(msg);

        await Clients.Group(GroupName(cid)).SendAsync("ReceiveMessage", new
        {
            userId = msg.UserId,
            userName = msg.UserName,
            avatarUrl = msg.AvatarUrl,
            text = msg.Text,
            at = msg.At,
            isInstructor = msg.IsInstructor
        });
    }

    private static string GroupName(Guid courseId) => $"course-{courseId}";

    private async Task<ApplicationUser?> GetUserAsync()
    {
        var idStr = _users.GetUserId(Context.User!);
        if (string.IsNullOrEmpty(idStr)) return null;
        return await _users.FindByIdAsync(idStr);
    }

    private async Task<bool> CanAccessAsync(ApplicationUser user, Guid courseId)
    {
        // Преподаватель курса
        if (await _db.Courses.AnyAsync(c => c.Id == courseId && c.InstructorId == user.Id))
            return true;
        // Записанный студент
        if (await _db.Enrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == user.Id))
            return true;
        // Админ
        if (await _users.IsInRoleAsync(user, "Admin"))
            return true;
        return false;
    }
}
