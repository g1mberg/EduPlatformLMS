using System.ComponentModel.DataAnnotations;
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

[Route("admin")]
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserActionLogger _audit;
    private readonly MongoLogService _mongo;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        UserActionLogger audit,
        MongoLogService mongo)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _mongo = mongo;
    }

    // ---------- DASHBOARD ----------

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var vm = new AdminDashboardVm
        {
            UsersTotal       = await _db.Users.CountAsync(),
            CoursesTotal     = await _db.Courses.CountAsync(),
            EnrollmentsTotal = await _db.Enrollments.CountAsync(),
            CertificatesTotal= await _db.Certificates.CountAsync(),
            LatestUsers      = await _db.Users.OrderByDescending(u => u.CreatedAt).Take(5).ToListAsync(),
            LatestEnrollments = await _db.Enrollments
                .Include(e => e.Student)
                .Include(e => e.Course)
                .OrderByDescending(e => e.EnrolledAt)
                .Take(7)
                .ToListAsync()
        };
        return View(vm);
    }

    // ---------- USERS ----------

    [HttpGet("users")]
    public async Task<IActionResult> Users(string? role = null, string? q = null, string? blocked = null)
    {
        var query = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email!.Contains(q) || u.FullName!.Contains(q));
        if (blocked == "yes")    query = query.Where(u => u.IsBlocked);
        else if (blocked == "no") query = query.Where(u => !u.IsBlocked);

        var users = await query.OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync();

        // Подтянем роли пакетно
        var roleMap = new Dictionary<Guid, string[]>();
        foreach (var u in users)
            roleMap[u.Id] = (await _userManager.GetRolesAsync(u)).ToArray();

        if (!string.IsNullOrWhiteSpace(role))
            users = users.Where(u => roleMap[u.Id].Contains(role, StringComparer.OrdinalIgnoreCase)).ToList();

        ViewData["Roles"] = roleMap;
        ViewData["Q"] = q;
        ViewData["RoleFilter"] = role;
        ViewData["BlockedFilter"] = blocked;
        return View(users);
    }

    [HttpPost("users/{id:guid}/credits")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCredits(Guid id, decimal amount)
    {
        var u = await _userManager.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();
        u.Credits += amount;
        _db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = u.Id,
            Amount = amount,
            Type = TransactionType.TopUp,
            CreatedAt = DateTime.UtcNow,
            Description = $"Начисление от администратора {User.Identity?.Name}"
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("admin.credits.grant", "User", u.Id.ToString(), $"{amount:N0} → {u.Email}");
        TempData["Toast"] = $"Начислено {amount:N0} кредитов пользователю {u.Email}";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("users/{id:guid}/block")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBlock(Guid id)
    {
        var u = await _userManager.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();
        u.IsBlocked = !u.IsBlocked;
        // Identity LockoutEnd: бессрочно если IsBlocked
        u.LockoutEnd = u.IsBlocked ? DateTimeOffset.MaxValue : null;
        await _userManager.UpdateAsync(u);
        await _audit.LogAsync(u.IsBlocked ? "admin.user.block" : "admin.user.unblock", "User", u.Id.ToString(), u.Email);
        TempData["Toast"] = u.IsBlocked ? $"Пользователь {u.Email} заблокирован." : $"Пользователь {u.Email} разблокирован.";
        return RedirectToAction(nameof(Users));
    }

    // ---------- COURSES ----------

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(string? q = null)
    {
        var query = _db.Courses.Include(c => c.Category).Include(c => c.Instructor).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.Title.Contains(q) || c.Slug.Contains(q));
        var items = await query.OrderByDescending(c => c.UpdatedAt).Take(200).ToListAsync();
        ViewData["Q"] = q;
        return View(items);
    }

    [HttpPost("courses/{id:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(Guid id)
    {
        var c = await _db.Courses.FindAsync(id);
        if (c is null) return NotFound();
        c.IsPublished = !c.IsPublished;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Toast"] = c.IsPublished ? "Курс опубликован." : "Курс снят с публикации.";
        return RedirectToAction(nameof(Courses));
    }

    [HttpPost("courses/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCourse(Guid id)
    {
        var c = await _db.Courses.Include(x => x.Enrollments).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (c.Enrollments.Any())
        {
            TempData["Error"] = "Нельзя удалить курс с записанными студентами.";
            return RedirectToAction(nameof(Courses));
        }
        _db.Courses.Remove(c);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Курс удалён.";
        return RedirectToAction(nameof(Courses));
    }

    // ---------- CATEGORIES ----------

    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        var cats = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, c.Slug, c.Courses.Count))
            .ToListAsync();
        return View(cats);
    }

    [HttpPost("categories/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory([Required] string name, string? slug)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Введите название";
            return RedirectToAction(nameof(Categories));
        }
        var s = string.IsNullOrWhiteSpace(slug) ? SlugHelper.Generate(name) : slug.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) { TempData["Error"] = "Slug пуст"; return RedirectToAction(nameof(Categories)); }
        if (await _db.Categories.AnyAsync(c => c.Slug == s))
        {
            TempData["Error"] = $"Категория со slug «{s}» уже существует";
            return RedirectToAction(nameof(Categories));
        }
        _db.Categories.Add(new Category { Name = name.Trim(), Slug = s });
        await _db.SaveChangesAsync();
        TempData["Toast"] = $"Категория «{name}» создана.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("categories/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, [Required] string name, [Required] string slug)
    {
        var cat = await _db.Categories.FindAsync(id);
        if (cat is null) return NotFound();
        slug = slug.Trim().ToLowerInvariant();
        if (await _db.Categories.AnyAsync(c => c.Slug == slug && c.Id != id))
        {
            TempData["Error"] = $"Slug «{slug}» уже занят другой категорией";
            return RedirectToAction(nameof(Categories));
        }
        cat.Name = name.Trim();
        cat.Slug = slug;
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Категория обновлена.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("categories/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var cat = await _db.Categories.Include(c => c.Courses).FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound();
        if (cat.Courses.Any())
        {
            TempData["Error"] = $"В категории «{cat.Name}» есть {cat.Courses.Count} курс(ов). Сначала перенесите их.";
            return RedirectToAction(nameof(Categories));
        }
        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Категория удалена.";
        return RedirectToAction(nameof(Categories));
    }

    // ---------- PLANS ----------

    [HttpGet("plans")]
    public async Task<IActionResult> Plans()
    {
        var plans = await _db.SubscriptionPlans.OrderBy(p => p.Id).ToListAsync();
        return View(plans);
    }

    [HttpPost("plans/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlan(int id, int? maxCourses, decimal commissionPercent, decimal priceCredits, bool allowsTests)
    {
        var p = await _db.SubscriptionPlans.FindAsync(id);
        if (p is null) return NotFound();
        p.MaxCourses = maxCourses;                 // null = безлимит
        p.CommissionPercent = commissionPercent;
        p.PriceCredits = priceCredits;
        p.AllowsTests = allowsTests;
        await _db.SaveChangesAsync();
        TempData["Toast"] = $"План {p.Code} обновлён.";
        return RedirectToAction(nameof(Plans));
    }

    // ---------- SUBSCRIPTIONS ----------

    [HttpGet("subscriptions")]
    public async Task<IActionResult> Subscriptions()
    {
        var subs = await _db.InstructorSubscriptions
            .Include(s => s.Instructor)
            .Include(s => s.Plan)
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync();
        return View(subs);
    }

    // ---------- LOGS ----------

    [HttpGet("logs")]
    public async Task<IActionResult> Logs()
    {
        ViewData["MongoEnabled"] = _mongo.IsEnabled;
        var vm = new LogsVm
        {
            HttpLogs    = await _mongo.GetRecentHttpAsync(100),
            UserActions = await _mongo.GetRecentActionsAsync(100)
        };
        return View(vm);
    }

    // ---------- FINANCE ----------

    [HttpGet("finance")]
    public async Task<IActionResult> Finance(int? days = 30)
    {
        var window = days ?? 30;
        var since = DateTime.UtcNow.AddDays(-window);

        var txs = await _db.Transactions
            .Include(t => t.User)
            .Include(t => t.RelatedCourse)
            .OrderByDescending(t => t.CreatedAt)
            .Take(500)
            .ToListAsync();

        // Доход платформы — модуль PlatformCommission
        var commissionAll = await _db.Transactions
            .Where(t => t.Type == TransactionType.PlatformCommission)
            .SumAsync(t => (decimal?)Math.Abs(t.Amount)) ?? 0;
        var commissionWindow = await _db.Transactions
            .Where(t => t.Type == TransactionType.PlatformCommission && t.CreatedAt >= since)
            .SumAsync(t => (decimal?)Math.Abs(t.Amount)) ?? 0;
        var subPaymentsAll = await _db.Transactions
            .Where(t => t.Type == TransactionType.SubscriptionPayment)
            .SumAsync(t => (decimal?)Math.Abs(t.Amount)) ?? 0;
        var coursePurchasesAll = await _db.Transactions
            .Where(t => t.Type == TransactionType.CoursePurchase)
            .SumAsync(t => (decimal?)Math.Abs(t.Amount)) ?? 0;
        var topUpsAll = await _db.Transactions
            .Where(t => t.Type == TransactionType.TopUp)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        // По инструкторам: суммарная выручка (CoursePurchase к ним приходит как +Amount на userId инструктора)
        var perInstructor = await _db.Transactions
            .Where(t => t.Type == TransactionType.CoursePurchase && t.Amount > 0)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
            .OrderByDescending(x => x.Total)
            .Take(10)
            .ToListAsync();

        var userMap = await _db.Users
            .Where(u => perInstructor.Select(x => x.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var instructorRows = perInstructor.Select(x => new FinanceInstructorRow
        {
            UserId = x.UserId,
            Name = userMap.GetValueOrDefault(x.UserId)?.FullName ?? "—",
            Email = userMap.GetValueOrDefault(x.UserId)?.Email ?? "",
            Revenue = x.Total,
            Sales = x.Count
        }).ToList();

        var vm = new FinanceVm
        {
            Days = window,
            CommissionAll = commissionAll,
            CommissionWindow = commissionWindow,
            SubPaymentsAll = subPaymentsAll,
            CoursePurchasesAll = coursePurchasesAll,
            TopUpsAll = topUpsAll,
            RecentTransactions = txs,
            TopInstructors = instructorRows
        };
        return View(vm);
    }

    // ---------- CERTIFICATES ----------

    [HttpGet("certificates")]
    public async Task<IActionResult> Certificates(string? q = null)
    {
        var query = _db.Certificates
            .Include(c => c.Student)
            .Include(c => c.Course)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c =>
                c.CertificateNumber.Contains(s) ||
                c.Student.Email!.Contains(s) ||
                c.Student.FullName!.Contains(s) ||
                c.Course.Title.Contains(s));
        }
        ViewData["Q"] = q;
        var list = await query.OrderByDescending(c => c.IssuedAt).Take(200).ToListAsync();
        return View(list);
    }

    [HttpPost("certificates/{id:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeCertificate(Guid id)
    {
        var cert = await _db.Certificates.FindAsync(id);
        if (cert is null) return NotFound();
        _db.Certificates.Remove(cert);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("admin.cert.revoke", "Certificate", id.ToString(), cert.CertificateNumber);
        TempData["Toast"] = $"Сертификат {cert.CertificateNumber} отозван.";
        return RedirectToAction(nameof(Certificates));
    }

    // ---------- REVIEWS (модерация) ----------

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews(string? filter = null)
    {
        var q = _db.Reviews
            .Include(r => r.Student)
            .Include(r => r.Course)
            .AsQueryable();
        if (filter == "pending") q = q.Where(r => !r.IsApproved);
        if (filter == "approved") q = q.Where(r => r.IsApproved);

        ViewData["Filter"] = filter;
        var list = await q.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync();
        return View(list);
    }

    [HttpPost("reviews/{id:guid}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleReview(Guid id)
    {
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        r.IsApproved = !r.IsApproved;
        await _db.SaveChangesAsync();
        await RecalcCourseAvgAsync(r.CourseId);
        TempData["Toast"] = r.IsApproved ? "Отзыв одобрен." : "Отзыв снят с публикации.";
        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost("reviews/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(Guid id)
    {
        var r = await _db.Reviews.FindAsync(id);
        if (r is null) return NotFound();
        var courseId = r.CourseId;
        _db.Reviews.Remove(r);
        await _db.SaveChangesAsync();
        await RecalcCourseAvgAsync(courseId);
        TempData["Toast"] = "Отзыв удалён.";
        return RedirectToAction(nameof(Reviews));
    }

    private async Task RecalcCourseAvgAsync(Guid courseId)
    {
        var approved = await _db.Reviews
            .Where(r => r.CourseId == courseId && r.IsApproved)
            .Select(r => r.Rating).ToListAsync();
        var course = await _db.Courses.FindAsync(courseId);
        if (course is null) return;
        course.AverageRating = approved.Count == 0 ? 0m : Math.Round((decimal)approved.Average(), 2);
        await _db.SaveChangesAsync();
    }

    [HttpPost("subscriptions/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSubscription(Guid id)
    {
        var sub = await _db.InstructorSubscriptions.FindAsync(id);
        if (sub is null) return NotFound();
        sub.IsActive = false;
        sub.ExpiresAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Подписка отменена.";
        return RedirectToAction(nameof(Subscriptions));
    }
}

// ====== view models ======

public record AdminDashboardVm
{
    public int UsersTotal { get; init; }
    public int CoursesTotal { get; init; }
    public int EnrollmentsTotal { get; init; }
    public int CertificatesTotal { get; init; }
    public List<ApplicationUser> LatestUsers { get; init; } = new();
    public List<Enrollment> LatestEnrollments { get; init; } = new();
}

public record CategoryRow(int Id, string Name, string Slug, int CourseCount);

public class LogsVm
{
    public List<HttpLogRecord> HttpLogs { get; init; } = new();
    public List<UserActionRecord> UserActions { get; init; } = new();
}

public class FinanceVm
{
    public int Days { get; init; }
    public decimal CommissionAll { get; init; }
    public decimal CommissionWindow { get; init; }
    public decimal SubPaymentsAll { get; init; }
    public decimal CoursePurchasesAll { get; init; }
    public decimal TopUpsAll { get; init; }
    public List<Transaction> RecentTransactions { get; init; } = new();
    public List<FinanceInstructorRow> TopInstructors { get; init; } = new();
}

public class FinanceInstructorRow
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public decimal Revenue { get; init; }
    public int Sales { get; init; }
}
