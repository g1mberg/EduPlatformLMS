using EduPlatform.Application.Abstractions;
using EduPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduPlatform.Infrastructure.Persistence;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    DbSet<ApplicationUser> IApplicationDbContext.Users => Users;
    Task<int> IApplicationDbContext.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonAttachment> LessonAttachments => Set<LessonAttachment>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    public DbSet<Test> Tests => Set<Test>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<InstructorSubscription> InstructorSubscriptions => Set<InstructorSubscription>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Course>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.AverageRating).HasPrecision(3, 2);
            e.HasOne(x => x.Instructor)
                .WithMany(u => u.AuthoredCourses)
                .HasForeignKey(x => x.InstructorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();

        b.Entity<Enrollment>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();
            e.Property(x => x.ProgressPercent).HasPrecision(5, 2);
            e.HasOne(x => x.Student)
                .WithMany(u => u.Enrollments)
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Certificate>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.CourseId }).IsUnique();
            e.HasIndex(x => x.CertificateNumber).IsUnique();
            e.HasOne(x => x.Student)
                .WithMany(u => u.Certificates)
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Review>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Student)
                .WithMany(u => u.Reviews)
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Test>()
            .HasOne(t => t.Lesson)
            .WithOne(l => l.Test!)
            .HasForeignKey<Test>(t => t.LessonId);

        b.Entity<SubscriptionPlan>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.CommissionPercent).HasPrecision(5, 2);
            e.Property(x => x.PriceCredits).HasPrecision(18, 2);
        });

        b.Entity<InstructorSubscription>()
            .HasOne(x => x.Instructor)
            .WithMany(u => u.Subscriptions)
            .HasForeignKey(x => x.InstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.User)
                .WithMany(u => u.Transactions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ApplicationUser>().Property(x => x.Credits).HasPrecision(18, 2);

        b.Entity<TestAttempt>()
            .HasOne(x => x.Student)
            .WithMany(u => u.TestAttempts)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<StudentAnswer>()
            .HasOne(x => x.SelectedOption)
            .WithMany()
            .HasForeignKey(x => x.SelectedOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<StudentAnswer>()
            .HasOne(x => x.Question)
            .WithMany()
            .HasForeignKey(x => x.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<LessonProgress>()
            .HasOne(x => x.Lesson)
            .WithMany()
            .HasForeignKey(x => x.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Transaction>()
            .HasOne(x => x.RelatedCourse)
            .WithMany()
            .HasForeignKey(x => x.RelatedCourseId)
            .OnDelete(DeleteBehavior.NoAction);

        // Seed подписочных планов
        b.Entity<SubscriptionPlan>().HasData(
            new SubscriptionPlan { Id = 1, Code = SubscriptionPlanCode.Free,  MaxCourses = 3,    CommissionPercent = 30m, AllowsTests = false, PriceCredits = 0m },
            new SubscriptionPlan { Id = 2, Code = SubscriptionPlanCode.Basic, MaxCourses = 10,   CommissionPercent = 20m, AllowsTests = true,  PriceCredits = 100m },
            new SubscriptionPlan { Id = 3, Code = SubscriptionPlanCode.Pro,   MaxCourses = null, CommissionPercent = 10m, AllowsTests = true,  PriceCredits = 300m }
        );
    }
}
