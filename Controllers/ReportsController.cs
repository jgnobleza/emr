using medrec.Data;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class ReportsController : Controller
{
    private readonly EmrRepository _repository;

    public ReportsController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index()
    {
        var dashboard = await _repository.GetDashboardAsync();
        return View(new ReportsPageViewModel
        {
            RecentRecords = dashboard.RecentRecords,
            LabResults = dashboard.LabResults,
            Prescriptions = dashboard.Prescriptions,
            DataNotice = dashboard.DataNotice
        });
    }
}
