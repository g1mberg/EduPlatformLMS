using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class TakeTestViewModel
{
    public Course Course { get; init; } = null!;
    public Lesson Lesson { get; init; } = null!;
    public Test Test { get; init; } = null!;
    public TestAttempt? LastAttempt { get; init; }
}

public class TestResultViewModel
{
    public Course Course { get; init; } = null!;
    public Lesson Lesson { get; init; } = null!;
    public Test Test { get; init; } = null!;
    public TestAttempt Attempt { get; init; } = null!;
    public Certificate? Certificate { get; init; }
}
