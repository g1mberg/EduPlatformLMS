using System.Diagnostics;
using dreams.Models;
using dreams.Models.Home;
using EduPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace dreams.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>> _roles;

    public HomeController(ApplicationDbContext db, RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>> roles)
    {
        _db = db;
        _roles = roles;
    }

    public async Task<IActionResult> Index()
    {
        var totalCourses = await _db.Courses.CountAsync(c => c.IsPublished);
        var totalStudents = await _db.Enrollments.Select(e => e.StudentId).Distinct().CountAsync();
        var instrRole = await _roles.FindByNameAsync("Instructor");
        var totalInstructors = instrRole is null ? 0
            : await _db.UserRoles.CountAsync(ur => ur.RoleId == instrRole.Id);
        var certsIssued = await _db.Certificates.CountAsync();

        var featured = await _db.Courses
            .Include(c => c.Category)
            .Include(c => c.Instructor)
            .Where(c => c.IsPublished)
            .OrderByDescending(c => c.AverageRating)
            .ThenByDescending(c => c.StudentsCount)
            .Take(6)
            .ToListAsync();

        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Take(8)
            .ToListAsync();

        var topInstructors = await _db.Courses
            .Where(c => c.IsPublished)
            .GroupBy(c => c.InstructorId)
            .Select(g => new
            {
                InstructorId = g.Key,
                CoursesCount = g.Count(),
                StudentsCount = g.Sum(c => c.StudentsCount),
                AvgRating = g.Average(c => c.AverageRating)
            })
            .OrderByDescending(x => x.StudentsCount)
            .Take(5)
            .ToListAsync();

        var instructorIds = topInstructors.Select(x => x.InstructorId).ToList();
        var instructorsById = await _db.Users
            .Where(u => instructorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var topRows = topInstructors.Select(x => new TopInstructorRow
        {
            Id = x.InstructorId,
            Name = instructorsById.GetValueOrDefault(x.InstructorId)?.FullName ?? "—",
            AvatarUrl = instructorsById.GetValueOrDefault(x.InstructorId)?.AvatarUrl,
            CoursesCount = x.CoursesCount,
            StudentsCount = x.StudentsCount,
            AverageRating = Math.Round(x.AvgRating, 1)
        }).ToList();

        var vm = new HomePageViewModel
        {
            TotalCourses = totalCourses,
            TotalStudents = totalStudents,
            TotalInstructors = totalInstructors,
            CertificatesIssued = certsIssued,
            FeaturedCourses = featured,
            Categories = categories,
            TopInstructors = topRows
        };
        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [HttpGet("/about")]
    public IActionResult About() => View();
}

[Route("error")]
public class ErrorController : Controller
{
    [HttpGet("404")]
    public IActionResult NotFoundPage() { Response.StatusCode = 404; return View("NotFound"); }

    [HttpGet("403")]
    public IActionResult ForbiddenPage() { Response.StatusCode = 403; return View("Forbidden"); }

    [HttpGet("{code:int}")]
    public IActionResult Generic(int code)
    {
        Response.StatusCode = code;
        if (code == 404) return View("NotFound");
        if (code == 403) return View("Forbidden");
        return View("Generic", code);
    }
}
