using EduPlatform.Infrastructure.Persistence;
using dreams.Models.Courses;
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

[Route("courses")]
public class CoursesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserActionLogger _audit;
    private readonly NotificationService _notifications;

    public CoursesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        UserActionLogger audit, NotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _notifications = notifications;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search = null,
        string? category = null,
        string? price = null,
        string sort = "new",
        int page = 1)
    {
        const int pageSize = 9;
        if (page < 1) page = 1;

        var query = _db.Courses
            .Include(c => c.Category)
            .Include(c => c.Instructor)
            .Where(c => c.IsPublished);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.Title.Contains(s) || (c.Description != null && c.Description.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(c => c.Category.Slug == category);

        if (price == "free")  query = query.Where(c => c.Price == 0);
        else if (price == "paid") query = query.Where(c => c.Price > 0);

        query = sort switch
        {
            "rating"     => query.OrderByDescending(c => c.AverageRating),
            "students"   => query.OrderByDescending(c => c.StudentsCount),
            "price-asc"  => query.OrderBy(c => c.Price),
            "price-desc" => query.OrderByDescending(c => c.Price),
            _            => query.OrderByDescending(c => c.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryFacet(c.Slug, c.Name, c.Courses.Count(co => co.IsPublished)))
            .ToListAsync();

        var vm = new CourseCatalogViewModel
        {
            Courses = items, Categories = categories,
            Search = search, CategorySlug = category, Price = price, Sort = sort,
            Page = page, PageSize = pageSize, TotalCount = total
        };
        return View(vm);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var course = await _db.Courses
            .Include(c => c.Category)
            .Include(c => c.Instructor)
            .Include(c => c.Sections.OrderBy(s => s.OrderIndex))
                .ThenInclude(s => s.Lessons.OrderBy(l => l.OrderIndex))
            .FirstOrDefaultAsync(c => c.Slug == slug);

        if (course is null) return NotFound();

        var userIdStr = _userManager.GetUserId(User);
        Guid? userId = userIdStr is null ? null : Guid.Parse(userIdStr);

        var isEnrolled = userId is not null &&
            await _db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        var isOwner = userId is not null && course.InstructorId == userId;

        // Не опубликованные курсы видны только владельцу
        if (!course.IsPublished && !isOwner) return NotFound();

        // Отзывы: показываем одобренные + свой (даже неодобренный); агрегаты по одобренным
        var reviews = await _db.Reviews
            .Include(r => r.Student)
            .Where(r => r.CourseId == course.Id && (r.IsApproved || r.StudentId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync();

        var approved = reviews.Where(r => r.IsApproved).ToList();
        var histogram = new int[5]; // index 0 = 5 stars
        foreach (var r in approved)
        {
            var idx = 5 - Math.Clamp(r.Rating, 1, 5);
            histogram[idx]++;
        }

        Review? myReview = userId is null ? null : reviews.FirstOrDefault(r => r.StudentId == userId);
        bool canLeave = isEnrolled && !isOwner && myReview is null;

        var vm = new CourseDetailsViewModel
        {
            Course = course,
            IsEnrolled = isEnrolled,
            IsOwner = isOwner,
            LessonsCount = course.Sections.Sum(s => s.Lessons.Count),
            Reviews = reviews,
            ReviewsCount = approved.Count,
            MyReview = myReview,
            CanLeaveReview = canLeave,
            RatingHistogram = histogram
        };
        return View(vm);
    }

    [HttpPost("{slug}/enroll")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(string slug)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Slug == slug && c.IsPublished);
        if (course is null) return NotFound();

        var userId = Guid.Parse(_userManager.GetUserId(User)!);

        if (course.InstructorId == userId)
        {
            TempData["Toast"] = "Нельзя записаться на свой собственный курс.";
            return RedirectToAction(nameof(Details), new { slug });
        }

        var already = await _db.Enrollments.AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (!already)
        {
            _db.Enrollments.Add(new Enrollment
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                StudentId = userId,
                EnrolledAt = DateTime.UtcNow,
                ProgressPercent = 0m
            });
            course.StudentsCount += 1;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("course.enroll", "Course", course.Id.ToString(), course.Title);
            var student = await _userManager.FindByIdAsync(userId.ToString());
            await _notifications.NotifyAsync(course.InstructorId,
                $"Новая запись на курс «{course.Title}»",
                $"{student?.FullName ?? "Студент"} только что записался на ваш курс.");
            TempData["Toast"] = $"Вы записаны на курс «{course.Title}».";
        }

        return RedirectToAction("CourseOverview", "Student", new { slug });
    }
}
