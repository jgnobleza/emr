using medrec.Data;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class SyncController : Controller
{
    private readonly EmrRepository _repository;

    public SyncController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _repository.GetDashboardAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Manual(string? returnUrl = null)
    {
        try
        {
            var count = await _repository.ManualSyncAsync();
            TempData["Success"] = $"{count} change(s) synced.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex is InvalidOperationException
                ? ex.Message
                : "Sync failed. Check the PostgreSQL connection and schema.";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CleanupRecords(string? returnUrl = null)
    {
        try
        {
            var summary = await _repository.CleanupRecordDataAsync();
            TempData["Success"] = $"Record cleanup completed: {summary}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex is InvalidOperationException
                ? ex.Message
                : "Record cleanup failed. Check the PostgreSQL connection and schema.";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }
}

