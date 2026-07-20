using medrec.Data;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class SyncController : Controller
{
    private readonly EmrRepository _repository;
    private readonly MedRecStorageOptions _options;
    private readonly ILogger<SyncController> _logger;

    public SyncController(EmrRepository repository, IConfiguration configuration, ILogger<SyncController> logger)
    {
        _repository = repository;
        _logger = logger;
        _options = configuration.GetSection("MedRec").Get<MedRecStorageOptions>() ?? new MedRecStorageOptions();
    }

    public async Task<IActionResult> Index()
    {
        ViewData["LocalMode"] = _options.UseLocalStorage;
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
            _logger.LogError(ex, "Manual sync failed.");
            TempData["Error"] = $"Sync failed: {ReadableError(ex)}";
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
            _logger.LogError(ex, "Record cleanup failed.");
            TempData["Error"] = $"Record cleanup failed: {ReadableError(ex)}";
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    private static string ReadableError(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}

