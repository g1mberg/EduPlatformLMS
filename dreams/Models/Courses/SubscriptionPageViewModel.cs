using dreams.Models.Entities;

namespace dreams.Models.Courses;

public class SubscriptionPageViewModel
{
    public IReadOnlyList<SubscriptionPlan> Plans { get; init; } = Array.Empty<SubscriptionPlan>();
    public SubscriptionPlanCode? ActivePlanCode { get; init; }
    public DateTime? ActiveExpiresAt { get; init; }
    public decimal Credits { get; init; }
    public bool IsAuthenticated { get; init; }
}
