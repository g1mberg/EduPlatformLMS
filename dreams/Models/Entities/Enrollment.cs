namespace dreams.Models.Entities;

public class Enrollment
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public ApplicationUser Student { get; set; } = null!;

    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public decimal ProgressPercent { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<LessonProgress> LessonProgress { get; set; } = new List<LessonProgress>();
}

public class LessonProgress
{
    public Guid Id { get; set; }

    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
}
