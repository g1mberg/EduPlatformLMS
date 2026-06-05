using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class InstructorPublicViewModel
{
    public ApplicationUser Instructor { get; init; } = null!;
    public IReadOnlyList<Course> Courses { get; init; } = Array.Empty<Course>();
    public int CoursesCount { get; init; }
    public int TotalStudents { get; init; }
    public decimal AverageRating { get; init; }
}
