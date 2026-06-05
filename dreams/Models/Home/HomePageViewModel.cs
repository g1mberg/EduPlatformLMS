using EduPlatform.Domain.Entities;

namespace dreams.Models.Home;

public class HomePageViewModel
{
    public int TotalCourses { get; init; }
    public int TotalStudents { get; init; }
    public int TotalInstructors { get; init; }
    public int CertificatesIssued { get; init; }
    public IReadOnlyList<Course> FeaturedCourses { get; init; } = Array.Empty<Course>();
    public IReadOnlyList<Category> Categories { get; init; } = Array.Empty<Category>();
    public IReadOnlyList<TopInstructorRow> TopInstructors { get; init; } = Array.Empty<TopInstructorRow>();
}

public class TopInstructorRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public int CoursesCount { get; init; }
    public int StudentsCount { get; init; }
    public decimal AverageRating { get; init; }
}
