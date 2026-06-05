using System.ComponentModel.DataAnnotations;
using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class CourseWizardViewModel
{
    // Шаг 1 — инфо
    [Required(ErrorMessage = "Введите название курса")]
    [StringLength(160, MinimumLength = 3, ErrorMessage = "Название от 3 до 160 символов")]
    public string Title { get; set; } = "";

    [StringLength(160)]
    [RegularExpression(@"^[a-z0-9\-]*$", ErrorMessage = "Только латиница, цифры и дефис")]
    public string? Slug { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [Url(ErrorMessage = "Должна быть валидная ссылка")]
    [StringLength(500)]
    public string? CoverUrl { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Цена от 0 до 1 000 000")]
    public decimal Price { get; set; }

    public string Language { get; set; } = "ru";

    // Шаг 2-3 — секции с уроками
    public List<WizardSection> Sections { get; set; } = new();

    // Шаг 4 — финальный тест (опционально, прикрепляется к последнему уроку последней секции)
    public bool IncludeFinalTest { get; set; }
    public string? FinalTestTitle { get; set; }
    [Range(1, 100)]
    public int FinalTestPassingScore { get; set; } = 70;
    public List<WizardTestQuestion> FinalTestQuestions { get; set; } = new();

    // Шаг 5 — публикация
    public bool IsPublished { get; set; }

    // UI
    public IReadOnlyList<Category> AvailableCategories { get; set; } = Array.Empty<Category>();
    public bool AllowsTests { get; set; }
}

public class WizardSection
{
    public string Title { get; set; } = "";
    public List<WizardLesson> Lessons { get; set; } = new();
}

public class WizardLesson
{
    public string Title { get; set; } = "";
    public string? VideoUrl { get; set; }
    public string? TextContent { get; set; }
}

public class WizardTestQuestion
{
    public string Text { get; set; } = "";
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public List<WizardTestOption> Options { get; set; } = new();
}

public class WizardTestOption
{
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}
