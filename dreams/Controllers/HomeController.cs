using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using dreams.Models;

namespace dreams.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
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