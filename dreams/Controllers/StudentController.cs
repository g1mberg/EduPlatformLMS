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

[Route("student")]
[Authorize]
public class StudentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CertificateService _certificates;

    public StudentController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        CertificateService certificates)
    {
        _db = db;
        _userManager = userManager;
        _certificates = certificates;
    }

    [AllowAnonymous]
    [HttpGet("profile")]
    public IActionResult Profile() => View();

    // ---------- Список моих курсов ----------

    [HttpGet("courses")]
    public async Task<IActionResult> Courses()
    {
        var userId = GetUserId();
        var enrollments = await _db.Enrollments
            .Include(e => e.Course).ThenInclude(c => c.Category)
            .Include(e => e.Course).ThenInclude(c => c.Instructor)
            .Where(e => e.StudentId == userId)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync();
        return View(enrollments);
    }

    // ---------- Обзор курса ----------

    [HttpGet("courses/{slug}")]
    public async Task<IActionResult> CourseOverview(string slug)
    {
        var userId = GetUserId();

        var course = await _db.Courses
            .Include(c => c.Category)
            .Include(c => c.Instructor)
            .Include(c => c.Sections.OrderBy(s => s.OrderIndex))
                .ThenInclude(s => s.Lessons.OrderBy(l => l.OrderIndex)).ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (enrollment is null) return RedirectToAction("Details", "Courses", new { slug });

        var completed = await _db.LessonProgress
            .Where(p => p.EnrollmentId == enrollment.Id && p.IsCompleted)
            .Select(p => p.LessonId)
            .ToListAsync();

        var vm = new StudentCourseOverviewViewModel
        {
            Course = course,
            Enrollment = enrollment,
            CompletedLessonIds = completed.ToHashSet(),
            LessonsCount = course.Sections.Sum(s => s.Lessons.Count)
        };
        return View(vm);
    }

    // ---------- Страница урока ----------

    [HttpGet("courses/{slug}/lessons/{lessonId:guid}")]
    public async Task<IActionResult> Lesson(string slug, Guid lessonId)
    {
        var userId = GetUserId();

        var lesson = await _db.Lessons
            .Include(l => l.Test)
            .Include(l => l.Attachments)
            .Include(l => l.Section).ThenInclude(s => s.Course)
                .ThenInclude(c => c.Sections.OrderBy(x => x.OrderIndex))
                    .ThenInclude(s => s.Lessons.OrderBy(x => x.OrderIndex))
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.Section.Course.Slug == slug);
        if (lesson is null) return NotFound();

        var course = lesson.Section.Course;

        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (enrollment is null) return RedirectToAction("Details", "Courses", new { slug });

        var progress = await _db.LessonProgress
            .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId);

        var orderedLessons = course.Sections
            .OrderBy(s => s.OrderIndex)
            .SelectMany(s => s.Lessons.OrderBy(l => l.OrderIndex))
            .Select(l => l.Id)
            .ToList();
        var idx = orderedLessons.IndexOf(lessonId);

        var vm = new StudentLessonViewModel
        {
            Course = course,
            Section = lesson.Section,
            Lesson = lesson,
            IsCompleted = progress?.IsCompleted == true,
            PrevLessonId = idx > 0 ? orderedLessons[idx - 1] : null,
            NextLessonId = idx >= 0 && idx < orderedLessons.Count - 1 ? orderedLessons[idx + 1] : null,
            ProgressPercent = enrollment.ProgressPercent
        };
        return View(vm);
    }

    [HttpPost("courses/{slug}/lessons/{lessonId:guid}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLessonComplete(string slug, Guid lessonId)
    {
        var userId = GetUserId();

        var course = await _db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons)
            .FirstOrDefaultAsync(c => c.Slug == slug);
        if (course is null) return NotFound();

        var lesson = course.Sections.SelectMany(s => s.Lessons).FirstOrDefault(l => l.Id == lessonId);
        if (lesson is null) return NotFound();

        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (enrollment is null) return Forbid();

        var progress = await _db.LessonProgress
            .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId);

        if (progress is null)
        {
            _db.LessonProgress.Add(new LessonProgress
            {
                Id = Guid.NewGuid(),
                EnrollmentId = enrollment.Id,
                LessonId = lessonId,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow
            });
        }
        else
        {
            progress.IsCompleted = !progress.IsCompleted;
            progress.CompletedAt = progress.IsCompleted ? DateTime.UtcNow : null;
        }

        await _db.SaveChangesAsync();

        await RecalculateProgressAsync(enrollment, course);
        await _db.SaveChangesAsync();

        await _certificates.IssueIfEligibleAsync(userId, course.Id);

        return RedirectToAction(nameof(Lesson), new { slug, lessonId });
    }

    // ---------- Тесты ----------

    [HttpGet("courses/{slug}/lessons/{lessonId:guid}/test")]
    public async Task<IActionResult> TakeTest(string slug, Guid lessonId)
    {
        var userId = GetUserId();

        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Test!).ThenInclude(t => t.Questions.OrderBy(q => q.OrderIndex)).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.Section.Course.Slug == slug);
        if (lesson is null || lesson.Test is null) return NotFound();

        var course = lesson.Section.Course;
        var enrolled = await _db.Enrollments
            .AnyAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (!enrolled) return RedirectToAction("Details", "Courses", new { slug });

        var lastAttempt = await _db.TestAttempts
            .Where(a => a.TestId == lesson.Test.Id && a.StudentId == userId)
            .OrderByDescending(a => a.StartedAt)
            .FirstOrDefaultAsync();

        return View(new TakeTestViewModel
        {
            Course = course,
            Lesson = lesson,
            Test = lesson.Test,
            LastAttempt = lastAttempt
        });
    }

    [HttpPost("courses/{slug}/lessons/{lessonId:guid}/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitTest(string slug, Guid lessonId)
    {
        var userId = GetUserId();

        var lesson = await _db.Lessons
            .Include(l => l.Section).ThenInclude(s => s.Course)
            .Include(l => l.Test!).ThenInclude(t => t.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.Section.Course.Slug == slug);
        if (lesson is null || lesson.Test is null) return NotFound();

        var course = lesson.Section.Course;
        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.CourseId == course.Id && e.StudentId == userId);
        if (enrollment is null) return Forbid();

        var attempt = new TestAttempt
        {
            Id = Guid.NewGuid(),
            TestId = lesson.Test.Id,
            StudentId = userId,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow
        };

        var total = lesson.Test.Questions.Count;
        var correct = 0;

        foreach (var q in lesson.Test.Questions)
        {
            var formKey = $"q_{q.Id}";
            var raw = Request.Form[formKey];

            if (q.Type == QuestionType.Text)
            {
                var text = raw.ToString().Trim();
                var expected = q.Options.FirstOrDefault()?.Text?.Trim() ?? "";
                var match = !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(expected) &&
                            string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
                attempt.Answers.Add(new StudentAnswer
                {
                    Id = Guid.NewGuid(),
                    AttemptId = attempt.Id,
                    QuestionId = q.Id,
                    SelectedOptionId = null,
                    TextAnswer = text
                });
                if (match) correct++;
            }
            else if (q.Type == QuestionType.MultipleChoice)
            {
                var pickedIds = raw.Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                                   .Where(g => g.HasValue).Select(g => g!.Value).ToHashSet();
                var correctIds = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                var isCorrect = pickedIds.Count > 0 && pickedIds.SetEquals(correctIds);
                // Запишем все выбранные варианты как несколько StudentAnswer (по одному на opt)
                foreach (var oid in pickedIds)
                {
                    attempt.Answers.Add(new StudentAnswer
                    {
                        Id = Guid.NewGuid(),
                        AttemptId = attempt.Id,
                        QuestionId = q.Id,
                        SelectedOptionId = oid
                    });
                }
                if (pickedIds.Count == 0)
                {
                    attempt.Answers.Add(new StudentAnswer
                    {
                        Id = Guid.NewGuid(),
                        AttemptId = attempt.Id,
                        QuestionId = q.Id
                    });
                }
                if (isCorrect) correct++;
            }
            else
            {
                Guid? pickedId = Guid.TryParse(raw.ToString(), out var pg) ? pg : null;
                var selected = pickedId.HasValue
                    ? q.Options.FirstOrDefault(o => o.Id == pickedId.Value)
                    : null;
                attempt.Answers.Add(new StudentAnswer
                {
                    Id = Guid.NewGuid(),
                    AttemptId = attempt.Id,
                    QuestionId = q.Id,
                    SelectedOptionId = selected?.Id
                });
                if (selected?.IsCorrect == true) correct++;
            }
        }

        attempt.Score = total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total);
        attempt.IsPassed = attempt.Score >= lesson.Test.PassingScore;
        _db.TestAttempts.Add(attempt);

        // Если тест сдан — отмечаем урок-тест как пройденный
        if (attempt.IsPassed)
        {
            var progress = await _db.LessonProgress
                .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId);
            if (progress is null)
            {
                _db.LessonProgress.Add(new LessonProgress
                {
                    Id = Guid.NewGuid(),
                    EnrollmentId = enrollment.Id,
                    LessonId = lessonId,
                    IsCompleted = true,
                    CompletedAt = DateTime.UtcNow
                });
            }
            else if (!progress.IsCompleted)
            {
                progress.IsCompleted = true;
                progress.CompletedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();

        // пересчитываем прогресс курса
        var fullCourse = await _db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons)
            .FirstAsync(c => c.Id == course.Id);
        await RecalculateProgressAsync(enrollment, fullCourse);
        await _db.SaveChangesAsync();

        Certificate? cert = null;
        if (attempt.IsPassed)
            cert = await _certificates.IssueIfEligibleAsync(userId, course.Id);

        TempData["TestResult"] = $"{attempt.Id}";
        return RedirectToAction(nameof(TestResult), new { slug, lessonId, attemptId = attempt.Id });
    }

    [HttpGet("courses/{slug}/lessons/{lessonId:guid}/test/{attemptId:guid}")]
    public async Task<IActionResult> TestResult(string slug, Guid lessonId, Guid attemptId)
    {
        var userId = GetUserId();

        var attempt = await _db.TestAttempts
            .Include(a => a.Test).ThenInclude(t => t.Lesson).ThenInclude(l => l.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.StudentId == userId);
        if (attempt is null) return NotFound();

        var course = attempt.Test.Lesson.Section.Course;
        var cert = await _db.Certificates
            .FirstOrDefaultAsync(c => c.StudentId == userId && c.CourseId == course.Id);

        return View(new TestResultViewModel
        {
            Course = course,
            Lesson = attempt.Test.Lesson,
            Test = attempt.Test,
            Attempt = attempt,
            Certificate = cert
        });
    }

    // ---------- Сертификаты ----------

    [HttpGet("certificates")]
    public async Task<IActionResult> Certificates()
    {
        var userId = GetUserId();
        var certs = await _db.Certificates
            .Include(c => c.Course).ThenInclude(c => c.Category)
            .Where(c => c.StudentId == userId)
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync();
        return View(certs);
    }

    [HttpGet("certificates/{id:guid}")]
    public async Task<IActionResult> Certificate(Guid id)
    {
        var userId = GetUserId();
        var cert = await _db.Certificates
            .Include(c => c.Course).ThenInclude(c => c.Instructor)
            .Include(c => c.Student)
            .FirstOrDefaultAsync(c => c.Id == id && c.StudentId == userId);
        if (cert is null) return NotFound();
        return View(cert);
    }

    // ---------- helpers ----------

    private Guid GetUserId() => Guid.Parse(_userManager.GetUserId(User)!);

    private async Task RecalculateProgressAsync(Enrollment enrollment, Course course)
    {
        var totalLessons = course.Sections.Sum(s => s.Lessons.Count);
        var completed = await _db.LessonProgress
            .CountAsync(p => p.EnrollmentId == enrollment.Id && p.IsCompleted);
        enrollment.ProgressPercent = totalLessons == 0 ? 0m : Math.Round((decimal)completed * 100m / totalLessons, 2);
        enrollment.CompletedAt = completed == totalLessons && totalLessons > 0 ? DateTime.UtcNow : null;
    }
}
