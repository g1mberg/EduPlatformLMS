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
[Route("courses/{slug}/review")]
public class ReviewsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly UserActionLogger _audit;
    private readonly NotificationService _notifications;

    public ReviewsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
        UserActionLogger audit, NotificationService notifications)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _notifications = notifications;
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string slug, int rating, string? text)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var userId = Guid.Parse(_users.GetUserId(User)!);

        if (course.InstructorId == userId)
        {
            TempData["Toast"] = "Нельзя оценить свой курс.";
            return Redirect($"/courses/{slug}");
        }

        var enrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (!enrolled)
        {
            TempData["Toast"] = "Оставлять отзыв могут только записанные студенты.";
            return Redirect($"/courses/{slug}");
        }

        if (rating < 1 || rating > 5)
        {
            TempData["Toast"] = "Оценка должна быть от 1 до 5.";
            return Redirect($"/courses/{slug}");
        }

        var existing = await _db.Reviews.FirstOrDefaultAsync(r => r.CourseId == course.Id && r.StudentId == userId);
        if (existing is not null)
        {
            // Редактирование своего отзыва — снова на премодерацию
            existing.Rating = rating;
            existing.Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            existing.IsApproved = true; // авто-аппрув; админ может отклонить позже
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Reviews.Add(new Review
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                StudentId = userId,
                Rating = rating,
                Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                IsApproved = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await RecalcAverageAsync(course.Id);
        await _audit.LogAsync("course.review", "Course", course.Id.ToString(), $"{rating}★");
        await _notifications.NotifyAsync(course.InstructorId,
            $"Новый отзыв на курс «{course.Title}»",
            $"Оценка: {rating}★. {(string.IsNullOrWhiteSpace(text) ? "" : "«" + text!.Trim() + "»")}");

        TempData["Toast"] = "Спасибо за отзыв!";
        return Redirect($"/courses/{slug}#reviews");
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string slug)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var userId = Guid.Parse(_users.GetUserId(User)!);
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.CourseId == course.Id && r.StudentId == userId);
        if (review is null) return Redirect($"/courses/{slug}");

        _db.Reviews.Remove(review);
        await _db.SaveChangesAsync();
        await RecalcAverageAsync(course.Id);
        await _audit.LogAsync("course.review.delete", "Course", course.Id.ToString());

        TempData["Toast"] = "Отзыв удалён.";
        return Redirect($"/courses/{slug}#reviews");
    }

    private async Task RecalcAverageAsync(Guid courseId)
    {
        var approved = await _db.Reviews
            .Where(r => r.CourseId == courseId && r.IsApproved)
            .Select(r => r.Rating)
            .ToListAsync();

        var course = await _db.Courses.FindAsync(courseId);
        if (course is null) return;
        course.AverageRating = approved.Count == 0 ? 0m : Math.Round((decimal)approved.Average(), 2);
        await _db.SaveChangesAsync();
    }
}
