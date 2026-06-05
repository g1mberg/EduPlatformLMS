using dreams.Data;
using dreams.Models.Courses;
using dreams.Models.Entities;
using dreams.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dreams.Controllers;

[Route("instructor")]
[Authorize(Roles = "Instructor,Admin")]
public class InstructorController : Controller
{
    private const int FreeMaxCourses = 3; // дефолт когда у инструктора нет активной подписки

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserActionLogger _audit;

    public InstructorController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, UserActionLogger audit)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpGet("profile")]
    public IActionResult Profile() => View();

    [AllowAnonymous]
    [HttpGet("subscription")]
    public IActionResult Subscription() => View("/Views/Subscription/Index.cshtml");

    // ---------- Список курсов инструктора ----------

    [HttpGet("courses")]
    public async Task<IActionResult> Courses()
    {
        var userId = GetUserId();

        var courses = await _db.Courses
            .Include(c => c.Category)
            .Where(c => c.InstructorId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var (used, limit) = await GetCourseLimitAsync(userId);
        ViewData["CoursesUsed"] = used;
        ViewData["CoursesLimit"] = limit; // null = безлимит
        return View(courses);
    }

    // ---------- Создание ----------

    [HttpGet("courses/new")]
    public async Task<IActionResult> CreateCourse()
    {
        var userId = GetUserId();
        var (used, limit) = await GetCourseLimitAsync(userId);
        if (limit is not null && used >= limit)
        {
            TempData["LimitError"] = $"Достигнут лимит курсов по подписке ({used}/{limit}). Обновите план.";
            return RedirectToAction(nameof(Courses));
        }

        var vm = new CourseFormViewModel
        {
            AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync()
        };
        return View("CourseForm", vm);
    }

    [HttpPost("courses/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCourse(CourseFormViewModel vm)
    {
        var userId = GetUserId();

        var (used, limit) = await GetCourseLimitAsync(userId);
        if (limit is not null && used >= limit)
        {
            ModelState.AddModelError(string.Empty, $"Достигнут лимит курсов ({used}/{limit}).");
        }

        await ValidateAndNormalizeAsync(vm, currentCourseId: null);

        if (!ModelState.IsValid)
        {
            vm.AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
            return View("CourseForm", vm);
        }

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Title = vm.Title.Trim(),
            Slug = vm.Slug!,
            Description = vm.Description?.Trim(),
            CoverUrl = string.IsNullOrWhiteSpace(vm.CoverUrl) ? "/assets/img/course/course-01.jpg" : vm.CoverUrl!.Trim(),
            CategoryId = vm.CategoryId,
            Price = vm.Price,
            Language = string.IsNullOrWhiteSpace(vm.Language) ? "ru" : vm.Language,
            IsPublished = vm.IsPublished,
            InstructorId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Courses.Add(course);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("course.create", "Course", course.Id.ToString(), course.Title);

        TempData["Toast"] = $"Курс «{course.Title}» создан";
        return RedirectToAction(nameof(Courses));
    }

    // ---------- Редактирование ----------

    [HttpGet("courses/{id:guid}/edit")]
    public async Task<IActionResult> EditCourse(Guid id)
    {
        var userId = GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        var vm = new CourseFormViewModel
        {
            Id = course.Id,
            Title = course.Title,
            Slug = course.Slug,
            Description = course.Description,
            CoverUrl = course.CoverUrl,
            CategoryId = course.CategoryId,
            Price = course.Price,
            Language = course.Language,
            IsPublished = course.IsPublished,
            AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync()
        };
        return View("CourseForm", vm);
    }

    [HttpPost("courses/{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCourse(Guid id, CourseFormViewModel vm)
    {
        var userId = GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        vm.Id = id;
        await ValidateAndNormalizeAsync(vm, currentCourseId: id);

        if (!ModelState.IsValid)
        {
            vm.AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
            return View("CourseForm", vm);
        }

        course.Title = vm.Title.Trim();
        course.Slug = vm.Slug!;
        course.Description = vm.Description?.Trim();
        course.CoverUrl = string.IsNullOrWhiteSpace(vm.CoverUrl) ? course.CoverUrl : vm.CoverUrl!.Trim();
        course.CategoryId = vm.CategoryId;
        course.Price = vm.Price;
        course.Language = string.IsNullOrWhiteSpace(vm.Language) ? "ru" : vm.Language;
        course.IsPublished = vm.IsPublished;
        course.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Toast"] = $"Курс «{course.Title}» обновлён";
        return RedirectToAction(nameof(Courses));
    }

    // ---------- Публикация / снятие ----------

    [HttpPost("courses/{id:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(Guid id)
    {
        var userId = GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        course.IsPublished = !course.IsPublished;
        course.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Toast"] = course.IsPublished ? "Курс опубликован" : "Курс снят с публикации";
        return RedirectToAction(nameof(Courses));
    }

    // ---------- Удаление ----------

    [HttpPost("courses/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourse(Guid id)
    {
        var userId = GetUserId();
        var course = await _db.Courses
            .Include(c => c.Enrollments)
            .FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        if (course.Enrollments.Any())
        {
            TempData["Toast"] = "Нельзя удалить курс, на который уже записались студенты. Снимите с публикации.";
            return RedirectToAction(nameof(Courses));
        }

        _db.Courses.Remove(course);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Курс удалён";
        return RedirectToAction(nameof(Courses));
    }

    // ---------- helpers ----------

    private Guid GetUserId()
    {
        var id = _userManager.GetUserId(User);
        return Guid.Parse(id!);
    }

    private async Task<(int used, int? limit)> GetCourseLimitAsync(Guid userId)
    {
        var used = await _db.Courses.CountAsync(c => c.InstructorId == userId);

        var activeSub = await _db.InstructorSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.InstructorId == userId && s.IsActive &&
                        (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        var limit = activeSub?.Plan.MaxCourses ?? FreeMaxCourses;
        return (used, limit);
    }

    private async Task ValidateAndNormalizeAsync(CourseFormViewModel vm, Guid? currentCourseId)
    {
        // Slug — авто если пусто
        if (string.IsNullOrWhiteSpace(vm.Slug))
            vm.Slug = SlugHelper.Generate(vm.Title);
        else
            vm.Slug = vm.Slug.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(vm.Slug))
            ModelState.AddModelError(nameof(vm.Slug), "Не удалось сгенерировать slug, задайте вручную");
        else
        {
            var taken = await _db.Courses.AnyAsync(c => c.Slug == vm.Slug && c.Id != currentCourseId);
            if (taken)
                ModelState.AddModelError(nameof(vm.Slug), "Такой slug уже занят");
        }

        if (!await _db.Categories.AnyAsync(c => c.Id == vm.CategoryId))
            ModelState.AddModelError(nameof(vm.CategoryId), "Выберите категорию");
    }
}
