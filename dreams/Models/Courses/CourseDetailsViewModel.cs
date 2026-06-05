using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class CourseDetailsViewModel
{
    public Course Course { get; init; } = null!;
    public bool IsEnrolled { get; init; }
    public bool IsOwner { get; init; }
    public int LessonsCount { get; init; }

    public IReadOnlyList<Review> Reviews { get; init; } = Array.Empty<Review>();
    public int ReviewsCount { get; init; }
    public Review? MyReview { get; init; }
    public bool CanLeaveReview { get; init; }   // записан И ещё не оставил
    public int[] RatingHistogram { get; init; } = new int[5]; // [5★, 4★, 3★, 2★, 1★]
}
