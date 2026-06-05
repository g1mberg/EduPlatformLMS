namespace dreams.Models.Entities;

public enum SubscriptionPlanCode
{
    Free = 0,
    Basic = 1,
    Pro = 2
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public SubscriptionPlanCode Code { get; set; }
    public int? MaxCourses { get; set; }            // null = безлимит (Pro)
    public decimal CommissionPercent { get; set; }
    public bool AllowsTests { get; set; }
    public decimal PriceCredits { get; set; }

    public ICollection<InstructorSubscription> Subscriptions { get; set; } = new List<InstructorSubscription>();
}

public class InstructorSubscription
{
    public Guid Id { get; set; }
    public Guid InstructorId { get; set; }
    public ApplicationUser Instructor { get; set; } = null!;

    public int PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}
