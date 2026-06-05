using System.ComponentModel.DataAnnotations;
using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class TestLessonRow
{
    public Guid LessonId { get; init; }
    public string SectionTitle { get; init; } = "";
    public string LessonTitle { get; init; } = "";
    public Guid? TestId { get; init; }
    public string? TestTitle { get; init; }
    public int? PassingScore { get; init; }
    public int QuestionsCount { get; init; }
}

public class TestListViewModel
{
    public Course Course { get; init; } = null!;
    public IReadOnlyList<TestLessonRow> Rows { get; init; } = Array.Empty<TestLessonRow>();
    public bool AllowsTests { get; init; }
}

public class TestFormViewModel
{
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = "";
    public Guid LessonId { get; set; }
    public string LessonTitle { get; set; } = "";

    public Guid? TestId { get; set; }

    [Required(ErrorMessage = "Введите название теста")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    [Range(1, 100, ErrorMessage = "Проходной балл от 1 до 100")]
    public int PassingScore { get; set; } = 70;

    public List<TestQuestionInput> Questions { get; set; } = new();
}

public class TestQuestionInput
{
    public Guid? Id { get; set; }
    public string Text { get; set; } = "";
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public List<TestOptionInput> Options { get; set; } = new();
}

public class TestOptionInput
{
    public Guid? Id { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}
