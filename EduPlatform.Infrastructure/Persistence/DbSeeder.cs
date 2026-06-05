using EduPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduPlatform.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedCategoriesAsync(db);
        await EnsureAdminAsync(userManager);
        var instructor = await EnsureDemoInstructorAsync(userManager);
        await SeedCoursesAsync(db, instructor.Id);
        await SeedLessonsAsync(db);
        await SeedTestsAsync(db);
        await SeedDemoStudentsAsync(userManager);
        await SeedReviewsAndPurchasesAsync(db, userManager);
    }

    private static async Task SeedDemoStudentsAsync(UserManager<ApplicationUser> userManager)
    {
        var students = new[]
        {
            ("alice@example.com", "Alice Hopper", 5000m),
            ("bob@example.com", "Bob Newton", 3000m),
            ("charlie@example.com", "Charlie Knight", 2000m)
        };
        foreach (var (email, name, credits) in students)
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;
            var u = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = name,
                EmailConfirmed = true,
                Credits = credits,
                AvatarUrl = "/assets/img/user/user-29.jpg"
            };
            var r = await userManager.CreateAsync(u, "DemoStudent!1");
            if (r.Succeeded)
                await userManager.AddToRoleAsync(u, "Student");
        }
    }

    private static async Task SeedReviewsAndPurchasesAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        // Если уже есть отзывы — не дублируем
        if (await db.Reviews.AnyAsync()) return;

        var students = await db.Users
            .Where(u => u.Email != null && (u.Email == "alice@example.com" || u.Email == "bob@example.com" || u.Email == "charlie@example.com"))
            .ToListAsync();
        if (students.Count == 0) return;

        var courses = await db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons)
            .OrderBy(c => c.CreatedAt)
            .Take(6)
            .ToListAsync();

        var rng = new Random(42); // детерминированный сид
        var sampleTexts = new[]
        {
            "Отличный курс, материал подан понятно. Рекомендую!",
            "Очень полезно для новичков. Жду продолжения.",
            "Достойный курс, но местами не хватает практики.",
            "Прошёл с удовольствием, всё структурировано.",
            "Хороший набор тем, инструктор отвечает на вопросы.",
            "Полезный курс для портфолио и реальных задач.",
            null, null  // часть отзывов без текста
        };

        var commissionPct = 30m; // у demo-инструктора нет подписки → Free

        foreach (var student in students)
        {
            // Каждый студент записывается на 2-3 курса
            var pickedCourses = courses.OrderBy(_ => rng.Next()).Take(rng.Next(2, 4)).ToList();
            foreach (var course in pickedCourses)
            {
                // Запись
                var enrollment = new Enrollment
                {
                    Id = Guid.NewGuid(),
                    StudentId = student.Id,
                    CourseId = course.Id,
                    EnrolledAt = DateTime.UtcNow.AddDays(-rng.Next(1, 90)),
                    ProgressPercent = rng.Next(20, 101)
                };
                db.Enrollments.Add(enrollment);
                course.StudentsCount += 1;

                // Покупка платного курса
                if (course.Price > 0 && student.Credits >= course.Price)
                {
                    var commission = Math.Round(course.Price * commissionPct / 100m, 2);
                    var net = course.Price - commission;
                    student.Credits -= course.Price;

                    db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        UserId = course.InstructorId,
                        Amount = net,
                        Type = TransactionType.CoursePurchase,
                        RelatedCourseId = course.Id,
                        Description = $"Покупка курса «{course.Title}» (студент {student.FullName})",
                        CreatedAt = enrollment.EnrolledAt
                    });
                    db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        UserId = course.InstructorId,
                        Amount = -commission,
                        Type = TransactionType.PlatformCommission,
                        RelatedCourseId = course.Id,
                        Description = $"Комиссия {commissionPct:0.#}% с курса «{course.Title}»",
                        CreatedAt = enrollment.EnrolledAt
                    });

                    // Зачисляем инструктору
                    var instructorUser = await db.Users.FirstOrDefaultAsync(u => u.Id == course.InstructorId);
                    if (instructorUser is not null) instructorUser.Credits += net;
                }

                // Отзыв (с вероятностью 70%)
                if (rng.NextDouble() < 0.7)
                {
                    var rating = rng.Next(3, 6); // 3..5
                    var text = sampleTexts[rng.Next(sampleTexts.Length)];
                    db.Reviews.Add(new Review
                    {
                        Id = Guid.NewGuid(),
                        CourseId = course.Id,
                        StudentId = student.Id,
                        Rating = rating,
                        Text = text,
                        IsApproved = true,
                        CreatedAt = enrollment.EnrolledAt.AddDays(rng.Next(1, 7))
                    });
                }
            }
        }

        // Пересчёт AverageRating
        await db.SaveChangesAsync();
        var coursesWithReviews = await db.Courses.Where(c => c.Reviews.Any(r => r.IsApproved)).ToListAsync();
        foreach (var c in coursesWithReviews)
        {
            var ratings = await db.Reviews.Where(r => r.CourseId == c.Id && r.IsApproved).Select(r => r.Rating).ToListAsync();
            c.AverageRating = ratings.Count == 0 ? 0m : Math.Round((decimal)ratings.Average(), 2);
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureAdminAsync(UserManager<ApplicationUser> userManager)
    {
        const string email = "admin@eduplatform.local";
        const string password = "Admin!Pass1";
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            await ForceResetPasswordAsync(userManager, user, password);
            return;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Администратор",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new Exception("Не удалось создать админа: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, "Admin");
    }

    private static async Task ForceResetPasswordAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string newPassword)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        await userManager.ResetPasswordAsync(user, token, newPassword);
        if (user.LockoutEnd.HasValue)
        {
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
        }
    }

    private static async Task SeedTestsAsync(ApplicationDbContext db)
    {
        if (await db.Tests.AnyAsync()) return;

        // Берём первые 2 курса, у которых есть секции, и в последнюю секцию добавляем финальный урок-тест
        var coursesWithSections = await db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons)
            .Where(c => c.Sections.Any())
            .OrderBy(c => c.CreatedAt)
            .Take(2)
            .ToListAsync();

        foreach (var course in coursesWithSections)
        {
            var lastSection = course.Sections.OrderBy(s => s.OrderIndex).Last();
            var nextOrder = lastSection.Lessons.Max(l => l.OrderIndex) + 1;

            var testLesson = new Lesson
            {
                Id = Guid.NewGuid(),
                SectionId = lastSection.Id,
                Title = $"Финальный тест — {course.Title}",
                TextContent = "Пройдите тест, чтобы завершить курс. Минимальный проходной балл — 70%.",
                OrderIndex = nextOrder
            };
            db.Lessons.Add(testLesson);

            var test = new Test
            {
                Id = Guid.NewGuid(),
                LessonId = testLesson.Id,
                Title = $"Итоговый тест: {course.Title}",
                PassingScore = 70
            };
            db.Tests.Add(test);

            for (var qi = 1; qi <= 3; qi++)
            {
                var q = new Question
                {
                    Id = Guid.NewGuid(),
                    TestId = test.Id,
                    Text = $"Вопрос {qi}: что вы узнали в модуле {qi} курса «{course.Title}»?",
                    Type = QuestionType.SingleChoice,
                    OrderIndex = qi
                };
                db.Questions.Add(q);

                for (var oi = 1; oi <= 4; oi++)
                {
                    db.AnswerOptions.Add(new AnswerOption
                    {
                        Id = Guid.NewGuid(),
                        QuestionId = q.Id,
                        Text = $"Вариант {oi}",
                        IsCorrect = oi == 1   // правильный — всегда первый (для seed-данных)
                    });
                }
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedLessonsAsync(ApplicationDbContext db)
    {
        if (await db.Sections.AnyAsync()) return;

        // Берём первые 5 опубликованных курсов, для каждого создаём 3 секции по 3 урока
        var courses = await db.Courses
            .Where(c => c.IsPublished)
            .OrderBy(c => c.CreatedAt)
            .Take(5)
            .ToListAsync();

        foreach (var course in courses)
        {
            for (var s = 1; s <= 3; s++)
            {
                var section = new Section
                {
                    Id = Guid.NewGuid(),
                    CourseId = course.Id,
                    Title = $"Модуль {s}",
                    OrderIndex = s
                };
                db.Sections.Add(section);

                for (var l = 1; l <= 3; l++)
                {
                    db.Lessons.Add(new Lesson
                    {
                        Id = Guid.NewGuid(),
                        SectionId = section.Id,
                        Title = $"Урок {s}.{l} — {course.Title}",
                        VideoUrl = "https://www.youtube.com/embed/1trvO6dqQUI",
                        TextContent = $"Это материал урока {s}.{l}. Изучите видео и текстовые материалы, после чего нажмите «Отметить пройденным». В этом уроке мы разбираем тему, связанную с курсом «{course.Title}».",
                        OrderIndex = l
                    });
                }
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedCategoriesAsync(ApplicationDbContext db)
    {
        if (await db.Categories.AnyAsync()) return;

        db.Categories.AddRange(
            new Category { Name = "Программирование", Slug = "programming" },
            new Category { Name = "Дизайн",           Slug = "design" },
            new Category { Name = "Маркетинг",        Slug = "marketing" },
            new Category { Name = "Бизнес",           Slug = "business" },
            new Category { Name = "Фотография",       Slug = "photography" },
            new Category { Name = "Музыка",           Slug = "music" },
            new Category { Name = "Иностранные языки", Slug = "languages" },
            new Category { Name = "Личностный рост",  Slug = "personal-growth" }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureDemoInstructorAsync(UserManager<ApplicationUser> userManager)
    {
        const string email = "demo.instructor@eduplatform.local";
        const string password = "DemoInstructor!1";
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            await ForceResetPasswordAsync(userManager, user, password);
            return user;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Демо Инструктор",
            EmailConfirmed = true,
            AvatarUrl = "/assets/img/user/user-29.jpg"
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new Exception("Не удалось создать демо-инструктора: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "Instructor");
        return user;
    }

    private record CourseSeed(
        string Title, string Slug, string Description,
        string CategorySlug, decimal Price, string Cover,
        decimal Rating, int Students);

    private static async Task SeedCoursesAsync(ApplicationDbContext db, Guid instructorId)
    {
        if (await db.Courses.AnyAsync()) return;

        var cats = await db.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id);

        var seeds = new[]
        {
            new CourseSeed("Полный курс веб-разработки 2026",                  "web-dev-2026",          "HTML, CSS, JavaScript, React и Node.js с нуля до продакшна.",                     "programming",  4500m, "course-01.jpg", 4.8m, 1240),
            new CourseSeed("UI/UX дизайн с нуля до Senior",                    "ui-ux-from-zero",       "Figma, исследование пользователей, проектирование интерфейсов и портфолио.",       "design",       3900m, "course-02.jpg", 4.7m,  860),
            new CourseSeed("Python для анализа данных",                        "python-data-analysis",  "NumPy, Pandas, Matplotlib и основы машинного обучения на практике.",              "programming",  3200m, "course-03.jpg", 4.6m,  720),
            new CourseSeed("Современный JavaScript: ES2024 и TypeScript",      "modern-js-ts",          "Все возможности современного JS, типизация и тестирование.",                       "programming",  2800m, "course-04.jpg", 4.5m,  540),
            new CourseSeed("SMM и контент-маркетинг",                          "smm-content",           "Стратегии продвижения в соцсетях, SMM-планирование и аналитика.",                  "marketing",    1900m, "course-05.jpg", 4.3m,  410),
            new CourseSeed("Фотография для начинающих",                        "photo-basics",          "Композиция, свет, обработка в Lightroom и съёмка на смартфон.",                   "photography",     0m, "course-06.jpg", 4.4m,  980),
            new CourseSeed("Бизнес-аналитика и стратегия",                     "business-analytics",    "Финмодели, KPI, юнит-экономика и принятие решений на данных.",                    "business",     4200m, "course-07.jpg", 4.6m,  330),
            new CourseSeed("Английский язык: уровень B1 → B2",                  "english-b1-b2",         "Грамматика, разговорная практика, лексика для работы и путешествий.",              "languages",    2400m, "course-08.jpg", 4.7m, 1520),
            new CourseSeed("Гитара с нуля за 30 дней",                          "guitar-30-days",        "Аккорды, бой, перебор и первые песни — пошаговая программа.",                      "music",           0m, "course-09.jpg", 4.5m,  640),
            new CourseSeed("Тайм-менеджмент и продуктивность",                  "time-management",       "GTD, планирование, борьба с прокрастинацией и формирование привычек.",            "personal-growth",1500m, "course-01.jpg", 4.2m,  290),
            new CourseSeed("Введение в Machine Learning",                       "ml-intro",              "Линейные модели, деревья, нейросети и реальные кейсы на Python.",                  "programming",  4900m, "course-03.jpg", 4.8m,  410),
            new CourseSeed("Графический дизайн: Adobe Illustrator",             "graphic-illustrator",   "Векторная графика, логотипы, иконки и подготовка к печати.",                       "design",       3100m, "course-02.jpg", 4.4m,  220)
        };

        var now = DateTime.UtcNow;
        var rnd = new Random(42);

        var courses = seeds.Select((s, i) => new Course
        {
            Id = Guid.NewGuid(),
            Title = s.Title,
            Slug = s.Slug,
            Description = s.Description,
            CoverUrl = $"/assets/img/course/{s.Cover}",
            Price = s.Price,
            Language = "ru",
            IsPublished = true,
            AverageRating = s.Rating,
            StudentsCount = s.Students,
            CategoryId = cats[s.CategorySlug],
            InstructorId = instructorId,
            CreatedAt = now.AddDays(-rnd.Next(1, 180)),
            UpdatedAt = now
        }).ToList();

        db.Courses.AddRange(courses);
        await db.SaveChangesAsync();
    }
}
