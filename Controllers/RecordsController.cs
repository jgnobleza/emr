using medrec.Data;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class RecordsController : Controller
{
    private readonly EmrRepository _repository;

    public RecordsController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index(int? patientId = null, int? recordId = null)
    {
        return View(await BuildPageAsync(patientId: patientId, recordId: recordId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "NewRecord")] RecordFormModel newRecord)
    {
        newRecord.DoctorName = CurrentUserName();
        ModelState.Remove("NewRecord.DoctorName");

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "recordModal";
            return View("Index", await BuildPageAsync(newRecord: newRecord));
        }

        try
        {
            var recordId = await _repository.CreateClinicalRecordAsync(newRecord);
            TempData["Success"] = "Check up saved.";
            return RedirectToAction(nameof(Index), new { patientId = newRecord.PatientId, recordId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index), new { patientId = newRecord.PatientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDiagnosis(DiagnosisFormModel form)
    {
        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = null;
            return View("Index", await BuildPageAsync(recordId: form.RecordId, diagnosis: form));
        }

        try
        {
            await _repository.UpdateDiagnosisAsync(form);
            TempData["Success"] = "Diagnosis saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index), new { recordId = form.RecordId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCheckup([Bind(Prefix = "CheckupEdit")] CheckupEditFormModel form)
    {
        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "checkupEditModal";
            return View("Index", await BuildPageAsync(recordId: form.RecordId, checkupEdit: form));
        }

        try
        {
            await _repository.UpdateCheckupAsync(form);
            TempData["Success"] = "Check up updated.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index), new { recordId = form.RecordId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLab([Bind(Prefix = "NewLab")] LabResultFormModel newLab)
    {
        if (newLab.File is null && string.IsNullOrWhiteSpace(newLab.FileUrl))
        {
            ModelState.AddModelError("NewLab.File", "Upload a file or enter a file URL.");
        }

        if (newLab.File is not null && !IsAllowedLabFile(newLab.File))
        {
            ModelState.AddModelError("NewLab.File", "Upload a PDF, JPG, PNG, GIF, or WEBP file.");
        }

        var dashboard = await _repository.GetDashboardAsync();
        var requestedCheckUp = dashboard.RecentRecords.FirstOrDefault(record => record.Id == newLab.ClinicalRecordId);
        if (requestedCheckUp is null || requestedCheckUp.PatientId != newLab.PatientId)
        {
            ModelState.AddModelError("NewLab.ClinicalRecordId", "Select requested check up.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "labModal";
            return View("Index", await BuildPageAsync(patientId: newLab.PatientId, recordId: newLab.ClinicalRecordId, newLab: newLab));
        }

        try
        {
            var fileUrl = await SaveUploadAsync(newLab.File, "labs") ?? newLab.FileUrl!.Trim();
            await _repository.CreateLabResultAsync(newLab, fileUrl);
            TempData["Success"] = "Lab result saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index), new { patientId = newLab.PatientId, recordId = newLab.ClinicalRecordId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachLab([Bind(Prefix = "LabAttachment")] LabAttachmentFormModel labAttachment)
    {
        var dashboard = await _repository.GetDashboardAsync();
        var lab = dashboard.LabResults.FirstOrDefault(item => item.Id == labAttachment.LabId);
        var record = dashboard.RecentRecords.FirstOrDefault(item => item.Id == labAttachment.ClinicalRecordId);

        if (lab is null || lab.PatientId != labAttachment.PatientId)
        {
            ModelState.AddModelError("LabAttachment.LabId", "Select lab.");
        }

        if (record is null || record.PatientId != labAttachment.PatientId)
        {
            ModelState.AddModelError("LabAttachment.ClinicalRecordId", "Select check up.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "attachLabModal";
            return View("Index", await BuildPageAsync(
                patientId: labAttachment.PatientId,
                recordId: labAttachment.ClinicalRecordId,
                labAttachment: labAttachment));
        }

        try
        {
            await _repository.AttachLabToCheckUpAsync(labAttachment);
            TempData["Success"] = "Lab attached.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index), new { patientId = labAttachment.PatientId, recordId = labAttachment.ClinicalRecordId });
    }

    private async Task<RecordsPageViewModel> BuildPageAsync(
        int? patientId = null,
        int? recordId = null,
        RecordFormModel? newRecord = null,
        LabResultFormModel? newLab = null,
        LabAttachmentFormModel? labAttachment = null,
        DiagnosisFormModel? diagnosis = null,
        CheckupEditFormModel? checkupEdit = null)
    {
        var dashboard = await _repository.GetDashboardAsync();
        var selectedRecord = recordId.HasValue
            ? dashboard.RecentRecords.FirstOrDefault(record => record.Id == recordId)
            : null;
        var selectedPatientId = patientId ?? selectedRecord?.PatientId ?? dashboard.Patients.FirstOrDefault()?.Id ?? 0;
        var selectedPatient = dashboard.Patients.FirstOrDefault(patient => patient.Id == selectedPatientId);
        var patientRecords = dashboard.RecentRecords
            .Where(record => record.PatientId == selectedPatientId)
            .OrderByDescending(record => record.VisitDate)
            .ToList();
        selectedRecord ??= patientRecords.FirstOrDefault();
        var selectedRecordId = selectedRecord?.Id;
        var patientLabs = dashboard.LabResults
            .Where(lab => lab.PatientId == selectedPatientId)
            .Where(lab => !lab.ClinicalRecordId.HasValue || lab.ClinicalRecordId == selectedRecordId)
            .OrderByDescending(lab => lab.RequestedDate)
            .ThenByDescending(lab => lab.ResultDate)
            .ToList();
        var complaintSuggestions = dashboard.RecentRecords
            .Select(record => record.ChiefComplaint.Trim())
            .Where(complaint => !string.IsNullOrWhiteSpace(complaint))
            .GroupBy(complaint => complaint, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.First())
            .Take(25)
            .ToList();

        var defaultRecord = newRecord ?? new RecordFormModel
        {
            PatientId = selectedPatientId,
            HeightCm = selectedPatient?.HeightCm,
            WeightKg = selectedPatient?.WeightKg,
            BloodPressure = selectedPatient?.BloodPressure ?? string.Empty,
            FetalHeartRate = selectedPatient?.FetalHeartTone ?? string.Empty
        };

        return new RecordsPageViewModel
        {
            Patients = dashboard.Patients,
            Records = dashboard.RecentRecords,
            Labs = dashboard.LabResults,
            SelectedPatient = selectedPatient,
            SelectedRecord = selectedRecord,
            PatientRecords = patientRecords,
            PatientLabs = patientLabs,
            DiagnosisLayout = dashboard.DiagnosisLayout,
            DataNotice = dashboard.DataNotice,
            NewRecord = PrepareRecordForm(defaultRecord),
            NewLab = newLab ?? new LabResultFormModel
            {
                PatientId = selectedPatientId,
                ClinicalRecordId = selectedRecord?.Id,
                RequestedDate = selectedRecord?.VisitDate ?? DateTime.Now
            },
            LabAttachment = labAttachment ?? new LabAttachmentFormModel
            {
                PatientId = selectedPatientId,
                ClinicalRecordId = selectedRecord?.Id,
                RequestedDate = selectedRecord?.VisitDate ?? DateTime.Now
            },
            Diagnosis = diagnosis ?? new DiagnosisFormModel
            {
                RecordId = selectedRecord?.Id ?? 0,
                Diagnosis = selectedRecord?.Diagnosis ?? string.Empty,
                HeightCm = selectedRecord?.HeightCm,
                WeightKg = selectedRecord?.WeightKg,
                BloodPressure = selectedRecord?.BloodPressure ?? string.Empty,
                FetalHeartRate = selectedRecord?.FetalHeartRate ?? string.Empty,
                TemperatureC = selectedRecord?.TemperatureC,
                Notes = selectedRecord?.Notes ?? string.Empty
            },
            CheckupEdit = checkupEdit ?? new CheckupEditFormModel
            {
                RecordId = selectedRecord?.Id ?? 0,
                ChiefComplaint = selectedRecord?.ChiefComplaint ?? string.Empty,
                HeightCm = selectedRecord?.HeightCm,
                WeightKg = selectedRecord?.WeightKg,
                BloodPressure = selectedRecord?.BloodPressure ?? string.Empty,
                FetalHeartRate = selectedRecord?.FetalHeartRate ?? string.Empty,
                TemperatureC = selectedRecord?.TemperatureC
            },
            ComplaintSuggestions = complaintSuggestions
        };
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

    private static bool IsAllowedLabFile(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };

        return allowed.Contains(extension);
    }

    private static string SaveError(Exception ex)
    {
        return ex is InvalidOperationException
            ? ex.Message
            : "Save failed. Check the MySQL connection and schema.";
    }

    private RecordFormModel PrepareRecordForm(RecordFormModel form)
    {
        form.DoctorName = CurrentUserName();
        return form;
    }

    private string CurrentUserName()
    {
        return HttpContext.Session.GetString("UserName")?.Trim() is { Length: > 0 } name
            ? name
            : "Doctor";
    }
}
