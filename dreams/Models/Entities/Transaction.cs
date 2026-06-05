namespace dreams.Models.Entities;

public enum TransactionType
{
    TopUp = 0,                  // ручное пополнение админом
    CoursePurchase = 1,
    PlatformCommission = 2,
    SubscriptionPayment = 3,
    InstructorPayout = 4
}

public class Transaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public decimal Amount { get; set; }                 // + пополнение, - списание
    public TransactionType Type { get; set; }

    public Guid? RelatedCourseId { get; set; }
    public Course? RelatedCourse { get; set; }

    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
