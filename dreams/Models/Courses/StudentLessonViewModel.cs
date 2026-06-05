using dreams.Models.Entities;

namespace dreams.Models.Courses;

public class StudentCourseOverviewViewModel
{
    public Course Course { get; init; } = null!;
    public Enrollment Enrollment { get; init; } = null!;
    public HashSet<Guid> CompletedLessonIds { get; init; } = new();
    public int LessonsCount { get; init; }
    public int CompletedCount => CompletedLessonIds.Count;
}

public class StudentLessonViewModel
{
    public Course Course { get; init; } = null!;
    public Lesson Lesson { get; init; } = null!;
    public Section Section { get; init; } = null!;
    public bool IsCompleted { get; init; }
    public Guid? PrevLessonId { get; init; }
    public Guid? NextLessonId { get; init; }
    public decimal ProgressPercent { get; init; }
}
