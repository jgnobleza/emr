using medrec.Data;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class PatientsController : Controller
{
    private readonly EmrRepository _repository;

    public PatientsController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index(bool addPatient = false)
    {
        if (addPatient)
        {
            ViewData["OpenModal"] = "patientModal";
        }

        var dashboard = await _repository.GetDashboardAsync();
        return View(new PatientsPageViewModel
        {
            Patients = dashboard.Patients,
            DataNotice = dashboard.DataNotice
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "NewPatient")] PatientFormModel newPatient)
    {
        ValidatePhoto(newPatient.Photo, "NewPatient.Photo");

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "patientModal";
            var dashboard = await _repository.GetDashboardAsync();
            return View("Index", new PatientsPageViewModel
            {
                Patients = dashboard.Patients,
                DataNotice = dashboard.DataNotice,
                NewPatient = newPatient
            });
        }

        try
        {
            var photoUrl = await SaveUploadAsync(newPatient.Photo, "patients") ?? newPatient.PhotoUrl;
            await _repository.CreatePatientAsync(newPatient, photoUrl);
            TempData["Success"] = "Patient saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "EditPatient")] PatientEditFormModel editPatient)
    {
        ValidatePhoto(editPatient.Photo, "EditPatient.Photo");

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "editPatientModal";
            var dashboard = await _repository.GetDashboardAsync();
            return View("Index", new PatientsPageViewModel
            {
                Patients = dashboard.Patients,
                DataNotice = dashboard.DataNotice,
                EditPatient = editPatient
            });
        }

        try
        {
            var photoUrl = await SaveUploadAsync(editPatient.Photo, "patients") ?? editPatient.PhotoUrl;
            await _repository.UpdatePatientAsync(editPatient, photoUrl);
            TempData["Success"] = "Patient updated.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        try
        {
            await _repository.ArchivePatientAsync(id);
            TempData["Success"] = "Patient archived.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index));
    }

    private void ValidatePhoto(IFormFile? file, string key)
    {
        if (file is null || file.Length == 0)
        {
            return;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };

        if (!allowed.Contains(extension))
        {
            ModelState.AddModelError(key, "Upload a JPG, PNG, GIF, or WEBP image.");
        }
    }

    private static async Task<string?> SaveUploadAsync(IFormFile? file, string folder)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);
        Directory.CreateDirectory(uploadRoot);

        var path = Path.Combine(uploadRoot, fileName);
        await using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream);

        return $"/uploads/{folder}/{fileName}";
    }

    private static string SaveError(Exception ex)
    {
        return ex is InvalidOperationException
            ? ex.Message
            : "Save failed. Check the PostgreSQL connection and schema.";
    }

}

