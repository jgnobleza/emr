using System.Diagnostics;
using medrec.Data;
using Microsoft.AspNetCore.Mvc;
using medrec.Models;

namespace medrec.Controllers;

public class HomeController : Controller
{
    private readonly EmrRepository _repository;

    public HomeController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _repository.GetDashboardAsync());
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
}
