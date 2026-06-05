using EduPlatform.Domain.Entities;

namespace dreams.Models.Courses;

public class CourseCatalogViewModel
{
    public IReadOnlyList<Course> Courses { get; init; } = Array.Empty<Course>();
    public IReadOnlyList<CategoryFacet> Categories { get; init; } = Array.Empty<CategoryFacet>();

    public string? Search { get; init; }
    public string? CategorySlug { get; init; }
    public string? Price { get; init; }   // "free" | "paid" | null
    public string Sort { get; init; } = "new"; // "new" | "rating" | "students" | "price-asc" | "price-desc"

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 9;
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public record CategoryFacet(string Slug, string Name, int Count);
