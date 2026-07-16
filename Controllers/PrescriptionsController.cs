using medrec.Data;
using medrec.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace medrec.Controllers;

public sealed class PrescriptionsController : Controller
{
    private readonly EmrRepository _repository;

    public PrescriptionsController(EmrRepository repository)
    {
        _repository = repository;
    }

    public async Task<IActionResult> Index()
    {
        return View(await BuildPageAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "NewPrescription")] PrescriptionFormModel newPrescription)
    {
        newPrescription.Prescriber = CurrentUserName();
        ModelState.Remove("NewPrescription.Prescriber");

        if (newPrescription.HasIncompleteDrugRows())
        {
            ModelState.AddModelError("NewPrescription.Items", "Complete each drug row.");
        }

        if (newPrescription.NormalizedItems().Count == 0)
        {
            ModelState.AddModelError("NewPrescription.Items", "Add at least one drug.");
        }

        var dashboard = await _repository.GetDashboardAsync();
        var selectedPatient = dashboard.Patients.FirstOrDefault(patient => patient.Id == newPrescription.PatientId);
        if (selectedPatient is null)
        {
            ModelState.AddModelError("NewPrescription.PatientId", "Select a patient.");
        }

        if (newPrescription.ClinicalRecordId.HasValue)
        {
            var selectedCheckUp = dashboard.RecentRecords.FirstOrDefault(record => record.Id == newPrescription.ClinicalRecordId);
            if (selectedCheckUp is null || selectedCheckUp.PatientId != newPrescription.PatientId)
            {
                ModelState.AddModelError("NewPrescription.ClinicalRecordId", "Select a check up for the selected patient.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewData["OpenModal"] = "prescriptionModal";
            return View("Index", await BuildPageAsync(newPrescription));
        }

        try
        {
            await _repository.CreatePrescriptionAsync(newPrescription);
            TempData["Success"] = "Prescription saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = SaveError(ex);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Print(int id)
    {
        try
        {
            await _repository.RegisterPrescriptionPrintAsync(id);
        }
        catch
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        return NoContent();
    }

    private async Task<PrescriptionsPageViewModel> BuildPageAsync(PrescriptionFormModel? newPrescription = null)
    {
        var dashboard = await _repository.GetDashboardAsync();
        var firstPatientId = dashboard.Patients.FirstOrDefault()?.Id ?? 0;
        var prescriptionForm = newPrescription ?? new PrescriptionFormModel { PatientId = firstPatientId };
        prescriptionForm.Prescriber = CurrentUserName();
        var medicationSuggestions = dashboard.Prescriptions
            .SelectMany(prescription => prescription.Items.Count > 0
                ? prescription.Items.Select(item => item.Medication)
                : [prescription.Medication])
            .Select(medication => medication.Trim())
            .Where(medication => !string.IsNullOrWhiteSpace(medication))
            .GroupBy(medication => medication, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.First())
            .Take(50)
            .ToList();

        if (prescriptionForm.Items.Count == 0)
        {
            prescriptionForm.Items.Add(new PrescriptionItemFormModel());
        }

        return new PrescriptionsPageViewModel
        {
            Patients = dashboard.Patients,
            Records = dashboard.RecentRecords,
            Prescriptions = dashboard.Prescriptions,
            PrescriptionLayout = dashboard.PrescriptionLayout,
            DataNotice = dashboard.DataNotice,
            NewPrescription = prescriptionForm,
            MedicationSuggestions = medicationSuggestions
        };
    }

    private static string SaveError(Exception ex)
    {
        return ex is InvalidOperationException
            ? ex.Message
            : "Save failed. Check the MySQL connection and schema.";
    }

    private string CurrentUserName()
    {
        return HttpContext.Session.GetString("UserName")?.Trim() is { Length: > 0 } name
            ? name
            : "Doctor";
    }
}
