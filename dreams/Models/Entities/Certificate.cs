namespace dreams.Models.Entities;

public class Certificate
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }
    public ApplicationUser Student { get; set; } = null!;

    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string CertificateNumber { get; set; } = null!;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}

public class Review
{
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public Guid StudentId { get; set; }
    public ApplicationUser Student { get; set; } = null!;

    public int Rating { get; set; }                  // 1..5
    public string? Text { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
