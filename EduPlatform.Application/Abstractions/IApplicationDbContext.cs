using EduPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduPlatform.Application.Abstractions;

/// <summary>
/// Абстракция над EF-контекстом для use-case'ов уровня Application,
/// чтобы бизнес-сервисы не зависели от Infrastructure напрямую.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Course> Courses { get; }
    DbSet<Section> Sections { get; }
    DbSet<Lesson> Lessons { get; }
    DbSet<LessonAttachment> LessonAttachments { get; }

    DbSet<Enrollment> Enrollments { get; }
    DbSet<LessonProgress> LessonProgress { get; }

    DbSet<Test> Tests { get; }
    DbSet<Question> Questions { get; }
    DbSet<AnswerOption> AnswerOptions { get; }
    DbSet<TestAttempt> TestAttempts { get; }
    DbSet<StudentAnswer> StudentAnswers { get; }

    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<InstructorSubscription> InstructorSubscriptions { get; }
    DbSet<Transaction> Transactions { get; }

    DbSet<Certificate> Certificates { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Notification> Notifications { get; }

    DbSet<ApplicationUser> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
