namespace dreams.Models.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;

    public ICollection<Course> Courses { get; set; } = new List<Course>();
}

public class Course
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public decimal Price { get; set; }
    public string Language { get; set; } = "ru";
    public bool IsPublished { get; set; }
    public decimal AverageRating { get; set; }
    public int StudentsCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid InstructorId { get; set; }
    public ApplicationUser Instructor { get; set; } = null!;

    public ICollection<Section> Sections { get; set; } = new List<Section>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();
}

public class Section
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string Title { get; set; } = null!;
    public int OrderIndex { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
}

public class Lesson
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public Section Section { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? VideoUrl { get; set; }
    public string? TextContent { get; set; }
    public int OrderIndex { get; set; }

    public ICollection<LessonAttachment> Attachments { get; set; } = new List<LessonAttachment>();
    public Test? Test { get; set; }
}

public class LessonAttachment
{
    public Guid Id { get; set; }
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string FileUrl { get; set; } = null!;
    public string FileName { get; set; } = null!;
}
