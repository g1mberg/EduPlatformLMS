using EduPlatform.Infrastructure.Persistence;
using EduPlatform.Domain.Entities;
using EduPlatform.Application.Abstractions;
using EduPlatform.Application.Services;
using EduPlatform.Infrastructure.Audit;
using EduPlatform.Infrastructure.Chat;
using EduPlatform.Infrastructure.Email;
using EduPlatform.Infrastructure.Logging;
using EduPlatform.Infrastructure.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dreams.Controllers;

[Authorize]
[Route("courses/{slug}/chat")]
public class ChatController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ChatMessageStore _store;

    public ChatController(ApplicationDbContext db, UserManager<ApplicationUser> users, ChatMessageStore store)
    {
        _db = db;
        _users = users;
        _store = store;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string slug)
    {
        var course = await _db.Courses
            .Include(c => c.Instructor)
            .FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var userId = Guid.Parse(_users.GetUserId(User)!);
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Challenge();

        var isInstructor = course.InstructorId == userId;
        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        var isAdmin = await _users.IsInRoleAsync(user, "Admin");
        if (!isInstructor && !isEnrolled && !isAdmin)
        {
            TempData["Toast"] = "Чат доступен инструктору курса и записанным студентам.";
            return Redirect($"/courses/{slug}");
        }

        ViewData["Course"] = course;
        ViewData["CurrentUserId"] = userId.ToString();
        ViewData["CurrentUserName"] = user.FullName ?? user.UserName ?? "";
        ViewData["CurrentAvatar"] = user.AvatarUrl ?? "";
        ViewData["IsInstructor"] = isInstructor;
        ViewData["MongoEnabled"] = _store.IsMongoEnabled;
        return View();
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(string slug, int limit = 50)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var userId = Guid.Parse(_users.GetUserId(User)!);
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Challenge();

        var isInstructor = course.InstructorId == userId;
        var isEnrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        var isAdmin = await _users.IsInRoleAsync(user, "Admin");
        if (!isInstructor && !isEnrolled && !isAdmin)
            return Forbid();

        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var msgs = await _store.GetHistoryAsync(course.Id, limit);
        return Json(msgs.Select(m => new
        {
            userId = m.UserId,
            userName = m.UserName,
            avatarUrl = m.AvatarUrl,
            text = m.Text,
            at = m.At,
            isInstructor = m.IsInstructor
        }));
    }
}
