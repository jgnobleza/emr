using System.Text;
using medrec.Data;
using medrec.Security;
using medrec.Services;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

[AdminOnly]
public sealed class AdminController : Controller
{
    private readonly EmrRepository _repository;
    private readonly AccountService _accounts;

    public AdminController(EmrRepository repository, AccountService accounts)
    {
        _repository = repository;
        _accounts = accounts;
    }

    public async Task<IActionResult> Index()
    {
        return View(await BuildPageAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDoctor([Bind(Prefix = "NewDoctor")] CreateDoctorAccountViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(newDoctor: form));
        }

        try
        {
            await _accounts.CreateDoctorAsync(form.FullName, form.Email, form.Password);
            TempData["Success"] = $"Doctor account for {form.FullName.Trim()} created.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("NewDoctor.Email", ex is InvalidOperationException ? ex.Message : "Account creation failed. Check the MySQL connection.");
            return View("Index", await BuildPageAsync(newDoctor: form));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnarchivePatient(int id)
    {
        try
        {
            await _repository.UnarchivePatientAsync(id);
            TempData["Success"] = "Patient restored to the active patient list.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex is InvalidOperationException ? ex.Message : "Patient could not be restored.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdatePrescriptionLayout([Bind(Prefix = "PrescriptionLayoutForm")] PrintLayoutFormModel form) =>
        SavePrintLayoutAsync(form, "Prescription", "PrescriptionLayoutForm", "prescriptionLayoutModal", "PrescriptionLayoutForm.Logo");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateDiagnosisLayout([Bind(Prefix = "DiagnosisLayoutForm")] PrintLayoutFormModel form) =>
        SavePrintLayoutAsync(form, "Diagnosis", "DiagnosisLayoutForm", "diagnosisLayoutModal", "DiagnosisLayoutForm.Logo");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLayoutImage(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Select an image." });
        if (!IsAllowedImage(file)) return BadRequest(new { error = "Upload a JPG, PNG, GIF, or WEBP image." });
        return Json(new { url = await SaveUploadAsync(file, "layout-images") });
    }

    private async Task<IActionResult> SavePrintLayoutAsync(PrintLayoutFormModel form, string documentType, string formPrefix, string modalId, string logoKey)
    {
        form.DocumentType = documentType;
        ApplyLayoutDefaults(form, documentType);
        if (string.Equals(documentType, "Diagnosis", StringComparison.OrdinalIgnoreCase))
        {
            RelaxDiagnosisLayoutValidation();
        }
        form.LayoutJson = ReadLayoutJson(formPrefix) ?? form.LayoutJson;
        if (form.Logo is { Length: > 0 } && !IsAllowedImage(form.Logo))
        {
            ModelState.AddModelError(logoKey, "Upload a JPG, PNG, GIF, or WEBP image.");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? entry.Key
                        : $"{entry.Key}: {error.ErrorMessage}"))
                .Distinct()
                .ToArray();

            TempData["Error"] = errors.Length > 0
                ? $"Layout validation failed: {string.Join(" | ", errors)}"
                : "Check the required layout fields.";
            ViewData["OpenModal"] = modalId;
            return View("Index", await BuildPageAsync(documentType, form));
        }

        try
        {
            var logoUrl = await SaveUploadAsync(form.Logo, "logos") ?? form.LogoUrl;
            await _repository.UpdatePrintLayoutAsync(form, documentType, logoUrl);
            TempData["Success"] = $"{documentType} layout saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Save failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<AdminPageViewModel> BuildPageAsync(string? formType = null, PrintLayoutFormModel? layoutForm = null, CreateDoctorAccountViewModel? newDoctor = null)
    {
        var dashboard = await _repository.GetDashboardAsync();
        var prescriptionForm = PrintLayoutFormModel.From(dashboard.PrescriptionLayout);
        var diagnosisForm = PrintLayoutFormModel.From(dashboard.DiagnosisLayout);
        if (layoutForm is not null && formType == "Prescription") prescriptionForm = layoutForm;
        if (layoutForm is not null && formType == "Diagnosis") diagnosisForm = layoutForm;

        return new AdminPageViewModel
        {
            Users = await _accounts.GetUsersAsync(),
            ArchivedPatients = await _repository.GetArchivedPatientsAsync(),
            NewDoctor = newDoctor ?? new(),
            PrescriptionLayout = dashboard.PrescriptionLayout,
            DiagnosisLayout = dashboard.DiagnosisLayout,
            PrescriptionLayoutForm = prescriptionForm,
            DiagnosisLayoutForm = diagnosisForm,
            DataNotice = dashboard.DataNotice
        };
    }

    private string? ReadLayoutJson(string prefix)
    {
        var encoded = Request.Form[$"{prefix}.LayoutEncoded"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(encoded)) return Request.Form[$"{prefix}.LayoutJson"].FirstOrDefault();
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
        catch { return null; }
    }

    private void ApplyLayoutDefaults(PrintLayoutFormModel form, string documentType)
    {
        var normalizedDocumentType = medrec.Models.PrintLayout.NormalizeDocumentType(documentType);
        var userName = HttpContext.Session.GetString("UserName")?.Trim();
        var userLicense = HttpContext.Session.GetString("UserLicenseNumber")?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(form.DocumentTitle))
        {
            form.DocumentTitle = normalizedDocumentType == "Diagnosis" ? "Medical Certificate" : "Prescription";
            ModelState.Remove(nameof(form.DocumentTitle));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicName))
        {
            form.ClinicName = "MedRec Clinic";
            ModelState.Remove(nameof(form.ClinicName));
        }

        if (string.IsNullOrWhiteSpace(form.DoctorName))
        {
            form.DoctorName = string.IsNullOrWhiteSpace(userName) ? "Doctor" : userName;
            ModelState.Remove(nameof(form.DoctorName));
        }

        if (string.IsNullOrWhiteSpace(form.SignatoryName))
        {
            form.SignatoryName = form.DoctorName;
            ModelState.Remove(nameof(form.SignatoryName));
        }

        if (string.IsNullOrWhiteSpace(form.LicenseNumber))
        {
            form.LicenseNumber = userLicense;
            ModelState.Remove(nameof(form.LicenseNumber));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicSchedule))
        {
            form.ClinicSchedule = string.Empty;
            ModelState.Remove(nameof(form.ClinicSchedule));
        }

        if (string.IsNullOrWhiteSpace(form.ClinicAddress))
        {
            form.ClinicAddress = string.Empty;
            ModelState.Remove(nameof(form.ClinicAddress));
        }

        if (string.IsNullOrWhiteSpace(form.LogoPosition))
        {
            form.LogoPosition = "Left";
            ModelState.Remove(nameof(form.LogoPosition));
        }

        if (string.IsNullOrWhiteSpace(form.DetailsAlignment))
        {
            form.DetailsAlignment = "Left";
            ModelState.Remove(nameof(form.DetailsAlignment));
        }
    }

    private void RelaxDiagnosisLayoutValidation()
    {
        var fields = new[]
        {
            nameof(PrintLayoutFormModel.DocumentType),
            nameof(PrintLayoutFormModel.DocumentTitle),
            nameof(PrintLayoutFormModel.ClinicName),
            nameof(PrintLayoutFormModel.ClinicSchedule),
            nameof(PrintLayoutFormModel.ClinicAddress),
            nameof(PrintLayoutFormModel.DoctorName),
            nameof(PrintLayoutFormModel.LicenseNumber),
            nameof(PrintLayoutFormModel.LogoPosition),
            nameof(PrintLayoutFormModel.DetailsAlignment),
            nameof(PrintLayoutFormModel.SignatoryName)
        };

        foreach (var field in fields)
        {
            var keys = ModelState.Keys
                .Where(key => key.Equals(field, StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith($".{field}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                ModelState.Remove(key);
            }
        }
    }

    private static bool IsAllowedImage(IFormFile file) =>
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase);

    private static async Task<string?> SaveUploadAsync(IFormFile? file, string folder)
    {
        if (file is null || file.Length == 0) return null;
        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);
        Directory.CreateDirectory(root);
        await using var stream = System.IO.File.Create(Path.Combine(root, fileName));
        await file.CopyToAsync(stream);
        return $"/uploads/{folder}/{fileName}";
    }
}
