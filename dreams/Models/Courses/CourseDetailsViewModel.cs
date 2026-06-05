using dreams.Models.Entities;

namespace dreams.Models.Courses;

public class CourseDetailsViewModel
{
    public Course Course { get; init; } = null!;
    public bool IsEnrolled { get; init; }
    public bool IsOwner { get; init; }
    public int LessonsCount { get; init; }
}
