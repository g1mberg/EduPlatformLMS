using EduPlatform.Infrastructure.Persistence;
using dreams.Models.Courses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dreams.Controllers;

[Route("instructors")]
public class InstructorsController : Controller
{
    private readonly ApplicationDbContext _db;

    public InstructorsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Index(Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || user.IsBlocked) return NotFound();

        var courses = await _db.Courses
            .Include(c => c.Category)
            .Where(c => c.InstructorId == id && c.IsPublished)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var totalStudents = courses.Sum(c => c.StudentsCount);
        var avg = courses.Count == 0 ? 0m :
                  Math.Round(courses.Average(c => c.AverageRating), 2);

        var vm = new InstructorPublicViewModel
        {
            Instructor = user,
            Courses = courses,
            CoursesCount = courses.Count,
            TotalStudents = totalStudents,
            AverageRating = avg
        };
        return View(vm);
    }
}
