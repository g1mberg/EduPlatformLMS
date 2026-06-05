namespace dreams.Models.Entities;

public enum QuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    Text = 2
}

public class Test
{
    public Guid Id { get; set; }

    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string Title { get; set; } = null!;
    public int PassingScore { get; set; }       // % для прохождения

    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<TestAttempt> Attempts { get; set; } = new List<TestAttempt>();
}

public class Question
{
    public Guid Id { get; set; }

    public Guid TestId { get; set; }
    public Test Test { get; set; } = null!;

    public string Text { get; set; } = null!;
    public QuestionType Type { get; set; }
    public int OrderIndex { get; set; }

    public ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
}

public class AnswerOption
{
    public Guid Id { get; set; }

    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public string Text { get; set; } = null!;
    public bool IsCorrect { get; set; }
}

public class TestAttempt
{
    public Guid Id { get; set; }

    public Guid TestId { get; set; }
    public Test Test { get; set; } = null!;

    public Guid StudentId { get; set; }
    public ApplicationUser Student { get; set; } = null!;

    public int Score { get; set; }
    public bool IsPassed { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public ICollection<StudentAnswer> Answers { get; set; } = new List<StudentAnswer>();
}

public class StudentAnswer
{
    public Guid Id { get; set; }

    public Guid AttemptId { get; set; }
    public TestAttempt Attempt { get; set; } = null!;

    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public Guid? SelectedOptionId { get; set; }
    public AnswerOption? SelectedOption { get; set; }

    public string? TextAnswer { get; set; }
}
