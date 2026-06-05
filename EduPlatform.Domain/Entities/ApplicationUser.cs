using Microsoft.AspNetCore.Identity;

namespace EduPlatform.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsBlocked { get; set; }
    public decimal Credits { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Course> AuthoredCourses { get; set; } = new List<Course>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<InstructorSubscription> Subscriptions { get; set; } = new List<InstructorSubscription>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<TestAttempt> TestAttempts { get; set; } = new List<TestAttempt>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
