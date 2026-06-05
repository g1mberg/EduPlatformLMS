using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class CourseAnalyticsViewModel
{
    public Course Course { get; init; } = null!;
    public int LessonsCount { get; init; }
    public int EnrollmentsCount { get; init; }
    public int CompletedCount { get; init; }
    public decimal AvgProgress { get; init; }
    public int Certificates { get; init; }
    public decimal Revenue { get; init; }
    public decimal Commission { get; init; }
    public int TestAttempts { get; init; }
    public decimal AvgTestScore { get; init; }

    public IReadOnlyList<Review> Reviews { get; init; } = Array.Empty<Review>();
    public IReadOnlyList<Enrollment> RecentEnrollments { get; init; } = Array.Empty<Enrollment>();
    public IReadOnlyList<CourseAnalyticsMonth> MonthlyEnrollments { get; init; } = Array.Empty<CourseAnalyticsMonth>();
}

public class CourseAnalyticsMonth
{
    public string Label { get; init; } = "";
    public int Count { get; init; }
}
