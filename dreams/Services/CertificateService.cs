using dreams.Data;
using dreams.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace dreams.Services;

public class CertificateService
{
    private readonly ApplicationDbContext _db;

    public CertificateService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Если все уроки курса пройдены и все привязанные тесты сданы — выдаёт
    /// сертификат (если ещё не выдан). Возвращает выданный/существующий сертификат
    /// или null, если условия не выполнены.
    /// </summary>
    public async Task<Certificate?> IssueIfEligibleAsync(Guid studentId, Guid courseId)
    {
        var existing = await _db.Certificates
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.CourseId == courseId);
        if (existing is not null) return existing;

        var course = await _db.Courses
            .Include(c => c.Sections).ThenInclude(s => s.Lessons).ThenInclude(l => l.Test)
            .FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return null;

        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.CourseId == courseId && e.StudentId == studentId);
        if (enrollment is null) return null;

        var allLessons = course.Sections.SelectMany(s => s.Lessons).ToList();
        if (allLessons.Count == 0) return null;

        var completedLessonIds = await _db.LessonProgress
            .Where(p => p.EnrollmentId == enrollment.Id && p.IsCompleted)
            .Select(p => p.LessonId)
            .ToListAsync();

        if (completedLessonIds.Count < allLessons.Count) return null; // не все уроки пройдены

        // Проверяем все тесты курса — должен быть хотя бы один passed attempt
        var testIds = allLessons.Where(l => l.Test != null).Select(l => l.Test!.Id).ToList();
        if (testIds.Count > 0)
        {
            var passedTestIds = await _db.TestAttempts
                .Where(a => a.StudentId == studentId && a.IsPassed && testIds.Contains(a.TestId))
                .Select(a => a.TestId)
                .Distinct()
                .ToListAsync();
            if (passedTestIds.Count < testIds.Count) return null;
        }

        var cert = new Certificate
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            CourseId = courseId,
            CertificateNumber = GenerateNumber(),
            IssuedAt = DateTime.UtcNow
        };
        _db.Certificates.Add(cert);

        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = studentId,
            Title = "Поздравляем! Выдан сертификат",
            Message = $"Курс «{course.Title}» успешно пройден. Номер сертификата: {cert.CertificateNumber}.",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return cert;
    }

    private static string GenerateNumber() =>
        $"CERT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
