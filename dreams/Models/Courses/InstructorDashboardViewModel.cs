using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class InstructorDashboardViewModel
{
    public int CoursesCount { get; init; }
    public int PublishedCount { get; init; }
    public int TotalStudents { get; init; }
    public decimal AverageRating { get; init; }
    public decimal TotalRevenue { get; init; }   // сумма за курсы минус комиссия
    public decimal PlatformCommissionTotal { get; init; }
    public decimal Credits { get; init; }
    public SubscriptionPlanCode? ActivePlan { get; init; }
    public DateTime? PlanExpires { get; init; }

    public IReadOnlyList<DashboardCourseRow> TopCourses { get; init; } = Array.Empty<DashboardCourseRow>();
    public IReadOnlyList<DashboardEnrollment> RecentEnrollments { get; init; } = Array.Empty<DashboardEnrollment>();
    public IReadOnlyList<DashboardMonthPoint> MonthlyEnrollments { get; init; } = Array.Empty<DashboardMonthPoint>();
}

public class DashboardCourseRow
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public int Students { get; init; }
    public decimal AverageRating { get; init; }
    public decimal Revenue { get; init; }
    public bool IsPublished { get; init; }
}

public class DashboardEnrollment
{
    public string StudentName { get; init; } = "";
    public string CourseTitle { get; init; } = "";
    public string CourseSlug { get; init; } = "";
    public DateTime At { get; init; }
    public decimal ProgressPercent { get; init; }
}

public class DashboardMonthPoint
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Count { get; init; }
    public string Label => $"{Year}-{Month:D2}";
}
