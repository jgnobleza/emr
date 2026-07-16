using medrec.Models;

namespace medrec.ViewModels;

public sealed class PrescriptionsPageViewModel
{
    public IReadOnlyList<Patient> Patients { get; set; } = [];
    public IReadOnlyList<ClinicalRecord> Records { get; set; } = [];
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = [];
    public PrintLayout PrescriptionLayout { get; set; } = PrintLayout.Default("Prescription");
    public PrescriptionFormModel NewPrescription { get; set; } = new();
    public IReadOnlyList<string> MedicationSuggestions { get; set; } = [];
    public string? DataNotice { get; set; }
}
