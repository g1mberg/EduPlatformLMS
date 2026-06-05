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

[Route("instructor")]
[Authorize(Roles = "Instructor,Admin")]
public class InstructorController : Controller
{
    private const int FreeMaxCourses = 3; // дефолт когда у инструктора нет активной подписки

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserActionLogger _audit;
    private readonly NotificationService _notifications;

    public InstructorController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        UserActionLogger audit, NotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
        _notifications = notifications;
    }

    [AllowAnonymous]
    [HttpGet("profile")]
    public IActionResult Profile() => View();

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var userId = GetUserId();

        var courses = await _db.Courses
            .Where(c => c.InstructorId == userId)
            .ToListAsync();

        var courseIds = courses.Select(c => c.Id).ToList();

        // Доход = сумма поступлений CoursePurchase инструктору минус комиссия (PlatformCommission)
        var purchaseTxs = await _db.Transactions
            .Where(t => t.RelatedCourseId != null && courseIds.Contains(t.RelatedCourseId!.Value)
                        && (t.Type == TransactionType.CoursePurchase || t.Type == TransactionType.PlatformCommission))
            .ToListAsync();

        var revenuePerCourse = purchaseTxs
            .Where(t => t.Type == TransactionType.CoursePurchase && t.UserId == userId)
            .GroupBy(t => t.RelatedCourseId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var commissionTotal = purchaseTxs
            .Where(t => t.Type == TransactionType.PlatformCommission)
            .Sum(t => Math.Abs(t.Amount));

        var totalRevenue = revenuePerCourse.Values.Sum();

        var topCourses = courses
            .OrderByDescending(c => c.StudentsCount)
            .Take(5)
            .Select(c => new DashboardCourseRow
            {
                Id = c.Id,
                Slug = c.Slug,
                Title = c.Title,
                Students = c.StudentsCount,
                AverageRating = c.AverageRating,
                Revenue = revenuePerCourse.GetValueOrDefault(c.Id),
                IsPublished = c.IsPublished
            })
            .ToList();

        var recent = await _db.Enrollments
            .Where(e => courseIds.Contains(e.CourseId))
            .OrderByDescending(e => e.EnrolledAt)
            .Take(10)
            .Select(e => new DashboardEnrollment
            {
                StudentName = e.Student.FullName ?? e.Student.UserName ?? "",
                CourseTitle = e.Course.Title,
                CourseSlug = e.Course.Slug,
                At = e.EnrolledAt,
                ProgressPercent = e.ProgressPercent
            })
            .ToListAsync();

        var since = DateTime.UtcNow.AddMonths(-5);
        since = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthly = await _db.Enrollments
            .Where(e => courseIds.Contains(e.CourseId) && e.EnrolledAt >= since)
            .GroupBy(e => new { e.EnrolledAt.Year, e.EnrolledAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync();

        // Заполним 6 месяцев подряд (включая нули)
        var monthlyFull = new List<DashboardMonthPoint>();
        for (int i = 0; i < 6; i++)
        {
            var d = since.AddMonths(i);
            var found = monthly.FirstOrDefault(m => m.Year == d.Year && m.Month == d.Month);
            monthlyFull.Add(new DashboardMonthPoint
            {
                Year = d.Year,
                Month = d.Month,
                Count = found?.Count ?? 0
            });
        }

        var activeSub = await _db.InstructorSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.InstructorId == userId && s.IsActive &&
                        (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        var user = await _userManager.FindByIdAsync(userId.ToString());

        var avgRating = courses.Count == 0 ? 0m : Math.Round(courses.Average(c => c.AverageRating), 2);

        var vm = new InstructorDashboardViewModel
        {
            CoursesCount = courses.Count,
            PublishedCount = courses.Count(c => c.IsPublished),
            TotalStudents = courses.Sum(c => c.StudentsCount),
            AverageRating = avgRating,
            TotalRevenue = totalRevenue,
            PlatformCommissionTotal = commissionTotal,
            Credits = user?.Credits ?? 0,
            ActivePlan = activeSub?.Plan.Code,
            PlanExpires = activeSub?.ExpiresAt,
            TopCourses = topCourses,
            RecentEnrollments = recent,
            MonthlyEnrollments = monthlyFull
        };
        return View(vm);
    }

    [AllowAnonymous]
    [HttpGet("subscription")]
    public async Task<IActionResult> Subscription()
    {
        var plans = await _db.SubscriptionPlans.OrderBy(p => p.Id).ToListAsync();
        var isAuth = User?.Identity?.IsAuthenticated == true;

        decimal credits = 0;
        SubscriptionPlanCode? activeCode = null;
        DateTime? activeExpires = null;

        if (isAuth)
        {
            var userId = GetUserId();
            var user = await _userManager.FindByIdAsync(userId.ToString());
            credits = user?.Credits ?? 0m;

            var active = await _db.InstructorSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.InstructorId == userId && s.IsActive &&
                            (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
            activeCode = active?.Plan.Code;
            activeExpires = active?.ExpiresAt;
        }

        var vm = new SubscriptionPageViewModel
        {
            Plans = plans,
            IsAuthenticated = isAuth,
            Credits = credits,
            ActivePlanCode = activeCode,
            ActiveExpiresAt = activeExpires
        };
        return View("/Views/Subscription/Index.cshtml", vm);
    }

    [HttpPost("subscription/buy/{planId:int}")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuySubscription(int planId)
    {
        if (User?.Identity?.IsAuthenticated != true)
            return Redirect("/account/login?returnUrl=/instructor/subscription");

        var plan = await _db.SubscriptionPlans.FindAsync(planId);
        if (plan is null) return NotFound();

        var userId = GetUserId();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Challenge();

        // Проверка баланса
        if (user.Credits < plan.PriceCredits)
        {
            TempData["Toast"] = $"Недостаточно кредитов. Нужно {plan.PriceCredits:N0}, у вас {user.Credits:N0}.";
            return RedirectToAction(nameof(Subscription));
        }

        // Деактивируем предыдущие активные подписки
        var oldActive = await _db.InstructorSubscriptions
            .Where(s => s.InstructorId == userId && s.IsActive)
            .ToListAsync();
        foreach (var s in oldActive)
        {
            s.IsActive = false;
            s.ExpiresAt = DateTime.UtcNow;
        }

        // Списываем кредиты + транзакция
        if (plan.PriceCredits > 0)
        {
            user.Credits -= plan.PriceCredits;
            await _userManager.UpdateAsync(user);

            _db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Amount = -plan.PriceCredits,
                Type = TransactionType.SubscriptionPayment,
                CreatedAt = DateTime.UtcNow,
                Description = $"Подписка {plan.Code} на 30 дней"
            });
        }

        // Активируем подписку
        var sub = new InstructorSubscription
        {
            Id = Guid.NewGuid(),
            InstructorId = userId,
            PlanId = plan.Id,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsActive = true
        };
        _db.InstructorSubscriptions.Add(sub);

        // Добавим роль Instructor если у юзера её ещё нет
        if (!await _userManager.IsInRoleAsync(user, "Instructor"))
            await _userManager.AddToRoleAsync(user, "Instructor");

        await _db.SaveChangesAsync();
        await _notifications.NotifyAsync(userId,
            "Подписка активирована",
            $"План {plan.Code} активирован до {sub.ExpiresAt:dd.MM.yyyy}.");
        await _audit.LogAsync("subscription.buy", "SubscriptionPlan", plan.Id.ToString(), $"{plan.Code}, -{plan.PriceCredits} cr");

        TempData["Toast"] = $"План {plan.Code} активирован до {sub.ExpiresAt:dd.MM.yyyy}.";
        return RedirectToAction(nameof(Subscription));
    }

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

    // ---------- Wizard (5 шагов) ----------

    [HttpGet("courses/wizard")]
    public async Task<IActionResult> Wizard()
    {
        var userId = GetUserId();
        var (used, limit) = await GetCourseLimitAsync(userId);
        if (limit is not null && used >= limit)
        {
            TempData["LimitError"] = $"Достигнут лимит курсов ({used}/{limit}). Обновите план.";
            return RedirectToAction(nameof(Courses));
        }

        var vm = new CourseWizardViewModel
        {
            AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync(),
            AllowsTests = await CurrentPlanAllowsTestsAsync(userId),
            Sections = new List<WizardSection>
            {
                new() { Title = "Введение", Lessons = new List<WizardLesson> { new() { Title = "Добро пожаловать" } } }
            }
        };
        return View(vm);
    }

    [HttpPost("courses/wizard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Wizard(CourseWizardViewModel vm)
    {
        var userId = GetUserId();

        var (used, limit) = await GetCourseLimitAsync(userId);
        if (limit is not null && used >= limit)
        {
            ModelState.AddModelError(string.Empty, $"Достигнут лимит курсов ({used}/{limit}).");
        }

        // Slug-валидация (как в обычной форме)
        if (string.IsNullOrWhiteSpace(vm.Slug))
            vm.Slug = SlugHelper.Generate(vm.Title);
        else
            vm.Slug = vm.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(vm.Slug))
            ModelState.AddModelError(nameof(vm.Slug), "Не удалось сгенерировать slug, задайте вручную");
        else if (await _db.Courses.AnyAsync(c => c.Slug == vm.Slug))
            ModelState.AddModelError(nameof(vm.Slug), "Такой slug уже занят");

        if (!await _db.Categories.AnyAsync(c => c.Id == vm.CategoryId))
            ModelState.AddModelError(nameof(vm.CategoryId), "Выберите категорию");

        // Нормализуем структуру курса
        vm.Sections ??= new List<WizardSection>();
        foreach (var s in vm.Sections)
        {
            s.Title = (s.Title ?? "").Trim();
            s.Lessons ??= new List<WizardLesson>();
            foreach (var l in s.Lessons)
            {
                l.Title = (l.Title ?? "").Trim();
                l.VideoUrl = string.IsNullOrWhiteSpace(l.VideoUrl) ? null : l.VideoUrl.Trim();
                l.TextContent = string.IsNullOrWhiteSpace(l.TextContent) ? null : l.TextContent.Trim();
            }
            s.Lessons = s.Lessons.Where(l => !string.IsNullOrEmpty(l.Title)).ToList();
        }
        vm.Sections = vm.Sections.Where(s => !string.IsNullOrEmpty(s.Title) && s.Lessons.Count > 0).ToList();
        if (vm.Sections.Count == 0)
            ModelState.AddModelError(string.Empty, "Курс должен содержать хотя бы один раздел с уроком");

        // Финальный тест
        var allowsTests = await CurrentPlanAllowsTestsAsync(userId);
        if (vm.IncludeFinalTest && !allowsTests)
        {
            ModelState.AddModelError(string.Empty, "Финальный тест доступен на планах Basic/Pro. Уберите галочку или смените план.");
            vm.IncludeFinalTest = false;
        }

        if (vm.IncludeFinalTest)
        {
            vm.FinalTestQuestions ??= new List<WizardTestQuestion>();
            foreach (var q in vm.FinalTestQuestions)
            {
                q.Text = (q.Text ?? "").Trim();
                q.Options ??= new List<WizardTestOption>();
                foreach (var o in q.Options) o.Text = (o.Text ?? "").Trim();
                q.Options = q.Options.Where(o => !string.IsNullOrEmpty(o.Text)).ToList();
            }
            vm.FinalTestQuestions = vm.FinalTestQuestions.Where(q => !string.IsNullOrEmpty(q.Text)).ToList();

            if (vm.FinalTestQuestions.Count == 0)
                ModelState.AddModelError(string.Empty, "Финальный тест требует хотя бы один вопрос");
            for (int i = 0; i < vm.FinalTestQuestions.Count; i++)
            {
                var q = vm.FinalTestQuestions[i];
                if (q.Options.Count < 2)
                    ModelState.AddModelError(string.Empty, $"Вопрос {i + 1} финального теста: нужно минимум 2 варианта");
                else if (!q.Options.Any(o => o.IsCorrect))
                    ModelState.AddModelError(string.Empty, $"Вопрос {i + 1}: отметьте правильный вариант");
                else if (q.Type == QuestionType.SingleChoice && q.Options.Count(o => o.IsCorrect) > 1)
                    ModelState.AddModelError(string.Empty, $"Вопрос {i + 1}: при одиночном выборе только один правильный");
            }
        }

        vm.AllowsTests = allowsTests;
        vm.AvailableCategories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();

        if (!ModelState.IsValid)
            return View(vm);

        // Сохранение в одной транзакции
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

        Lesson? lastLesson = null;
        for (int si = 0; si < vm.Sections.Count; si++)
        {
            var s = vm.Sections[si];
            var section = new Section
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                Title = s.Title,
                OrderIndex = si + 1
            };
            _db.Sections.Add(section);

            for (int li = 0; li < s.Lessons.Count; li++)
            {
                var l = s.Lessons[li];
                var lesson = new Lesson
                {
                    Id = Guid.NewGuid(),
                    SectionId = section.Id,
                    Title = l.Title,
                    VideoUrl = l.VideoUrl,
                    TextContent = l.TextContent,
                    OrderIndex = li + 1
                };
                _db.Lessons.Add(lesson);
                lastLesson = lesson;
            }
        }

        if (vm.IncludeFinalTest && lastLesson is not null)
        {
            var test = new Test
            {
                Id = Guid.NewGuid(),
                LessonId = lastLesson.Id,
                Title = string.IsNullOrWhiteSpace(vm.FinalTestTitle) ? $"Финальный тест — {course.Title}" : vm.FinalTestTitle!.Trim(),
                PassingScore = vm.FinalTestPassingScore
            };
            _db.Tests.Add(test);

            for (int qi = 0; qi < vm.FinalTestQuestions.Count; qi++)
            {
                var qi_ = vm.FinalTestQuestions[qi];
                var q = new Question
                {
                    Id = Guid.NewGuid(),
                    TestId = test.Id,
                    Text = qi_.Text,
                    Type = qi_.Type,
                    OrderIndex = qi + 1
                };
                _db.Questions.Add(q);
                foreach (var oi in qi_.Options)
                {
                    _db.AnswerOptions.Add(new AnswerOption
                    {
                        Id = Guid.NewGuid(),
                        QuestionId = q.Id,
                        Text = oi.Text,
                        IsCorrect = oi.IsCorrect
                    });
                }
            }
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("course.wizard.create", "Course", course.Id.ToString(),
            $"{vm.Sections.Count} sec, {vm.Sections.Sum(s => s.Lessons.Count)} les, test={vm.IncludeFinalTest}");

        TempData["Toast"] = $"Курс «{course.Title}» создан мастером ({vm.Sections.Sum(s => s.Lessons.Count)} уроков" +
                            (vm.IncludeFinalTest ? ", с финальным тестом" : "") + ")";
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

    // ---------- Аналитика по курсу ----------

    [HttpGet("courses/{id:guid}/analytics")]
    public async Task<IActionResult> CourseAnalytics(Guid id)
    {
        var userId = GetUserId();
        var course = await _db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons).ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        var enrollments = await _db.Enrollments
            .Include(e => e.Student)
            .Where(e => e.CourseId == id)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync();

        var revenue = await _db.Transactions
            .Where(t => t.Type == TransactionType.CoursePurchase && t.RelatedCourseId == id && t.UserId == userId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var commission = await _db.Transactions
            .Where(t => t.Type == TransactionType.PlatformCommission && t.RelatedCourseId == id)
            .SumAsync(t => (decimal?)Math.Abs(t.Amount)) ?? 0;

        var certificates = await _db.Certificates.CountAsync(c => c.CourseId == id);

        var reviews = await _db.Reviews
            .Include(r => r.Student)
            .Where(r => r.CourseId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        // Среднее по тестам курса
        var testIds = course.Sections.SelectMany(s => s.Lessons).Where(l => l.Test != null).Select(l => l.Test!.Id).ToList();
        decimal avgTestScore = 0;
        int testAttemptsCount = 0;
        if (testIds.Count > 0)
        {
            var attempts = await _db.TestAttempts
                .Where(a => testIds.Contains(a.TestId))
                .ToListAsync();
            testAttemptsCount = attempts.Count;
            avgTestScore = attempts.Count == 0 ? 0 : Math.Round((decimal)attempts.Average(a => a.Score), 1);
        }

        var monthlyEnroll = enrollments
            .Where(e => e.EnrolledAt >= DateTime.UtcNow.AddMonths(-5))
            .GroupBy(e => new { e.EnrolledAt.Year, e.EnrolledAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToList();
        var since = DateTime.UtcNow.AddMonths(-5);
        since = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthly = Enumerable.Range(0, 6).Select(i =>
        {
            var d = since.AddMonths(i);
            return new CourseAnalyticsMonth
            {
                Label = $"{d.Year}-{d.Month:D2}",
                Count = monthlyEnroll.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Count ?? 0
            };
        }).ToList();

        var avgProgress = enrollments.Count == 0 ? 0m : Math.Round(enrollments.Average(e => e.ProgressPercent), 1);
        var completedCount = enrollments.Count(e => e.CompletedAt.HasValue);

        var vm = new CourseAnalyticsViewModel
        {
            Course = course,
            EnrollmentsCount = enrollments.Count,
            CompletedCount = completedCount,
            AvgProgress = avgProgress,
            Certificates = certificates,
            Revenue = revenue,
            Commission = commission,
            TestAttempts = testAttemptsCount,
            AvgTestScore = avgTestScore,
            Reviews = reviews,
            RecentEnrollments = enrollments.Take(15).ToList(),
            MonthlyEnrollments = monthly,
            LessonsCount = course.Sections.Sum(s => s.Lessons.Count)
        };
        return View(vm);
    }

    // ---------- Тесты к урокам ----------

    [HttpGet("courses/{id:guid}/tests")]
    public async Task<IActionResult> Tests(Guid id)
    {
        var userId = GetUserId();
        var course = await _db.Courses
            .Include(c => c.Sections.OrderBy(s => s.OrderIndex))
                .ThenInclude(s => s.Lessons.OrderBy(l => l.OrderIndex))
                    .ThenInclude(l => l.Test)
                        .ThenInclude(t => t!.Questions)
            .FirstOrDefaultAsync(c => c.Id == id && c.InstructorId == userId);
        if (course is null) return NotFound();

        var rows = course.Sections
            .SelectMany(s => s.Lessons.Select(l => new TestLessonRow
            {
                LessonId = l.Id,
                SectionTitle = s.Title,
                LessonTitle = l.Title,
                TestId = l.Test?.Id,
                TestTitle = l.Test?.Title,
                PassingScore = l.Test?.PassingScore,
                QuestionsCount = l.Test?.Questions.Count ?? 0
            }))
            .ToList();

        var vm = new TestListViewModel
        {
            Course = course,
            Rows = rows,
            AllowsTests = await CurrentPlanAllowsTestsAsync(userId)
        };
        return View("Tests", vm);
    }

    [HttpGet("lessons/{lessonId:guid}/test/edit")]
    public async Task<IActionResult> EditTest(Guid lessonId)
    {
        var userId = GetUserId();
        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Test!).ThenInclude(t => t.Questions.OrderBy(q => q.OrderIndex)).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null || lesson.Section.Course.InstructorId != userId) return NotFound();

        if (!await CurrentPlanAllowsTestsAsync(userId))
        {
            TempData["Toast"] = "Для работы с тестами нужен план Basic или Pro.";
            return RedirectToAction(nameof(Tests), new { id = lesson.Section.CourseId });
        }

        var vm = new TestFormViewModel
        {
            CourseId = lesson.Section.CourseId,
            CourseTitle = lesson.Section.Course.Title,
            LessonId = lesson.Id,
            LessonTitle = lesson.Title,
            TestId = lesson.Test?.Id,
            Title = lesson.Test?.Title ?? $"Тест: {lesson.Title}",
            PassingScore = lesson.Test?.PassingScore ?? 70,
            Questions = lesson.Test?.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => new TestQuestionInput
                {
                    Id = q.Id,
                    Text = q.Text,
                    Type = q.Type,
                    Options = q.Options.Select(o => new TestOptionInput
                    {
                        Id = o.Id,
                        Text = o.Text,
                        IsCorrect = o.IsCorrect
                    }).ToList()
                }).ToList() ?? new List<TestQuestionInput>()
        };

        // Если новый тест — добавим один пустой вопрос с двумя опциями для удобства
        if (vm.Questions.Count == 0)
        {
            vm.Questions.Add(new TestQuestionInput
            {
                Text = "",
                Type = QuestionType.SingleChoice,
                Options = new List<TestOptionInput>
                {
                    new() { Text = "", IsCorrect = true },
                    new() { Text = "", IsCorrect = false }
                }
            });
        }

        return View("TestForm", vm);
    }

    [HttpPost("lessons/{lessonId:guid}/test/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTest(Guid lessonId, TestFormViewModel vm)
    {
        var userId = GetUserId();
        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Test!).ThenInclude(t => t.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null || lesson.Section.Course.InstructorId != userId) return NotFound();

        if (!await CurrentPlanAllowsTestsAsync(userId))
        {
            TempData["Toast"] = "Для работы с тестами нужен план Basic или Pro.";
            return RedirectToAction(nameof(Tests), new { id = lesson.Section.CourseId });
        }

        vm.CourseId = lesson.Section.CourseId;
        vm.CourseTitle = lesson.Section.Course.Title;
        vm.LessonId = lesson.Id;
        vm.LessonTitle = lesson.Title;

        // Нормализация + валидация
        vm.Questions ??= new List<TestQuestionInput>();
        foreach (var q in vm.Questions)
        {
            q.Text = (q.Text ?? "").Trim();
            q.Options ??= new List<TestOptionInput>();
            foreach (var o in q.Options) o.Text = (o.Text ?? "").Trim();
            q.Options = q.Options.Where(o => !string.IsNullOrEmpty(o.Text)).ToList();
        }
        vm.Questions = vm.Questions.Where(q => !string.IsNullOrEmpty(q.Text)).ToList();

        if (vm.Questions.Count == 0)
            ModelState.AddModelError(string.Empty, "Добавьте хотя бы один вопрос");

        for (int i = 0; i < vm.Questions.Count; i++)
        {
            var q = vm.Questions[i];
            if (q.Type == QuestionType.Text)
            {
                // Для текстового вопроса ожидаем ровно один Option с непустым Text — это эталонный ответ
                var correct = q.Options.FirstOrDefault();
                if (correct is null || string.IsNullOrWhiteSpace(correct.Text))
                    ModelState.AddModelError($"Questions[{i}].Options", $"Вопрос {i + 1}: укажите эталонный ответ");
                else
                {
                    q.Options = new List<TestOptionInput> { new() { Text = correct.Text.Trim(), IsCorrect = true } };
                }
                continue;
            }
            if (q.Options.Count < 2)
                ModelState.AddModelError($"Questions[{i}].Options", $"Вопрос {i + 1}: нужно минимум 2 варианта");
            else if (!q.Options.Any(o => o.IsCorrect))
                ModelState.AddModelError($"Questions[{i}].Options", $"Вопрос {i + 1}: отметьте хотя бы один правильный вариант");
            else if (q.Type == QuestionType.SingleChoice && q.Options.Count(o => o.IsCorrect) > 1)
                ModelState.AddModelError($"Questions[{i}].Options", $"Вопрос {i + 1}: при одиночном выборе можно отметить только один правильный вариант");
        }

        if (!ModelState.IsValid)
            return View("TestForm", vm);

        // Сохранение: проще всего пересоздать вопросы целиком (старые ответы студентов не привязаны к ним напрямую через cascade — мы используем Restrict).
        var test = lesson.Test;
        if (test is null)
        {
            test = new Test
            {
                Id = Guid.NewGuid(),
                LessonId = lesson.Id,
                Title = vm.Title.Trim(),
                PassingScore = vm.PassingScore
            };
            _db.Tests.Add(test);
        }
        else
        {
            test.Title = vm.Title.Trim();
            test.PassingScore = vm.PassingScore;

            // Удаляем старые ответы студентов, чтобы можно было поменять структуру вопросов
            var oldQids = test.Questions.Select(q => q.Id).ToList();
            if (oldQids.Count > 0)
            {
                var oldAnswers = await _db.StudentAnswers.Where(a => oldQids.Contains(a.QuestionId)).ToListAsync();
                if (oldAnswers.Count > 0) _db.StudentAnswers.RemoveRange(oldAnswers);
            }
            var oldAttempts = await _db.TestAttempts.Where(a => a.TestId == test.Id).ToListAsync();
            if (oldAttempts.Count > 0) _db.TestAttempts.RemoveRange(oldAttempts);

            foreach (var q in test.Questions.ToList())
            {
                _db.AnswerOptions.RemoveRange(q.Options);
                _db.Questions.Remove(q);
            }
        }

        for (int i = 0; i < vm.Questions.Count; i++)
        {
            var qi = vm.Questions[i];
            var q = new Question
            {
                Id = Guid.NewGuid(),
                TestId = test.Id,
                Text = qi.Text,
                Type = qi.Type,
                OrderIndex = i + 1
            };
            _db.Questions.Add(q);

            foreach (var oi in qi.Options)
            {
                _db.AnswerOptions.Add(new AnswerOption
                {
                    Id = Guid.NewGuid(),
                    QuestionId = q.Id,
                    Text = oi.Text,
                    IsCorrect = oi.IsCorrect
                });
            }
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("test.save", "Test", test.Id.ToString(), $"{vm.Questions.Count} q.");

        TempData["Toast"] = $"Тест «{test.Title}» сохранён ({vm.Questions.Count} вопр.)";
        return RedirectToAction(nameof(Tests), new { id = lesson.Section.CourseId });
    }

    [HttpPost("lessons/{lessonId:guid}/test/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTest(Guid lessonId)
    {
        var userId = GetUserId();
        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Test!).ThenInclude(t => t.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null || lesson.Section.Course.InstructorId != userId) return NotFound();
        if (lesson.Test is null) return RedirectToAction(nameof(Tests), new { id = lesson.Section.CourseId });

        var test = lesson.Test;
        var qids = test.Questions.Select(q => q.Id).ToList();
        if (qids.Count > 0)
        {
            var oldAnswers = await _db.StudentAnswers.Where(a => qids.Contains(a.QuestionId)).ToListAsync();
            if (oldAnswers.Count > 0) _db.StudentAnswers.RemoveRange(oldAnswers);
        }
        var oldAttempts = await _db.TestAttempts.Where(a => a.TestId == test.Id).ToListAsync();
        if (oldAttempts.Count > 0) _db.TestAttempts.RemoveRange(oldAttempts);

        foreach (var q in test.Questions.ToList())
        {
            _db.AnswerOptions.RemoveRange(q.Options);
            _db.Questions.Remove(q);
        }
        _db.Tests.Remove(test);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("test.delete", "Test", test.Id.ToString(), test.Title);

        TempData["Toast"] = "Тест удалён";
        return RedirectToAction(nameof(Tests), new { id = lesson.Section.CourseId });
    }

    private async Task<bool> CurrentPlanAllowsTestsAsync(Guid userId)
    {
        var plan = await _db.InstructorSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.InstructorId == userId && s.IsActive &&
                        (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Plan)
            .FirstOrDefaultAsync();
        // Если активной подписки нет — считаем Free, тесты запрещены.
        return plan?.AllowsTests ?? false;
    }

    // ---------- Прикреплённые файлы к уроку ----------

    [HttpGet("lessons/{lessonId:guid}/attachments")]
    public async Task<IActionResult> Attachments(Guid lessonId)
    {
        var userId = GetUserId();
        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null || lesson.Section.Course.InstructorId != userId) return NotFound();

        ViewData["Lesson"] = lesson;
        return View(lesson.Attachments.ToList());
    }

    [HttpPost("lessons/{lessonId:guid}/attachments/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)] // 50 MB
    public async Task<IActionResult> UploadAttachment(Guid lessonId, IFormFile? file)
    {
        var userId = GetUserId();
        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null || lesson.Section.Course.InstructorId != userId) return NotFound();

        if (file is null || file.Length == 0)
        {
            TempData["Toast"] = "Выберите файл.";
            return RedirectToAction(nameof(Attachments), new { lessonId });
        }
        if (file.Length > 50_000_000)
        {
            TempData["Toast"] = "Максимум 50 МБ.";
            return RedirectToAction(nameof(Attachments), new { lessonId });
        }

        var webRoot = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var dir = Path.Combine(webRoot, "uploads", "lessons", lessonId.ToString("N"));
        Directory.CreateDirectory(dir);

        var safeName = SanitizeFileName(Path.GetFileName(file.FileName));
        if (string.IsNullOrEmpty(safeName)) safeName = "file";
        var unique = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{safeName}";
        var fullPath = Path.Combine(dir, unique);
        using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        var url = $"/uploads/lessons/{lessonId:N}/{Uri.EscapeDataString(unique)}";
        _db.LessonAttachments.Add(new LessonAttachment
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            FileUrl = url,
            FileName = safeName
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("lesson.attachment.upload", "Lesson", lessonId.ToString(), safeName);

        TempData["Toast"] = $"Файл «{safeName}» загружен.";
        return RedirectToAction(nameof(Attachments), new { lessonId });
    }

    [HttpPost("attachments/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(Guid id)
    {
        var userId = GetUserId();
        var att = await _db.LessonAttachments
            .Include(a => a.Lesson).ThenInclude(l => l.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (att is null || att.Lesson.Section.Course.InstructorId != userId) return NotFound();

        var lessonId = att.LessonId;
        var webRoot = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        try
        {
            // FileUrl у нас вида /uploads/lessons/<id>/<file>
            var rel = att.FileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var full = Path.Combine(webRoot, rel);
            if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        }
        catch { /* не критично — запись из БД всё равно удалим */ }

        _db.LessonAttachments.Remove(att);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Файл удалён.";
        return RedirectToAction(nameof(Attachments), new { lessonId });
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        s = s.Replace(' ', '_');
        if (s.Length > 200) s = s[^200..];
        return s;
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
