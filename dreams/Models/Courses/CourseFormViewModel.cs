using System.ComponentModel.DataAnnotations;
using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class CourseFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Введите название курса")]
    [StringLength(160, MinimumLength = 3, ErrorMessage = "Название от 3 до 160 символов")]
    [Display(Name = "Название")]
    public string Title { get; set; } = string.Empty;

    [StringLength(160)]
    [RegularExpression(@"^[a-z0-9\-]*$", ErrorMessage = "Только латинские буквы, цифры и дефис")]
    [Display(Name = "Slug (URL)")]
    public string? Slug { get; set; }

    [StringLength(4000)]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Url(ErrorMessage = "Должна быть валидная ссылка")]
    [StringLength(500)]
    [Display(Name = "Ссылка на обложку")]
    public string? CoverUrl { get; set; }

    [Required]
    [Display(Name = "Категория")]
    public int CategoryId { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Цена от 0 до 1 000 000")]
    [Display(Name = "Цена, ₽")]
    public decimal Price { get; set; }

    [Display(Name = "Язык")]
    public string Language { get; set; } = "ru";

    [Display(Name = "Опубликован")]
    public bool IsPublished { get; set; }

    // Заполняется контроллером для выпадающего списка
    public IReadOnlyList<Category> AvailableCategories { get; set; } = Array.Empty<Category>();
}
