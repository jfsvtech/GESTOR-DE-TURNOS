using System.Diagnostics;
using GeneradorTurnos.Models;
using GeneradorTurnos.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeneradorTurnos.Controllers;

public class HomeController : Controller
{
    private readonly ITenantRepository _tenants;
    public HomeController(ITenantRepository tenants) => _tenants = tenants;

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var negocios = (await _tenants.GetAllAsync()).Where(t => t.Activo).ToList();
        return View(negocios);
    }

    [HttpGet("/privacidad")]
    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
